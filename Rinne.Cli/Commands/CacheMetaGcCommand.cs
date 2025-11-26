using Rinne.Cli.Commands.Interfaces;
using Rinne.Core.Common;
using Rinne.Core.Features.FileCache;
using Rinne.Core.Features.Space;

namespace Rinne.Cli.Commands;

public sealed class CacheMetaGcCommand : ICliCommand
{
    public string Name => "cache-meta-gc";
    public IEnumerable<string> Aliases => Array.Empty<string>();
    public string Summary => "Garbage-collect stale rows in filemeta.db for a space.";

    public string Usage => """
            Usage:
              rinne cache-meta-gc [<space>] [options...]
              rinne cache-meta-gc --space <space> [options...]

            Options:
              --keep DAYS        Keep rows whose updated_at_ticks are within the last DAYS (default: 30)
                                 Rows older than this AND whose paths do not exist in the workspace
                                 are deleted from filemeta.db.
              --space <space>    Explicit space; if omitted, use '.rinne/snapshots/current'

            Notes:
              - This operates only on filemeta.db (SpaceFileMetaDbParallel).
              - filemeta.db is treated as a cache; deleting rows only forces re-hashing later.
            """;

    private readonly RinnePaths _paths = new(Environment.CurrentDirectory);

    public async Task<int> RunAsync(string[] args, CancellationToken ct)
    {
        if (args.Length < 0)
        {
            Console.WriteLine(Usage);
            return 2;
        }

        string? spaceArg = null;
        int keepDays = 30;

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
                    keepDays = CliArgs.ParseNonNegativeInt(
                        CliArgs.NeedValue(args, ref i, "--keep"), "--keep");
                    if (keepDays < 0)
                    {
                        Console.Error.WriteLine("--keep must be >= 0.");
                        return 2;
                    }
                    break;

                default:
                    Console.Error.WriteLine($"unknown option: {a}");
                    Console.WriteLine(Usage);
                    return 2;
            }
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

        var root = Environment.CurrentDirectory;
        var spaceDir = _paths.SnapshotsSpace(space);
        var fileMetaDbPath = Path.Combine(spaceDir, "filemeta.db");

        if (!File.Exists(fileMetaDbPath))
        {
            Console.WriteLine("cache-meta-gc:");
            Console.WriteLine($"  space      : {space}");
            Console.WriteLine("  filemeta   : not found (nothing to GC).");
            return 0;
        }

        var alivePaths = EnumerateAlivePaths(root, _paths.RinneRoot);

        var nowUtc = DateTime.UtcNow;
        var cutoffTicks = nowUtc.AddDays(-keepDays).Ticks;

        try
        {
            using var db = SpaceFileMetaDbParallel.Open(fileMetaDbPath);
            var deleted = db.GarbageCollect(alivePaths, cutoffTicks);

            Console.WriteLine("cache-meta-gc:");
            Console.WriteLine($"  space      : {space}");
            Console.WriteLine($"  keep(days) : {keepDays}");
            Console.WriteLine($"  deleted    : {deleted} row(s)");

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"cache-meta-gc failed: {ex.Message}");
            return 1;
        }
    }

    private static IEnumerable<string> EnumerateAlivePaths(string root, string rinneRoot)
    {
        var rootFull = Path.GetFullPath(root);
        var rinneFull = Path.GetFullPath(rinneRoot);

        return Directory.EnumerateFiles(rootFull, "*", SearchOption.AllDirectories)
            .Where(p =>
            {
                var full = Path.GetFullPath(p);
                if (full.StartsWith(rinneFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    return false;
                if (string.Equals(full, rinneFull, StringComparison.OrdinalIgnoreCase))
                    return false;
                return true;
            })
            .Select(p => Path.GetRelativePath(rootFull, p).Replace('\\', '/'));
    }
}
