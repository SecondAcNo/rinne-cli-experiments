using Rinne.Core.Common;
using Rinne.Core.Features.Cas.Models;
using Rinne.Core.Features.Cas.Pipes;
using Rinne.Core.Features.FileCache;

namespace Rinne.Core.Features.Cas.Services;

public sealed class CompactService
{
    private readonly RinnePaths _paths;

    public CompactService(RinnePaths paths) => _paths = paths;

    public sealed record Summary(int SuccessCount, int SkipCount, int FailCount);

    public async Task<Summary> RunAsync(string space, IReadOnlyList<SnapshotInfo> targets, CompactOptions opt, CancellationToken ct)
    {
        var storeDir = Path.Combine(_paths.RinneRoot, "store");
        var manifestsRoot = Path.Combine(storeDir, "manifests");
        Directory.CreateDirectory(storeDir);
        Directory.CreateDirectory(manifestsRoot);

        var spaceDir = Path.Combine(_paths.RinneRoot, "snapshots", "space", space);
        var fileMetaDbPath = Path.Combine(spaceDir, "filemeta.db");

        int ok = 0, skip = 0, fail = 0;

        using (var fileMetaDb = SpaceFileMetaDb.Open(fileMetaDbPath))
        {
            foreach (var s in targets)
            {
                ct.ThrowIfCancellationRequested();

                var payloadDir = Path.Combine(s.FullPath, "snapshots");
                if (!Directory.Exists(payloadDir))
                {
                    //Console.Error.WriteLine($"skip (payload not found): {s.Id}  (expected '{payloadDir}')");
                    skip++;
                    continue;
                }

                var manifestPath = Path.Combine(manifestsRoot, $"{s.Id}.json");
                var manifestTmp = manifestPath + ".tmp";

                TryDeleteFile(manifestTmp);

                try
                {
                    //Console.WriteLine($"compact: {s.Id} -> {Path.GetFileName(manifestPath)} ...");

                    await SaveDirectoryOnePassPipe.RunAsync(
                        inputDir: payloadDir,
                        storeDir: storeDir,
                        manifestPath: manifestTmp,
                        avgSizeBytes: opt.AvgMiB * 1024 * 1024,
                        minSizeBytes: opt.MinKiB * 1024,
                        maxSizeBytes: opt.MaxMiB * 1024 * 1024,
                        level: opt.ZstdLevel,
                        workers: opt.Workers,
                        fileMetaDb: fileMetaDb,
                        fullHashCheck: opt.FullHashCheck,
                        ct: ct);

#if NET8_0_OR_GREATER
                    File.Move(manifestTmp, manifestPath, overwrite: true);
#else
                    if (File.Exists(manifestPath)) File.Delete(manifestPath);
                    File.Move(manifestTmp, manifestPath);
#endif

                    TryDeleteDirectory(payloadDir);

                    //Console.WriteLine($"  ok: {s.Id}");
                    ok++;
                }
                catch (Exception ex)
                {
                    //Console.Error.WriteLine($"  fail: {s.Id} ({ex.Message})");
                    TryDeleteFile(manifestTmp);
                    fail++;
                }
            }
        }

        return new Summary(ok, skip, fail);
    }

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static void TryDeleteDirectory(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch { }
    }
}
