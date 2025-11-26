using Rinne.Cli.Commands.Interfaces;
using Rinne.Core.Common;
using Rinne.Core.Features.Cas.Models;
using Rinne.Core.Features.Cas.Services;
using Rinne.Core.Features.Space;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Rinne.Cli.Commands;

public sealed class HydrateCommand : ICliCommand
{
    public string Name => "hydrate";
    public IEnumerable<string> Aliases => Array.Empty<string>();
    public string Summary => "Rebuild <id>/snapshots from store manifests (skip if payload already exists).";

    public string Usage => """
            Usage:
              rinne hydrate [<space>] [selector] [--rm-manifest]
              rinne hydrate --space <space> [selector] [--rm-manifest]

            Selectors (exactly one):
              --latest N             Take N newest
              --ago 30d              Select older than relative age (d, w, mo, y)
              --before YYYY-MM-DD    Select before absolute date (local midnight)
              --id 2025...           Unique id prefix (single)
              --match 202511*        Glob by id (* and ? supported)

            Options:
              --rm-manifest          Delete corresponding manifest after successful hydrate
                                     (skipped/failed IDs are not deleted)

            Notes:
              - If neither <space> nor --space is given, the command reads the current space from:
                  .rinne/snapshots/current  (single-line text)
              - Only hydrates IDs that do NOT have <id>/snapshots yet.
            """;

    private readonly RinnePaths _paths = new(Environment.CurrentDirectory);

