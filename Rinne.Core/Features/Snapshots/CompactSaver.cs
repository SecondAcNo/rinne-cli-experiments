using Rinne.Core.Common;
using Rinne.Core.Config;
using Rinne.Core.Features.Cas.Chunking;
using Rinne.Core.Features.Cas.Storage;
using Rinne.Core.Features.FileCache;
using Rinne.Core.Features.Meta;
using Rinne.Core.Features.Notes;
using System.Buffers;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace Rinne.Core.Features.Snapshots;

public static class CompactSaver
{
    public sealed record CompactParams(int AvgMiB = 4, int MinKiB = 32, int MaxMiB = 8, int ZstdLevel = 4, int Workers = 0);

    public enum HashMode { Full, None }

    public static SnapshotResult SaveCompact(
        SnapshotOptions opt,
        CompactParams? cp = null,
        bool fullHashCheck = false,
        HashMode hashMode = HashMode.Full
        )
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

        try
        {
            Directory.CreateDirectory(targetDir);

            using (var fileMetaDb = SpaceFileMetaDb.Open(fileMetaDbPath))
            {
                RunManifestLiteAsync(
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
            string hashAlg;
            string hashHex;
            long fileCount;
            long totalBytes;

            if (hashMode == HashMode.Full)
            {
                var plannedTriples = plan.Files
                    .Select(f => (f.FullPath, Path.GetRelativePath(opt.SourceRoot, f.FullPath), f.Length));

                var items = SnapshotHash.ItemsFromPlan(plannedTriples, excludeMetaJson: false, excludeRinneDir: true);
                var res = SnapshotHash.Compute(items);

                hashAlg = "sha256";
                hashHex = res.HashHex;
                fileCount = res.FileCount;
                totalBytes = res.TotalBytes;
            }
            else
            {
                hashAlg = "skip";
                hashHex = "SKIP";
                fileCount = plan.Files.LongCount();
                totalBytes = plan.Files.Sum(f => f.Length);
            }

            var meta = new SnapshotMeta(
                Version: 1,
                HashAlgorithm: hashAlg,
                SnapshotHash: hashHex,
                FileCount: fileCount,
                TotalBytes: totalBytes
            );

            File.WriteAllText(Path.Combine(targetDir, "meta.json"),
                JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true }));


            return new SnapshotResult(
                    TargetDir: targetDir,
                    CopiedFiles: fileCount,
                    CopiedBytes: totalBytes,
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

    private sealed class FileEntry
    {
        public string RelativePath { get; }
        public long Bytes { get; set; }
        public List<string> ChunkHashes { get; } = new();
        public FileEntry(string rel) => RelativePath = rel;
    }

    private sealed record LiteManifest(
        string Version,
        string Root,
        string OriginalSha256,
        long TotalBytes,
        int AvgSizeBytes,
        int MinSizeBytes,
        int MaxSizeBytes,
        int Level,
        int FileCount,
        List<FileEntry> Files,
        List<string> Dirs);

    private static readonly object FileMetaDbLock = new();

    private static async Task RunManifestLiteAsync(
        IEnumerable<string> absoluteFiles,
        string inputDir,
        string storeDir,
        string manifestPath,
        int avgSizeBytes,
        int minSizeBytes,
        int maxSizeBytes,
        int level,
        int workers,
        IEnumerable<string>? includeDirs,
        SpaceFileMetaDb? fileMetaDb,
        bool fullHashCheck,
        CancellationToken ct)
    {
        if (!Directory.Exists(inputDir)) throw new DirectoryNotFoundException(inputDir);
        var inputAbs = Path.GetFullPath(inputDir);
        if (workers <= 0) workers = Math.Clamp(Environment.ProcessorCount, 1, 16);

        var allFiles = absoluteFiles
            .Select(p => Path.GetFullPath(p))
            .Where(p => p.StartsWith(inputAbs, StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToArray();

        long totalBytes = 0;
        foreach (var f in allFiles) totalBytes += new FileInfo(f).Length;

        var store = new ZstdContentAddressableStore(storeDir, level);
        var results = new ConcurrentDictionary<string, FileEntry>(StringComparer.Ordinal);

        var ch = Channel.CreateBounded<(string rel, int idx, byte[] buf, int len)>(
            new BoundedChannelOptions(Math.Max(256, Environment.ProcessorCount * 16))
            {
                SingleReader = false,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait,
            });

        Exception? err = null;
        var writer = ch.Writer;

        var producer = Task.Run(async () =>
        {
            try
            {
                await Parallel.ForEachAsync(
                    allFiles,
                    new ParallelOptions { MaxDegreeOfParallelism = workers, CancellationToken = ct },
                    async (path, token) =>
                    {
                        var rel = Path.GetRelativePath(inputAbs, path).Replace('\\', '/');
                        var fe = new FileEntry(rel);
                        results[rel] = fe;

                        var fi = new FileInfo(path);
                        var size = fi.Length;
                        var mtimeTicks = fi.LastWriteTimeUtc.Ticks;

                        if (fileMetaDb is not null && !fullHashCheck)
                        {
                            SpaceFileMeta? oldMeta;
                            lock (FileMetaDbLock)
                            {
                                oldMeta = fileMetaDb.TryGet(rel);
                            }

                            if (oldMeta is not null &&
                                oldMeta.Value.Size == size &&
                                oldMeta.Value.MtimeTicks == mtimeTicks &&
                                oldMeta.Value.ChunkHashes is { Count: > 0 })
                            {
                                bool allExist = true;
                                foreach (var h in oldMeta.Value.ChunkHashes)
                                {
                                    if (string.IsNullOrWhiteSpace(h) || !store.Exists(h))
                                    {
                                        allExist = false;
                                        break;
                                    }
                                }

                                if (allExist)
                                {
                                    fe.Bytes = size;
                                    foreach (var h in oldMeta.Value.ChunkHashes)
                                    {
                                        fe.ChunkHashes.Add(h);
                                    }
                                    return;
                                }
                            }
                        }

                        long bytes = 0;
                        int idx = 0;

                        using var fs = new FileStream(path, new FileStreamOptions
                        {
                            Mode = FileMode.Open,
                            Access = FileAccess.Read,
                            Share = FileShare.Read,
                            Options = FileOptions.SequentialScan | FileOptions.Asynchronous
                        });

                        using var sha256 = SHA256.Create();

                        await foreach (var chunk in FastCdcChunker.SplitAsync(
                            fs, avgSizeBytes, minSizeBytes, maxSizeBytes, token).ConfigureAwait(false))
                        {
                            bytes += chunk.Length;
                            sha256.TransformBlock(chunk.Bytes, 0, chunk.Length, null, 0);

                            var rented = ArrayPool<byte>.Shared.Rent(chunk.Length);
                            Buffer.BlockCopy(chunk.Bytes, 0, rented, 0, chunk.Length);
                            await writer.WriteAsync((rel, idx++, rented, chunk.Length), token).ConfigureAwait(false);
                        }

                        sha256.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                        var fileHashHex = Convert.ToHexString(sha256.Hash!);

                        fe.Bytes = bytes;

                        if (fileMetaDb is not null)
                        {
                            var nowTicks = DateTime.UtcNow.Ticks;
                            lock (FileMetaDbLock)
                            {
                                fileMetaDb.StageForUpdate(rel, size, mtimeTicks, fileHashHex, fe.ChunkHashes, nowTicks);
                            }
                        }
                    }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                err = ex;
            }
            finally
            {
                writer.TryComplete(err);
            }
        }, ct);

        var consumers = Enumerable.Range(0, Math.Max(1, workers)).Select(_ => Task.Run(async () =>
        {
            await foreach (var item in ch.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                var (rel, idx, buf, len) = item;
                try
                {
                    var fe = results[rel];
                    var hex = await store.PutIfAbsentAsync(buf.AsMemory(0, len), ct).ConfigureAwait(false);

                    lock (fe)
                    {
                        while (fe.ChunkHashes.Count <= idx) fe.ChunkHashes.Add(string.Empty);
                        fe.ChunkHashes[idx] = hex;
                    }

                    if (fileMetaDb is not null)
                    {
                        lock (FileMetaDbLock)
                        {
                            fileMetaDb.SetStagedChunkHash(rel, idx, hex);
                        }
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buf);
                }
            }
        }, ct)).ToArray();

        await Task.WhenAll(consumers.Prepend(producer)).ConfigureAwait(false);

        var files = results.Values.OrderBy(f => f.RelativePath, StringComparer.Ordinal).ToList();

        foreach (var fe in files)
        {
            if (fe.Bytes == 0)
            {
                fe.ChunkHashes.Clear();
                continue;
            }
            if (fe.ChunkHashes.Count == 0 || fe.ChunkHashes.Any(string.IsNullOrWhiteSpace))
                throw new InvalidOperationException($"Empty chunk hash detected in {fe.RelativePath}");
        }

        if (fileMetaDb is not null)
        {
            lock (FileMetaDbLock)
            {
                fileMetaDb.CommitStagedUpdates();
            }
        }

        var outDir = Path.GetDirectoryName(manifestPath);
        if (!string.IsNullOrEmpty(outDir)) Directory.CreateDirectory(outDir);

        List<string> dirs;
        if (includeDirs != null)
        {
            dirs = includeDirs
                .Select(d => Path.IsPathRooted(d) ? Path.GetFullPath(d) : Path.GetFullPath(Path.Combine(inputAbs, d)))
                .Select(p => Path.GetRelativePath(inputAbs, p))
                .Select(rel => rel.Replace('\\', '/'))
                .Where(rel => !string.IsNullOrEmpty(rel) && rel != ".")
                .Distinct(StringComparer.Ordinal)
                .OrderBy(rel => rel, StringComparer.Ordinal)
                .ToList();
        }
        else
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (var fe in files)
            {
                var rel = fe.RelativePath;
                for (var dir = Path.GetDirectoryName(rel); !string.IsNullOrEmpty(dir); dir = Path.GetDirectoryName(dir))
                {
                    var norm = dir.Replace('\\', '/');
                    set.Add(norm);
                }
            }
            dirs = set.OrderBy(rel => rel, StringComparer.Ordinal).ToList();
        }

        string overallHex;
        using (var ih = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
        {
            foreach (var fe in files.OrderBy(f => f.RelativePath, StringComparer.Ordinal))
            {
                AppendUtf8(ih, fe.RelativePath);
                AppendUtf8(ih, "\n");
                AppendUtf8(ih, fe.Bytes.ToString());
                AppendUtf8(ih, "\n");
                foreach (var chunkHash in fe.ChunkHashes)
                {
                    AppendUtf8(ih, chunkHash);
                    AppendUtf8(ih, "\n");
                }
            }
            overallHex = Convert.ToHexString(ih.GetHashAndReset());
        }

        var mani = new LiteManifest(
            Version: "cas:2",
            Root: inputAbs,
            OriginalSha256: overallHex,
            TotalBytes: totalBytes,
            AvgSizeBytes: avgSizeBytes,
            MinSizeBytes: minSizeBytes,
            MaxSizeBytes: maxSizeBytes,
            Level: level,
            FileCount: files.Count,
            Files: files,
            Dirs: dirs
        );

        await File.WriteAllTextAsync(
            manifestPath,
            JsonSerializer.Serialize(mani, new JsonSerializerOptions { WriteIndented = true }),
            ct).ConfigureAwait(false);
    }

    private static void AppendUtf8(IncrementalHash ih, string s)
    {
        var b = Encoding.UTF8.GetBytes(s);
        ih.AppendData(b);
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
