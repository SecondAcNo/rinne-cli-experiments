using System.Text.Json;

namespace Rinne.Core.Features.Cas.Pipes;

public static class StatisticsPipe
{
    public sealed record Result(
        int ManifestCount,
        long FileCount,
        long OriginalBytes,
        long TotalChunkRefs,
        long UniqueChunks,
        long PresentChunks,
        long MissingChunks,
        long PresentBytes,
        double DedupFactor,
        double CompressionRatio,
        double AvgRefsPerFile,
        long[] SizeBins);

    private sealed record CoreStats(
        int ManifestCount,
        long FileCount,
        long OriginalBytes,
        long TotalChunkRefs,
        HashSet<string> UniqueHashes);

    private static CoreStats AnalyzeCore(IEnumerable<(long TotalBytes, IEnumerable<IEnumerable<string>> ChunkHashes)> manifests)
    {
        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int maniCount = 0;
        long fileCount = 0, origBytes = 0, refCount = 0;

        foreach (var (total, files) in manifests)
        {
            maniCount++;
            origBytes += total;

            foreach (var chunks in files)
            {
                fileCount++;
                foreach (var h in chunks)
                {
                    var hn = NormalizeHex64(h);
                    if (hn is null) continue;
                    refCount++;
                    unique.Add(hn);
                }
            }
        }
        return new CoreStats(maniCount, fileCount, origBytes, refCount, unique);
    }

    public static Result Analyze(
        IEnumerable<(long TotalBytes, IEnumerable<IEnumerable<string>> ChunkHashes)> manifests,
        string storeDir)
    {
        var core = AnalyzeCore(manifests); 

        long presentChunks = 0, missingChunks = 0, presentBytes = 0;
        var bins = new long[6];

        foreach (var h in core.UniqueHashes)
        {
            var zst = StorePath(storeDir, h);
            if (File.Exists(zst))
            {
                var sz = new FileInfo(zst).Length;
                presentBytes += sz;
                presentChunks++;
                bins[BinIndex(sz)]++;
            }
            else
            {
                missingChunks++;
            }
        }

        long uniqueChunks = core.UniqueHashes.Count;
        double dedup = uniqueChunks == 0 ? 0 : (double)core.TotalChunkRefs / uniqueChunks;
        double comp = presentBytes == 0 ? 0 : (double)core.OriginalBytes / presentBytes;
        double avg = core.FileCount == 0 ? 0 : (double)core.TotalChunkRefs / core.FileCount;

        return new Result(core.ManifestCount, core.FileCount, core.OriginalBytes, core.TotalChunkRefs,
                          uniqueChunks, presentChunks, missingChunks, presentBytes, dedup, comp, avg, bins);
    }

    public static async Task<Result> RunAsync(
        string manifestPathOrDir,
        string storeDir,
        CancellationToken ct = default)
    {
        var maniPaths = CollectManifestPaths(manifestPathOrDir);
        if (maniPaths.Count == 0)
            throw new InvalidOperationException("No manifest found.");

        var manifests = new List<(long, List<List<string>>)>();
        foreach (var path in maniPaths)
        {
            using var fs = File.OpenRead(path);
            using var doc = await JsonDocument.ParseAsync(fs, cancellationToken: ct).ConfigureAwait(false);

            if (!doc.RootElement.TryGetProperty("Version", out var vEl)) continue;
            var ver = vEl.GetString();
            if (string.IsNullOrEmpty(ver) || !ver.StartsWith("cas:", StringComparison.OrdinalIgnoreCase)) continue;

            long total = doc.RootElement.TryGetProperty("TotalBytes", out var tb) && tb.TryGetInt64(out var v) ? v : 0;
            var files = new List<List<string>>();

            if (doc.RootElement.TryGetProperty("Files", out var filesEl) && filesEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var fe in filesEl.EnumerateArray())
                {
                    if (!fe.TryGetProperty("ChunkHashes", out var chEl) || chEl.ValueKind != JsonValueKind.Array)
                        continue;

                    var list = new List<string>();
                    foreach (var c in chEl.EnumerateArray())
                    {
                        var h = c.GetString();
                        if (!string.IsNullOrWhiteSpace(h))
                            list.Add(h);
                    }
                    files.Add(list);
                }
            }

            manifests.Add((total, files));
        }

        var input = manifests.Select(m => (TotalBytes: m.Item1, ChunkHashes: (IEnumerable<IEnumerable<string>>)m.Item2));
        return Analyze(input, storeDir);
    }

    private static List<string> CollectManifestPaths(string pathOrDir)
    {
        var list = new List<string>();
        if (Directory.Exists(pathOrDir))
            list.AddRange(Directory.EnumerateFiles(pathOrDir, "*.json", SearchOption.AllDirectories));
        else if (File.Exists(pathOrDir))
            list.Add(pathOrDir);
        return list;
    }

    private static string StorePath(string storeDir, string hex)
    {
        var d1 = hex.Substring(0, 2);
        var d2 = hex.Substring(2, 2);
        return Path.Combine(storeDir, d1, d2, hex + ".zst");
    }

    private static int BinIndex(long bytes)
    {
        if (bytes < 64L * 1024) return 0;
        if (bytes < 256L * 1024) return 1;
        if (bytes < 1L * 1024 * 1024) return 2;
        if (bytes < 4L * 1024 * 1024) return 3;
        if (bytes < 16L * 1024 * 1024) return 4;
        return 5;
    }

    private static string? NormalizeHex64(string? s)
    {
        if (string.IsNullOrEmpty(s) || s.Length != 64) return null;

        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            bool ok = c >= '0' && c <= '9' ||
                      c >= 'A' && c <= 'F' ||
                      c >= 'a' && c <= 'f';
            if (!ok) return null;
        }
        return s.ToUpperInvariant();
    }
}
