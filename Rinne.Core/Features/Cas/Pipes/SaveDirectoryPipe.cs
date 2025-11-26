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

public static class SaveDirectoryPipe
{
    private static readonly object FileMetaDbLock = new();

    public sealed class FileEntry
    {
        public string RelativePath { get; }
        public long Bytes { get; set; }
        public List<string> ChunkHashes { get; } = new();
        public FileEntry(string relativePath) => RelativePath = relativePath;
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
        string inputDir,
        string storeDir,
        string manifestPath,
        int avgSizeBytes,
        int minSizeBytes,
        int maxSizeBytes,
        int level = 5,
        int workers = 0,
        SpaceFileMetaDb? fileMetaDb = null,
        bool fullHashCheck = false,
        CancellationToken ct = default)
    {
        if (!Directory.Exists(inputDir))
            throw new DirectoryNotFoundException(inputDir);

        var inputDirAbs = Path.GetFullPath(inputDir);
        if (workers <= 0) workers = Math.Clamp(Environment.ProcessorCount, 1, 16);

        var allFiles = Directory.EnumerateFiles(inputDirAbs, "*", SearchOption.AllDirectories)
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

        var writer = ch.Writer;
        Exception? err = null;

        // プロデューサ：ファイルから chunk を作ってチャネルへ流す
        var producer = Task.Run(async () =>
        {
            try
            {
                await Parallel.ForEachAsync(
                    allFiles,
                    new ParallelOptions { MaxDegreeOfParallelism = workers, CancellationToken = ct },
                    async (path, token) =>
                    {
                        var rel = Path.GetRelativePath(inputDirAbs, path).Replace('\\', '/');

                        var fe = new FileEntry(rel);
                        results[rel] = fe;

                        var fi = new FileInfo(path);
                        long size = fi.Length;
                        long mtimeTicks = fi.LastWriteTimeUtc.Ticks;

                        // ★ 再利用側：filemeta.db + store.Exists による安全なキャッシュ判定
                        if (fileMetaDb is not null && !fullHashCheck)
                        {
                            SpaceFileMeta? meta;
                            lock (FileMetaDbLock)
                            {
                                meta = fileMetaDb.TryGet(rel);
                            }

                            if (meta is not null &&
                                meta.Value.Size == size &&
                                meta.Value.MtimeTicks == mtimeTicks &&
                                meta.Value.ChunkHashes is { Count: > 0 })
                            {
                                bool allExist = true;
                                foreach (var h in meta.Value.ChunkHashes)
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
                                    foreach (var h in meta.Value.ChunkHashes)
                                    {
                                        fe.ChunkHashes.Add(h);
                                    }
                                    return; // DB + store が整合している場合のみ I/O スキップ
                                }

                                // どれか欠けている → このファイルのキャッシュは破損として、
                                // 以降の通常フローで再chunk & CAS 書き込み & DB 更新を行う。
                            }
                        }

                        // 2. ここに来たら「DB に無い」or「サイズ/mtime 不一致」or「fullHashCheck あり」。
                        long bytes = 0;
                        int idx = 0;
                        var chunkHashes = new List<string>();

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

                            chunkHashes.Add(string.Empty);
                        }

                        sha256.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                        var fileHashHex = Convert.ToHexString(sha256.Hash!);

                        fe.Bytes = bytes;

                        if (fileMetaDb is not null)
                        {
                            var nowTicks = DateTime.UtcNow.Ticks;
                            lock (FileMetaDbLock)
                            {
                                fileMetaDb.StageForUpdate(rel, size, mtimeTicks, fileHashHex, chunkHashes, nowTicks);
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

        // コンシューマ：チャネルから chunk を読み出して store に PutIfAbsent
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
                throw new InvalidDataException($"Empty chunk hash detected in {fe.RelativePath}");
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

        var dirs = Directory.EnumerateDirectories(inputDirAbs, "*", SearchOption.AllDirectories)
                            .Select(d => Path.GetRelativePath(inputDirAbs, d).Replace('\\', '/'))
                            .Where(rel => rel.Length > 0 && rel != ".")
                            .OrderBy(rel => rel, StringComparer.Ordinal)
                            .ToList();

        var mani = new Manifest(
            Version: "cas:2",
            Root: inputDirAbs,
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
