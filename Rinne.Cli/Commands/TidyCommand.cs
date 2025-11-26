using Rinne.Cli.Commands.Interfaces;
using Rinne.Core.Features.Space;
using Rinne.Core.Features.Tidy;
using Rinne.Core.Common;
using System.Globalization;

namespace Rinne.Cli.Commands;

public sealed class TidyCommand : ICliCommand
{
    public string Name => "tidy";
    public IEnumerable<string> Aliases => Array.Empty<string>();
    public string Summary => "Delete snapshots by selector (--keep/--before/--latest/--match), then run CAS GC.";

    public string Usage => """
            Usage:
              rinne tidy [<space>] <selector> [options...]
              rinne tidy --space <space> <selector> [options...]

            Selectors (exactly one; mutually exclusive):
              --keep N               Keep the latest N snapshots; delete older ones
              --latest N | --newest N
                                     Delete the newest N snapshots (opposite of --keep)
              --before YYYY-MM-DD    Delete snapshots older than the date (local midnight)
                                     (also accepts full ISO-8601; interpreted as UTC)
              --match GLOB           Delete snapshots whose id matches the glob (supports * and ?).
                                     Repeatable; you can pass comma-separated patterns too.

            Options:
              --dry-run | --dry      Preview only; do not delete or GC
              --no-gc                Skip CAS garbage collection (GC). Default: run GC.
                                     warn: --no-gc is deprecated and will be removed in the next release. 
            
              --space <space>        Explicit space; if omitted, use '.rinne/snapshots/current'

            Notes:
              - Exactly one selector must be specified.
              - Selectors cannot be combined.
            """;

    private readonly RinnePaths _paths = new(Environment.CurrentDirectory);

    public async Task<int> RunAsync(string[] args, CancellationToken ct)
    {
        if (args.Length < 1)
        {
            Console.WriteLine(Usage);
            return 2;
        }

        string? spaceArg = null;

        int? keep = null;
        int? latest = null;
        DateTimeOffset? before = null;
        var matchGlobs = new List<string>();

        bool dryRun = false;
        bool runGc = true;

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

                case "--keep":
                    keep = CliArgs.ParseNonNegativeInt(CliArgs.NeedValue(args, ref i, "--keep"), "--keep");
                    break;

                case "--latest":
                case "--newest":
                    latest = CliArgs.ParseNonNegativeInt(CliArgs.NeedValue(args, ref i, a), a);
                    break;

                case "--before":
                    {
                        var s = CliArgs.NeedValue(args, ref i, "--before");
                        if (DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dto))
                            before = dto.ToUniversalTime();
                        else if (DateTime.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                            before = DateUtil.ParseLocalDateAsUtcMidnight(s);
                        else
                            throw new FormatException($"Invalid --before value: {s}");
                        break;
                    }

                case "--match":
                    {
                        var v = CliArgs.NeedValue(args, ref i, "--match");
                        var parts = v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                        matchGlobs.AddRange(parts);
                        break;
                    }

                case "--dry-run":
                case "--dry":
                    dryRun = true;
                    break;

                case "--no-gc":
                    runGc = false;
                    break;

                default:
                    Console.Error.WriteLine($"unknown option: {a}");
                    Console.WriteLine(Usage);
                    return 2;
            }
        }

        var selectorCount = 0;
        if (keep is not null) selectorCount++;
        if (latest is not null) selectorCount++;
        if (before is not null) selectorCount++;
        if (matchGlobs.Count > 0) selectorCount++;

        if (selectorCount == 0)
        {
            Console.Error.WriteLine("one of --keep, --latest/--newest, --before, or --match is required.");
            Console.WriteLine(Usage);
            return 2;
        }

        if (selectorCount > 1)
        {
            Console.Error.WriteLine("exactly one of --keep, --latest/--newest, --before, or --match can be specified.");
            Console.WriteLine(Usage);
            return 2;
        }

        var spaceSvc = new SpaceService(_paths);
        string space;
        try
        {
            space = spaceArg ?? spaceSvc.GetCurrentSpaceFromPointer();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 2;
        }

        var service = new TidyService(_paths);
        var opt = new TidyService.Options(
            Space: space,
            Keep: keep,
            Latest: latest,
            Before: before,
            RunGc: runGc,
            DryRun: dryRun,
            MatchGlobs: matchGlobs.Count > 0 ? matchGlobs : null
        );

        try
        {
            var r = await service.RunAsync(opt, ct);

            if (dryRun)
            {
                Console.WriteLine("[dry-run] Tidy preview");
                Console.WriteLine($"  space      : {space}");
                Console.WriteLine($"  targets    : {r.TargetIds.Count} snapshot(s)");
                if (r.TargetIds.Count > 0)
                {
                    foreach (var id in r.TargetIds.Take(50))
                        Console.WriteLine($"    - {id}");
                    if (r.TargetIds.Count > 50)
                        Console.WriteLine($"    ... (+{r.TargetIds.Count - 50} more)");
                }

                if (runGc)
                {
                    Console.WriteLine("  GC (preview):");
                    Console.WriteLine($"    examined : {r.GcExamined}");
                    Console.WriteLine($"    deletable: {r.GcDeletable}");
                    Console.WriteLine($"    bytes    : {r.GcBytesFreed}");
                    if (r.GcCandidates is { Count: > 0 })
                    {
                        foreach (var c in r.GcCandidates.Take(30))
                            Console.WriteLine($"      - {c}");
                        if (r.GcCandidates.Count > 30)
                            Console.WriteLine($"      ... (+{r.GcCandidates.Count - 30} more)");
                    }
                }
                else
                {
                    Console.WriteLine("  GC         : skipped (--no-gc)");
                }
            }
            else
            {
                Console.WriteLine("Tidy done.");
                Console.WriteLine($"  space          : {space}");
                Console.WriteLine($"  snapshots del  : {r.SnapshotDirsDeleted}");
                Console.WriteLine($"  manifests  del : {r.ManifestsDeleted}");
                if (runGc)
                {
                    Console.WriteLine("  GC:");
                    Console.WriteLine($"    examined : {r.GcExamined}");
                    Console.WriteLine($"    deleted  : {r.GcDeletable}");
                    Console.WriteLine($"    bytes    : {r.GcBytesFreed}");
                }
                else
                {
                    Console.WriteLine("  GC         : skipped (--no-gc)");
                }
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"tidy failed: {ex.Message}");
            return 1;
        }
    }
}
