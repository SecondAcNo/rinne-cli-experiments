using System.Text.Json;

namespace Rinne.Core.Features.Cas.Pipes;

public static class ReferenceScannerPipe
{
    public static Dictionary<string, long> Analyze(IEnumerable<JsonDocument> manifests)
    {
        var dict = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        foreach (var doc in manifests)
        {
            if (!doc.RootElement.TryGetProperty("Version", out var verEl))
                continue;

            var ver = verEl.GetString() ?? string.Empty;
            if (!ver.StartsWith("cas:", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!doc.RootElement.TryGetProperty("Files", out var filesEl) ||
                filesEl.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var fe in filesEl.EnumerateArray())
            {
                if (!fe.TryGetProperty("ChunkHashes", out var chunksEl) ||
                    chunksEl.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var ch in chunksEl.EnumerateArray())
                {
                    var h = ch.GetString();
                    if (!IsHex64(h)) continue;

                    dict.TryGetValue(h!, out var n);
                    dict[h!] = n + 1;
                }
            }
        }
        return dict;
    }

    public static async Task<Dictionary<string, long>> RunAsync(
        string manifestPathOrDir,
        string outRefcountJson,
        CancellationToken ct = default)
    {
        var paths = CollectManifestPaths(manifestPathOrDir);
        if (paths.Count == 0)
            throw new FileNotFoundException("no manifest found", manifestPathOrDir);

        var docs = new List<JsonDocument>(capacity: Math.Min(paths.Count, 1024));
        try
        {
            foreach (var p in paths)
            {
                ct.ThrowIfCancellationRequested();
                using var fs = File.OpenRead(p);
                var doc = await JsonDocument.ParseAsync(fs, cancellationToken: ct).ConfigureAwait(false);
                docs.Add(doc);
            }

            var result = Analyze(docs);

            var dir = Path.GetDirectoryName(outRefcountJson);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            await File.WriteAllTextAsync(
                outRefcountJson,
                JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }),
                ct).ConfigureAwait(false);

            return result;
        }
        finally
        {
            foreach (var d in docs) d.Dispose();
        }
    }

    private static List<string> CollectManifestPaths(string pathOrDir)
    {
        var list = new List<string>();
        if (Directory.Exists(pathOrDir))
        {
            list.AddRange(Directory.EnumerateFiles(pathOrDir, "*.json", SearchOption.AllDirectories));
        }
        else if (File.Exists(pathOrDir))
        {
            list.Add(pathOrDir);
        }
        return list;
    }

    private static bool IsHex64(string? s)
    {
        if (string.IsNullOrEmpty(s) || s.Length != 64) return false;
        ReadOnlySpan<char> span = s.AsSpan();
        for (int i = 0; i < span.Length; i++)
        {
            char c = span[i];
            bool hex =
                (c >= '0' && c <= '9') ||
                (c >= 'A' && c <= 'F') ||
                (c >= 'a' && c <= 'f');
            if (!hex) return false;
        }
        return true;
    }
}
