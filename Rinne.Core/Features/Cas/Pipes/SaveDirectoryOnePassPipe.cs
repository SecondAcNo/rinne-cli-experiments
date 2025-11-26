using Rinne.Core.Features.Cas.Chunking;
using Rinne.Core.Features.Cas.Storage;
using Rinne.Core.Features.FileCache;
using System.Buffers;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace Rinne.Core.Features.Cas.Pipes
{
    public static class SaveDirectoryOnePassPipe
    {
        public static async Task RunAsync(
            string inputDir,
            string storeDir,
            string manifestPath,
            int avgSizeBytes,
            int minSizeBytes,
            int maxSizeBytes,
            int level,
            int workers,
            SpaceFileMetaDb? fileMetaDb,
            bool fullHashCheck,
            CancellationToken ct)
        {
            if (!Directory.Exists(inputDir)) throw new DirectoryNotFoundException(inputDir);
            var inputAbs = Path.GetFullPath(inputDir);
            if (workers <= 0) workers = Math.Clamp(Environment.ProcessorCount, 1, 32);

            var allFiles = Directory.EnumerateFiles(inputAbs, "*", SearchOption.AllDirectories)
                                    .OrderBy(p => p, StringComparer.Ordinal)
                                    .ToArray();

            long totalBytes = 0;
            foreach (var f in allFiles) totalBytes += new FileInfo(f).Length;

            var store = new ZstdContentAddressableStore(storeDir, level);
            var results = new ConcurrentDictionary<string, FileEntry>(StringComparer.Ordinal);

            var channel = Channel.CreateBounded<ChunkPacket>(new BoundedChannelOptions(Math.Max(256, Environment.ProcessorCount * 16))
            {
                SingleReader = false,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait
            });

            Exception? producerError = null;

            var producer = Task.Run(async () =>
            {
                try
                {
                    await Parallel.ForEachAsync(
                        allFiles,
                        new ParallelOptions { MaxDegreeOfParallelism = workers, CancellationToken = ct },
                        async (path, token) =>
                        {
                            token.ThrowIfCancellationRequested();

                            var rel = Path.GetRelativePath(inputAbs, path).Replace('\\', '/');
                            var fe = new FileEntry(rel);
                            results[rel] = fe;

                            var fi = new FileInfo(path);
                            var size = fi.Length;
                            var mtimeTicks = fi.LastWriteTimeUtc.Ticks;

                            if (fileMetaDb is not null && !fullHashCheck)
                            {
                                SpaceFileMeta? old;
                                lock (_fileMetaDbLock) { old = fileMetaDb.TryGet(rel); }

                                if (old is not null &&
                                    old.Value.Size == size &&
                                    old.Value.MtimeTicks == mtimeTicks &&
                                    old.Value.ChunkHashes is { Count: > 0 })
                                {
                                    bool allExist = true;
                                    foreach (var h in old.Value.ChunkHashes)
                                    {
                                        if (string.IsNullOrWhiteSpace(h) || !store.Exists(h))
                                        {
                                            allExist = false; break;
                                        }
                                    }
                                    if (allExist)
                                    {
                                        fe.Bytes = size;
                                        foreach (var h in old.Value.ChunkHashes) fe.ChunkHashes.Add(h);
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
                                token.ThrowIfCancellationRequested();

                                bytes += chunk.Length;
                                sha256.TransformBlock(chunk.Bytes, 0, chunk.Length, null, 0);

                                var rented = ArrayPool<byte>.Shared.Rent(chunk.Length);
                                Buffer.BlockCopy(chunk.Bytes, 0, rented, 0, chunk.Length);
                                await channel.Writer.WriteAsync(new ChunkPacket(rel, idx++, rented, chunk.Length), token)
                                                   .ConfigureAwait(false);
                            }

                            sha256.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                            var fileHashHex = Convert.ToHexString(sha256.Hash!);

                            fe.Bytes = bytes;

                            if (fileMetaDb is not null)
                            {
                                var nowTicks = DateTime.UtcNow.Ticks;
                                lock (_fileMetaDbLock)
                                {
                                    fileMetaDb.StageForUpdate(rel, size, mtimeTicks, fileHashHex, fe.ChunkHashes, nowTicks);
                                }
                            }
                        }).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    producerError = ex;
                }
                finally
                {
                    channel.Writer.TryComplete(producerError);
                }
            }, ct);

            var consumers = Enumerable.Range(0, Math.Max(1, workers)).Select(_ => Task.Run(async () =>
            {
                await foreach (var pkt in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
                {
                    try
                    {
                        var hex = await store.PutIfAbsentAsync(pkt.Buffer.AsMemory(0, pkt.Length), ct).ConfigureAwait(false);
                        if (!store.Exists(hex))
                            throw new IOException($"CAS write verification failed: {hex}");

                        var fe = results[pkt.RelativePath];
                        lock (fe)
                        {
                            while (fe.ChunkHashes.Count <= pkt.Index) fe.ChunkHashes.Add(string.Empty);
                            fe.ChunkHashes[pkt.Index] = hex;
                        }

                        if (fileMetaDb is not null)
                        {
                            lock (_fileMetaDbLock)
                            {
                                fileMetaDb.SetStagedChunkHash(pkt.RelativePath, pkt.Index, hex);
                            }
                        }
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(pkt.Buffer);
                    }
                }
            }, ct)).ToArray();

            await Task.WhenAll(consumers.Prepend(producer)).ConfigureAwait(false);
            if (producerError is not null) throw producerError;

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
                lock (_fileMetaDbLock) { fileMetaDb.CommitStagedUpdates(); }
            }

            var dirs = BuildDirs(files);

            string overallHex;
            using (var ih = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                foreach (var fe in files)
                {
                    AppendUtf8(ih, fe.RelativePath); AppendUtf8(ih, "\n");
                    AppendUtf8(ih, fe.Bytes.ToString()); AppendUtf8(ih, "\n");
                    foreach (var ch in fe.ChunkHashes) { AppendUtf8(ih, ch); AppendUtf8(ih, "\n"); }
                }
                overallHex = Convert.ToHexString(ih.GetHashAndReset());
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
                Files: files.Select(f => new ManifestFile(f.RelativePath, f.Bytes, f.ChunkHashes)).ToList(),
                Dirs: dirs
            );

            var outDir = Path.GetDirectoryName(manifestPath);
            if (!string.IsNullOrEmpty(outDir)) Directory.CreateDirectory(outDir);

            await File.WriteAllTextAsync(
                manifestPath,
                JsonSerializer.Serialize(mani, new JsonSerializerOptions { WriteIndented = true }),
                ct).ConfigureAwait(false);
        }

        private sealed class FileEntry
        {
            public string RelativePath { get; }
            public long Bytes { get; set; }
            public List<string> ChunkHashes { get; } = new();
            public FileEntry(string rel) => RelativePath = rel;
        }

        private sealed record Manifest(
            string Version,
            string Root,
            string OriginalSha256,
            long TotalBytes,
            int AvgSizeBytes,
            int MinSizeBytes,
            int MaxSizeBytes,
            int Level,
            int FileCount,
            List<ManifestFile> Files,
            List<string> Dirs
        );

        private sealed record ManifestFile(string RelativePath, long Bytes, List<string> ChunkHashes);

        private readonly struct ChunkPacket
        {
            public string RelativePath { get; }
            public int Index { get; }
            public byte[] Buffer { get; }
            public int Length { get; }
            public ChunkPacket(string rel, int index, byte[] buffer, int length)
            {
                RelativePath = rel; Index = index; Buffer = buffer; Length = length;
            }
        }

        private static readonly object _fileMetaDbLock = new();

        private static void AppendUtf8(IncrementalHash ih, string s)
            => ih.AppendData(Encoding.UTF8.GetBytes(s));

        private static List<string> BuildDirs(List<FileEntry> files)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (var fe in files)
            {
                var rel = fe.RelativePath;
                for (var dir = Path.GetDirectoryName(rel); !string.IsNullOrEmpty(dir); dir = Path.GetDirectoryName(dir))
                    set.Add(dir.Replace('\\', '/'));
            }
            return set.OrderBy(x => x, StringComparer.Ordinal).ToList();
        }
    }
}
