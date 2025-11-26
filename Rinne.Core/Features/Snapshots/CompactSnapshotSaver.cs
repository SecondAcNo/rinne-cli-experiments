using Rinne.Core.Common;
using Rinne.Core.Config;
using Rinne.Core.Features.Cas.Pipes;
using Rinne.Core.Features.FileCache;
using Rinne.Core.Features.Meta;
using Rinne.Core.Features.Snapshots;
using Rinne.Core.Features.Notes;
using System.Text.Json;

public static class CompactSnapshotSaver
{
    public sealed record CompactParams(int AvgMiB = 4, int MinKiB = 32, int MaxMiB = 8, int ZstdLevel = 4, int Workers = 0);

    public static SnapshotResult SaveCompact(SnapshotOptions opt, CompactParams? cp = null, bool fullHashCheck = false)
    {
        if (!Directory.Exists(opt.SourceRoot))
            throw new DirectoryNotFoundException($"source directory not found: {opt.SourceRoot}");
        var paths = new RinnePaths(opt.SourceRoot);

        if (!Directory.Exists(paths.RinneRoot))
            throw new InvalidOperationException($".rinne is missing. Run `rinne init` first: {paths.RinneRoot}");
        if (!Directory.Exists(paths.SnapshotsSpace(opt.Space)))
            throw new InvalidOperationException($"space '{opt.Space}' does not exist.");

        CleanupIncompleteSnapshots(paths, opt.Space);

        var cfg = ExcludeConfig.Load(paths.RinneIgnoreJson).WithDefaults();
        var ignorer = new Ignorer(cfg);
        var plan = CopyPlanner.PlanAll(opt.SourceRoot, ignorer);

        var snapshotId = SnapshotId.CreateUtc();
        var targetDir = paths.Snapshot(opt.Space, snapshotId);

        Directory.CreateDirectory(paths.StoreRoot);
        Directory.CreateDirectory(paths.StoreManifests);
        var manifestPath = paths.StoreManifest(snapshotId);
        var tmpManifest = manifestPath + ".tmp";
        TryDeleteFile(tmpManifest);

        var p = cp ?? new CompactParams();
        var workers = p.Workers > 0 ? p.Workers : Environment.ProcessorCount;

        var spaceDir = paths.SnapshotsSpace(opt.Space);
        var fileMetaDbPath = Path.Combine(spaceDir, "filemeta.db");

        SaveFileListPipe.Manifest mani;

        try
        {
            Directory.CreateDirectory(targetDir);

            using (var fileMetaDb = SpaceFileMetaDb.Open(fileMetaDbPath))
            {
                mani = SaveFileListPipe.RunAsync(
                    absoluteFiles: plan.Files.Select(x => x.FullPath),
                    inputDir: opt.SourceRoot,
                    storeDir: paths.StoreRoot,
                    manifestPath: tmpManifest,
                    avgSizeBytes: p.AvgMiB * 1024 * 1024,
                    minSizeBytes: p.MinKiB * 1024,
                    maxSizeBytes: p.MaxMiB * 1024 * 1024,
                    level: p.ZstdLevel,
                    workers: workers,
                    includeDirs: plan.Dirs.Where(d => d != "."),
                    fileMetaDb: fileMetaDb,
                    fullHashCheck: fullHashCheck,
                    ct: default
                ).GetAwaiter().GetResult();
            }

#if NET8_0_OR_GREATER
            File.Move(tmpManifest, manifestPath, overwrite: true);
#else
            if (File.Exists(manifestPath)) File.Delete(manifestPath);
            File.Move(tmpManifest, manifestPath);
#endif

            var plannedTriples = plan.Files
                .Select(f => (f.FullPath, Path.GetRelativePath(opt.SourceRoot, f.FullPath), f.Length));

            var items = SnapshotHash.ItemsFromPlan(plannedTriples, excludeMetaJson: false, excludeRinneDir: true);
            var res = SnapshotHash.Compute(items);

            var meta = new SnapshotMeta(
                Version: 1,
                HashAlgorithm: "sha256",
                SnapshotHash: res.HashHex,
                FileCount: res.FileCount,
                TotalBytes: res.TotalBytes
            );

            File.WriteAllText(Path.Combine(targetDir, "meta.json"),
                JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true }));

            return new SnapshotResult(
                TargetDir: targetDir,
                CopiedFiles: res.FileCount,
                CopiedBytes: res.TotalBytes,
                SkippedFiles: 0L,
                Errors: Array.Empty<string>()
            );
        }
        catch
        {
            SilentDelete(targetDir, recursive: true);
            throw;
        }
    }

    private static void CleanupIncompleteSnapshots(RinnePaths paths, string space)
    {
        var spaceDir = paths.SnapshotsSpace(space);
        if (!Directory.Exists(spaceDir))
            return;

        foreach (var dir in Directory.EnumerateDirectories(spaceDir))
        {
            try
            {
                var metaPath = Path.Combine(dir, "meta.json");
                var notePath = Path.Combine(dir, NoteService.DefaultFileName);

                var isOrphan =
                    !File.Exists(metaPath) ||
                    !File.Exists(notePath);

                if (isOrphan)
                {
                    Directory.Delete(dir, recursive: true);
                }
            }
            catch
            {
            }
        }
    }

    private static void TryDeleteFile(string p)
    {
        try { if (File.Exists(p)) File.Delete(p); }
        catch { }
    }

    private static void SilentDelete(string dir, bool recursive)
    {
        try
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive);
        }
        catch
        {
        }
    }
}
