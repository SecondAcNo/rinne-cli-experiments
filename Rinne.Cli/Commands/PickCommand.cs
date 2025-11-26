using Rinne.Cli.Commands.Interfaces;
using Rinne.Core.Common;
using Rinne.Core.Features.Cas.Services;
using Rinne.Core.Features.Space;

namespace Rinne.Cli.Commands;

public sealed class PickCommand : ICliCommand
{
    public string Name => "pick";
    public IEnumerable<string> Aliases => Array.Empty<string>();
    public string Summary =>
        "Pick a file or directory from a logical snapshot (manifest+store) into the working filesystem.";

    public string Usage => """
        Usage:
          rinne pick [<space>] <snapshot-id|@N> <selector> <out-path>

        Arguments:
          <space>        Space name. If omitted, the current space is used
                         (from '.rinne/snapshots/current').
          <snapshot-id>  Snapshot id or unique id prefix, or @N (N-th newest snapshot; @0 is latest).
                         @N counts all snapshots in the space, physical or logical.
          <selector>     Path inside the snapshot (file or directory).
          <out-path>     Destination file or directory path on the local filesystem.

        Notes:
          - This command operates on logical (compact) snapshots via manifest+store.
            If the selected snapshot is physical-only (no manifest), the command fails
            with "manifest not found for snapshot".
          - On PowerShell you may need to quote @N:
              rinne pick '@0' Assets/Player.prefab out/Player.prefab
        """;

    private readonly RinnePaths _paths = new(Environment.CurrentDirectory);

    public async Task<int> RunAsync(string[] args, CancellationToken ct)
    {
        if (args.Length < 3 || args.Length > 4)
        {
            Console.WriteLine(Usage);
            return 2;
        }

        string? spaceArg = null;
        string snapshotId;
        string selector;
        string outPath;

        if (args.Length == 3)
        {
            snapshotId = args[0];
            selector = args[1];
            outPath = args[2];
        }
        else
        {
            spaceArg = args[0];
            snapshotId = args[1];
            selector = args[2];
            outPath = args[3];
        }

        if ((spaceArg is not null && spaceArg.StartsWith("--", StringComparison.Ordinal)) ||
            snapshotId.StartsWith("--", StringComparison.Ordinal) ||
            selector.StartsWith("--", StringComparison.Ordinal) ||
            outPath.StartsWith("--", StringComparison.Ordinal))
        {
            Console.Error.WriteLine("options are not supported for this command.");
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

        string snapshotDir;
        try
        {
            snapshotDir = ResolveSnapshotDir(spaceDir, snapshotId);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 2;
        }

        var snapId = Path.GetFileName(snapshotDir);
        var manifestPath = _paths.StoreManifest(snapId);
        if (!File.Exists(manifestPath))
        {
            Console.Error.WriteLine(
                $"manifest not found for snapshot (logical/compact snapshot only): {snapId}");
            return 2;
        }

        var service = new HydrateService(_paths);

        try
        {
            await service.HydratePickAsync(
                manifestPath: manifestPath,
                selector: selector,
                outputPath: outPath,
                workers: 0,
                ct: ct);

            Console.WriteLine($"picked from {snapId}: {selector} -> {outPath}");
            return 0;
        }
        catch (FileNotFoundException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("pick canceled.");
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"pick failed: {ex.Message}");
            return 1;
        }
    }

    private string ResolveSnapshotDir(string spaceDir, string idOrAt)
    {
        if (idOrAt.StartsWith("@", StringComparison.Ordinal) &&
            int.TryParse(idOrAt.AsSpan(1), out int n) &&
            n >= 0)
        {
            var snaps = Directory.GetDirectories(spaceDir)
                                 .OrderByDescending(x => x)
                                 .ToList();

            if (snaps.Count == 0)
                throw new ArgumentException($"no snapshots in space: {spaceDir}");

            if (n >= snaps.Count)
                throw new ArgumentException(
                    $"no snapshot matches '{idOrAt}' (only {snaps.Count} snapshots).");

            return snaps[n];
        }

        var hits = new List<string>();

        foreach (var dir in Directory.EnumerateDirectories(spaceDir))
        {
            var name = Path.GetFileName(dir);
            if (name.StartsWith(idOrAt, StringComparison.OrdinalIgnoreCase))
            {
                hits.Add(dir);
            }
        }

        if (hits.Count == 0)
            throw new ArgumentException($"no snapshot matches '{idOrAt}'");

        if (hits.Count > 1)
        {
            var show = string.Join(", ", hits.Take(5).Select(Path.GetFileName));
            throw new ArgumentException(
                $"ambiguous '{idOrAt}': {show}{(hits.Count > 5 ? "…" : "")}");
        }

        return hits[0];
    }
}
