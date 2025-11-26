using Rinne.Cli.Commands.Interfaces;
using Rinne.Core.Common;
using Rinne.Core.Features.Cas.Models;
using Rinne.Core.Features.Cas.Services;
using Rinne.Core.Features.Space;
using System.Globalization;

namespace Rinne.Cli.Commands;

public sealed class CompactCommand : ICliCommand
{
    public string Name => "compact";

    public IEnumerable<string> Aliases => Array.Empty<string>();

    public string Summary =>
        "Convert full snapshots to Compact (dedup+zstd). Writes manifest to store/manifests/<id>.json and removes <id>/snapshots.";

    public string Usage => """
            Usage:
              rinne compact [<space>] --keep N [--full] [--speed]
              rinne compact [<space>] --latest N [--full] [--speed]
              rinne compact [<space>] --before YYYY-MM-DD [--full] [--speed]
              rinne compact --space <space> --keep N [--full] [--speed]
              rinne compact --space <space> --latest N [--full] [--speed]

            Description:
              - Reads <id>/snapshots/ for each selected snapshot.
              - Stores chunks under .rinne/store.
              - Writes manifest as .rinne/store/manifests/<id>.json.
              - On success, deletes <id>/snapshots/.

            Notes:
              - If <space> is not specified, the current space is read from:
                  .rinne/snapshots/current
              - --full forces full content verification and disables mtime/size-based skip.
              - --speed uses an experimental in-memory compact path.
              - --speed enables a high-performance mode that uses significantly more memory and is intended for systems with ample RAM.
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
        var full = false;
        var speed = false;

        for (int i = 0; i < args.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            var a = args[i];

            if (!a.StartsWith("--", StringComparison.Ordinal))
            {
                if (spaceArg is null)
                {
                    spaceArg = a;
                    continue;
                }
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
                    {
                        var s = CliArgs.NeedValue(args, ref i, "--keep");
                        keep = int.Parse(s, CultureInfo.InvariantCulture);
                        break;
                    }

                case "--latest":
                    {
                        var s = CliArgs.NeedValue(args, ref i, "--latest");
                        latest = int.Parse(s, CultureInfo.InvariantCulture);
                        break;
                    }

                case "--before":
                    {
                        var s = CliArgs.NeedValue(args, ref i, "--before");
                        if (DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture,
                            DateTimeStyles.AssumeUniversal, out var dto))
                        {
                            before = dto.ToUniversalTime();
                        }
                        else if (DateTime.TryParseExact(s, "yyyy-MM-dd",
                            CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                        {
                            var localMidnight = new DateTime(d.Year, d.Month, d.Day,
                                0, 0, 0, DateTimeKind.Local);
                            before = new DateTimeOffset(localMidnight).ToUniversalTime();
                        }
                        else
                        {
                            throw new FormatException($"Invalid --before value: {s}");
                        }
                        break;
                    }

                case "--full":
                    full = true;
                    break;

                case "--speed":
                    speed = true;
                    break;

                default:
                    Console.Error.WriteLine($"unknown option: {a}");
                    Console.WriteLine(Usage);
                    return 2;
            }
        }

        if ((keep is not null && latest is not null)
            || (keep is not null && before is not null)
            || (latest is not null && before is not null))
        {
            Console.Error.WriteLine("options --keep, --latest, --before are mutually exclusive.");
            Console.WriteLine(Usage);
            return 2;
        }

        if (keep is null && latest is null && before is null)
        {
            Console.Error.WriteLine("one of --keep, --latest, or --before is required.");
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

        var snaps = EnumerateSnapshots(spaceDir)
            .OrderByDescending(s => s.CreatedUtc)
            .ToList();

        if (snaps.Count == 0)
        {
            Console.WriteLine("(no snapshots)");
            return 0;
        }

        var targetIds = new HashSet<string>(StringComparer.Ordinal);

        if (keep is int k)
        {
            if (k < 0) k = 0;
            foreach (var s in snaps.Skip(k))
                targetIds.Add(s.Id);
        }

        if (latest is int l)
        {
            if (l < 0) l = 0;
            foreach (var s in snaps.Take(l))
                targetIds.Add(s.Id);
        }

        if (before is DateTimeOffset cutoff)
        {
            foreach (var s in snaps.Where(s => s.CreatedUtc < cutoff))
                targetIds.Add(s.Id);
        }

        var targets = snaps.Where(s => targetIds.Contains(s.Id)).ToList();

        if (targets.Count == 0)
        {
            Console.WriteLine("(no matches)");
            return 0;
        }

        Console.WriteLine("Compacting snapshots:");
        foreach (var s in targets)
            Console.WriteLine($"  {s.Id}  {s.CreatedUtc:yyyy-MM-dd HH:mm:ss 'UTC'}");

        Directory.CreateDirectory(_paths.StoreRoot);
        Directory.CreateDirectory(_paths.StoreManifests);

        const int avgMiB = 4;
        const int minKiB = 1024;
        const int maxMiB = 8;
        const int zstdLevel = 5;
        const int workers = 0;

        var opt = new CompactOptions(
            AvgMiB: avgMiB,
            MinKiB: minKiB,
            MaxMiB: maxMiB,
            ZstdLevel: zstdLevel,
            Workers: workers,
            FullHashCheck: full
        );

        if (speed)
        {
            //The amount of testing is not sufficient.
            var service = new CompactParallelMemoryService(_paths);
            await service.RunAsync(space, targets, opt, ct);
        }
        else
        {
            var service = new CompactParallelService(_paths);
            await service.RunAsync(space, targets, opt, ct);
        }

        return 0;
    }

    private static IEnumerable<SnapshotInfo> EnumerateSnapshots(string spaceDir)
    {
        foreach (var dir in Directory.EnumerateDirectories(spaceDir))
        {
            var name = Path.GetFileName(dir);
            yield return new SnapshotInfo(name, dir, ResolveCreatedUtcFromDirNameOrFs(dir, name));
        }
    }

    private static DateTimeOffset ResolveCreatedUtcFromDirNameOrFs(string dir, string name)
    {
        if (name.Length >= 17 && name[8] == 'T' && name[15] == 'Z')
        {
            var ts = name.Substring(0, 16);
            if (DateTime.TryParseExact(ts, "yyyyMMdd'T'HHmmss'Z'",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var dtA))
            {
                var utc = DateTime.SpecifyKind(dtA, DateTimeKind.Utc);
                return new DateTimeOffset(utc);
            }
        }

        var us = name.IndexOf('_');
        if (us > 0 && us + 1 < name.Length)
        {
            var datePart = name[(us + 1)..];
            if (DateTime.TryParseExact(datePart, "yyyyMMdd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var dtB))
            {
                var utcMidnight = DateTime.SpecifyKind(dtB, DateTimeKind.Utc);
                return new DateTimeOffset(utcMidnight);
            }
        }

        return new DateTimeOffset(Directory.GetLastWriteTimeUtc(dir));
    }
}