    public async Task<int> RunAsync(string[] args, CancellationToken ct)
    {
        if (args.Length < 1) { Console.WriteLine(Usage); return 2; }

        string? spaceArg = null;

        int? latest = null;
        string? ago = null;
        DateTimeOffset? before = null;
        string? idPrefix = null;    // single only
        string? matchGlob = null;

        bool removeManifest = false;

        for (int i = 0; i < args.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            var a = args[i];

            if (!a.StartsWith("--", StringComparison.Ordinal))
            {
                if (spaceArg is null) { spaceArg = a; continue; }
                Console.Error.WriteLine($"unknown argument: {a}");
                Console.WriteLine(Usage);
                return 2;
            }

            switch (a)
            {
                case "--space":
                    spaceArg = CliArgs.NeedValue(args, ref i, "--space");
                    break;

                case "--latest":
                    latest = int.Parse(CliArgs.NeedValue(args, ref i, "--latest"), CultureInfo.InvariantCulture);
                    break;

                case "--ago":
                    ago = CliArgs.NeedValue(args, ref i, "--ago");
                    break;

                case "--before":
                    {
                        var s = CliArgs.NeedValue(args, ref i, "--before");
                        if (DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dto))
                            before = dto.ToUniversalTime();
                        else if (DateTime.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                        {
                            var localMidnight = new DateTime(d.Year, d.Month, d.Day, 0, 0, 0, DateTimeKind.Local);
                            before = new DateTimeOffset(localMidnight).ToUniversalTime();
                        }
                        else
                            throw new FormatException($"Invalid --before value: {s}");
                        break;
                    }

                case "--id":
                    idPrefix = CliArgs.NeedValue(args, ref i, "--id");
                    break;

                case "--match":
                    matchGlob = CliArgs.NeedValue(args, ref i, "--match");
                    break;

                case "--rm-manifest":
                case "--remove-manifest":
                    removeManifest = true;
                    break;

                default:
                    Console.Error.WriteLine($"unknown option: {a}");
                    Console.WriteLine(Usage);
                    return 2;
            }
        }

        // ---- selector count check: exactly one ----
        int selCount = 0;
        if (latest is not null) selCount++;
        if (!string.IsNullOrWhiteSpace(ago)) selCount++;
        if (before is not null) selCount++;
        if (!string.IsNullOrWhiteSpace(idPrefix)) selCount++;
        if (!string.IsNullOrWhiteSpace(matchGlob)) selCount++;

        if (selCount == 0)
        {
            Console.Error.WriteLine("exactly one selector is required.");
            Console.WriteLine(Usage);
            return 2;
        }
        if (selCount > 1)
        {
            Console.Error.WriteLine("selectors are mutually exclusive; specify exactly one.");
            Console.WriteLine(Usage);
            return 2;
        }

        var spaceSvc = new SpaceService(_paths);
        var space = spaceArg ?? spaceSvc.GetCurrentSpaceFromPointer();

        var spaceDir = _paths.SnapshotsSpace(space);
        if (!Directory.Exists(spaceDir))
        {
            Console.Error.WriteLine($"space not found: {space}");
            return 2;
        }

        var snaps = SnapshotSelector.Enumerate(spaceDir)
                                    .OrderByDescending(s => s.CreatedUtc)
                                    .ToList();
        if (snaps.Count == 0) { Console.WriteLine("(no snapshots)"); return 0; }

        // ---- single selection ----
        IEnumerable<SnapshotInfo> selected = Array.Empty<SnapshotInfo>();

        if (latest is int ln)
        {
            if (ln <= 0) { Console.WriteLine("(no matches)"); return 0; }
            selected = snaps.Take(ln);
        }
        else if (!string.IsNullOrWhiteSpace(ago))
        {
            var cutoff = UtcCutoffFromAge(ago);
            selected = SnapshotSelector.SelectBefore(snaps, cutoff);
        }
        else if (before is not null)
        {
            selected = SnapshotSelector.SelectBefore(snaps, before.Value);
        }
        else if (!string.IsNullOrWhiteSpace(idPrefix))
        {
            selected = ResolveByIdPrefixes(snaps, new[] { idPrefix });
        }
        else if (!string.IsNullOrWhiteSpace(matchGlob))
        {
            var rx = Glob.ToRegex(matchGlob);
            selected = snaps.Where(s => rx.IsMatch(s.Id));
        }

        var targets = selected
            .Where(s => !Directory.Exists(Path.Combine(s.FullPath, "snapshots")))
            .ToList();

        if (targets.Count == 0) { Console.WriteLine("(no matches)"); return 0; }

        Console.WriteLine("Hydrating snapshots:");
        foreach (var s in targets)
            Console.WriteLine($"  {s.Id}  {s.CreatedUtc:yyyy-MM-dd HH:mm:ss 'UTC'}");

        var service = new HydrateService(_paths);
        var summary = await service.RunAsync(space, targets, workers: 0, removeManifest: removeManifest, ct: ct);

        Console.WriteLine($"done. success={summary.SuccessCount} skip={summary.SkipCount} fail={summary.FailCount}");
        return summary.FailCount > 0 ? 1 : 0;
    }

    private static DateTimeOffset UtcCutoffFromAge(string s)
    {
        var m = Regex.Match(s, @"^\s*(\d+)\s*(d|w|mo|y)\s*$", RegexOptions.IgnoreCase);
        if (!m.Success) throw new FormatException("age must be like 7d, 2w, 3mo, 1y");
        int v = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        var now = DateTimeOffset.UtcNow;
        return m.Groups[2].Value.ToLowerInvariant() switch
        {
            "d" => now.AddDays(-v),
            "w" => now.AddDays(-7 * v),
            "mo" => now.AddMonths(-v),
            "y" => now.AddYears(-v),
            _ => now
        };
    }

    private static IEnumerable<SnapshotInfo> ResolveByIdPrefixes(IEnumerable<SnapshotInfo> snaps, IEnumerable<string> prefixes)
    {
        foreach (var p in prefixes)
        {
            var hits = snaps.Where(s => s.Id.StartsWith(p, StringComparison.OrdinalIgnoreCase)).ToList();
            if (hits.Count == 0) throw new ArgumentException($"no snapshot matches '{p}'");
            if (hits.Count > 1)
            {
                var show = string.Join(", ", hits.Take(5).Select(h => h.Id));
                throw new ArgumentException($"ambiguous '{p}': {show}{(hits.Count > 5 ? "…" : "")}");
            }
            yield return hits[0];
        }
    }
}
