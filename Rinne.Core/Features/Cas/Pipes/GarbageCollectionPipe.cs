using System.Text.Json;
using System.Text.RegularExpressions;

namespace Rinne.Core.Features.Cas.Pipes;

public static class GarbageCollectionPipe
{
    public sealed record GcError(string Path, string Message);
    public sealed record GcResult(
        long Examined,
        long Deletable,
        long BytesFreed,
        bool DryRun,
        IReadOnlyList<string> CandidatePaths,
        IReadOnlyList<GcError> Errors)
    {
        public double BytesFreedMiB => BytesFreed / (1024.0 * 1024.0);
    }

    private static readonly Regex Hex64Regex =
        new("^[A-F0-9]{64}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static async Task<GcResult> RunAsync(
        string storeDir,
        string refcountJson,
        bool dryRun = true,
        CancellationToken ct = default)
    {
        if (!Directory.Exists(storeDir))
            throw new DirectoryNotFoundException(storeDir);
        if (!File.Exists(refcountJson))
            throw new FileNotFoundException("refcount.json not found", refcountJson);

        var refcount = await LoadRefcountAsync(refcountJson, ct).ConfigureAwait(false);

        long examined = 0, deletable = 0, bytesFreed = 0;
        var errors = new List<GcError>();
        var candidates = new List<string>();

        foreach (var zst in Directory.EnumerateFiles(storeDir, "*.zst", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();

            var hash = Path.GetFileNameWithoutExtension(zst);
            if (!Hex64Regex.IsMatch(hash)) continue;

            examined++;

            if (!refcount.TryGetValue(hash, out var cnt) || cnt <= 0)
            {
                deletable++;
                try
                {
                    var fi = new FileInfo(zst);
                    bytesFreed += fi.Length;
                    candidates.Add(zst);
                    if (!dryRun) File.Delete(zst);
                }
                catch (Exception ex)
                {
                    errors.Add(new GcError(zst, ex.Message));
                }
            }
        }

        return new GcResult(examined, deletable, bytesFreed, dryRun, candidates, errors);
    }

    private static async Task<Dictionary<string, long>> LoadRefcountAsync(string path, CancellationToken ct)
    {
        var json = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        var map = JsonSerializer.Deserialize<Dictionary<string, long>>(json,
                      new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                  ?? new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        var norm = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in map)
        {
            var h = kv.Key?.Trim();
            if (string.IsNullOrEmpty(h)) continue;
            norm[h.ToUpperInvariant()] = kv.Value;
        }
        return norm;
    }
}
