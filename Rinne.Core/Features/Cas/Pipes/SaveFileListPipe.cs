using Rinne.Core.Features.Cas.Chunking;
using Rinne.Core.Features.Cas.Hashing;
using Rinne.Core.Features.Cas.Storage;
using Rinne.Core.Features.FileCache;
using System.Buffers;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Channels;

namespace Rinne.Core.Features.Cas.Pipes;

public static class SaveFileListPipe
{
    private static readonly object FileMetaDbLock = new();

    public sealed class FileEntry
    {
        public string RelativePath { get; }
        public long Bytes { get; set; }
        public List<string> ChunkHashes { get; } = new();
        public FileEntry(string rel) => RelativePath = rel;
    }

    public sealed record Manifest(
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

    public static async Task<Manifest> RunAsync(
        IEnumerable<string> absoluteFiles,
        string inputDir,
        string storeDir,
        string manifestPath,
        int avgSizeBytes,
        int minSizeBytes,
        int maxSizeBytes,
        int level = 5,
        int workers = 0,
        IEnumerable<string>? includeDirs = null,
        SpaceFileMetaDb? fileMetaDb = null,
        bool fullHashCheck = false,
        CancellationToken ct = default)
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

        string overallHex = allFiles.Length == 0
            ? Convert.ToHexString(SHA256.HashData(Array.Empty<byte>()))
            : await Sha256Hasher.ComputeHexFromFilesAsync(allFiles, ct: ct).ConfigureAwait(false);

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

                        // ★ 再利用側：filemeta.db + store.Exists を使った安全なキャッシュヒット判定
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
                                    return; // DB と store の両方が整合している場合のみ I/O スキップ
                                }

                                // どれか欠けている → このファイルはキャッシュ破損扱いとし、
                                // 以降の通常フローで再chunk & CAS 書き込み & DB 更新を行う。
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

        var mani = new Manifest(
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

        return mani;
    }
}
