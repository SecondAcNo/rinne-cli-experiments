using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Rinne.Core.Common;

namespace Rinne.Core.Features.Diff;

public sealed class DiffService
{
    private readonly RinnePaths _paths;
    public DiffService(RinnePaths paths) => _paths = paths;

    public sealed record Options(bool UseContentHash = false, long MaxHashBytes = long.MaxValue);

    public sealed record FileItem(string Path, long Length, string? Fingerprint);
    public enum ChangeKind { Added, Removed, Modified, Renamed, Unchanged }
    public sealed record Change(ChangeKind Kind, string PathA, string PathB);
    public sealed record Result(string IdA, string IdB, IReadOnlyList<Change> Changes, int Added, int Removed, int Modified, int Renamed, int Unchanged);

    public async Task<Result> DiffAsync(string space, string idA, string idB, Options opt, CancellationToken ct)
    {
        var invA = await BuildInventoryAsync(space, idA, opt, ct);
        var invB = await BuildInventoryAsync(space, idB, opt, ct);

        var cmp = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var mapA = invA.ToDictionary(x => x.Path, cmp);
        var mapB = invB.ToDictionary(x => x.Path, cmp);

        var added = new List<Change>();
        var removed = new List<Change>();
        var modified = new List<Change>();
        var unchanged = new List<Change>();

        foreach (var kv in mapA)
        {
            ct.ThrowIfCancellationRequested();
            var path = kv.Key;
            var fa = kv.Value;
            if (!mapB.TryGetValue(path, out var fb))
            {
                removed.Add(new Change(ChangeKind.Removed, path, ""));
                continue;
            }

            var same = fa.Length == fb.Length;
            if (same && opt.UseContentHash && fa.Fingerprint is not null && fb.Fingerprint is not null)
                same = string.Equals(fa.Fingerprint, fb.Fingerprint, StringComparison.OrdinalIgnoreCase);

            if (same) unchanged.Add(new Change(ChangeKind.Unchanged, path, path));
            else modified.Add(new Change(ChangeKind.Modified, path, path));
        }

        foreach (var kv in mapB)
        {
            ct.ThrowIfCancellationRequested();
            var path = kv.Key;
            if (!mapA.ContainsKey(path))
                added.Add(new Change(ChangeKind.Added, "", path));
        }

        added.Sort((a, b) => string.Compare(a.PathB, b.PathB, StringComparison.Ordinal));
        removed.Sort((a, b) => string.Compare(a.PathA, b.PathA, StringComparison.Ordinal));
        modified.Sort((a, b) => string.Compare(a.PathA, b.PathA, StringComparison.Ordinal));
        unchanged.Sort((a, b) => string.Compare(a.PathA, b.PathA, StringComparison.Ordinal));

        var all = new List<Change>(added.Count + removed.Count + modified.Count + unchanged.Count);
        all.AddRange(added);
        all.AddRange(removed);
        all.AddRange(modified);
        all.AddRange(unchanged);

        return new Result(idA, idB, all, added.Count, removed.Count, modified.Count, 0, unchanged.Count);
    }

    private async Task<IReadOnlyList<FileItem>> BuildInventoryAsync(string space, string id, Options opt, CancellationToken ct)
    {
        var manifestPath = _paths.StoreManifest(id);
        if (File.Exists(manifestPath))
        {
            var mani = await TryLoadFromManifestAsync(manifestPath, ct);
            if (mani is not null)
            {
                var list = new List<FileItem>(mani.Count);
                foreach (var m in mani)
                {
                    ct.ThrowIfCancellationRequested();
                    var key = CanonManifestPath(id, m.Path);
                    if (string.IsNullOrEmpty(key)) continue;
                    list.Add(new FileItem(key, m.Length, m.Fingerprint));
                }
                var filtered = FilterOutSidecars(list);
                return Dedup(filtered);
            }
        }

        var payloadRoot = _paths.SnapshotPayload(space, id);
        if (!Directory.Exists(payloadRoot))
            throw new FileNotFoundException($"inventory not available: {id}");

        var fromPayload = await LoadFromPayloadAsync(payloadRoot, opt, ct);
        return Dedup(fromPayload);
    }

    private static string CanonManifestPath(string id, string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        var p = raw.Replace('\\', '/').Trim();
        if (p.StartsWith("/")) p = p[1..];
        var needle = "/" + id + "/";
        var i = p.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
        if (i >= 0) p = p[(i + needle.Length)..];
        else if (p.StartsWith(id + "/", StringComparison.OrdinalIgnoreCase)) p = p[(id.Length + 1)..];
        const string snap = "snapshots/";
        if (p.StartsWith(snap, StringComparison.OrdinalIgnoreCase)) p = p[snap.Length..];
        return p.Normalize(NormalizationForm.FormC);
    }

    private static List<FileItem> FilterOutSidecars(List<FileItem> items)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return items
            .Where(x => !string.Equals(x.Path, "meta.json", comparison) &&
                        !string.Equals(x.Path, "note.md", comparison))
            .ToList();
    }

    private static IReadOnlyList<FileItem> Dedup(List<FileItem> items)
    {
        var cmp = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var dict = new Dictionary<string, FileItem>(cmp);
        foreach (var it in items)
        {
            if (!dict.TryGetValue(it.Path, out var cur))
            {
                dict[it.Path] = it;
                continue;
            }
            var ra = it.Fingerprint is not null ? 2 : 1;
            var rb = cur.Fingerprint is not null ? 2 : 1;
            if (ra > rb) dict[it.Path] = it;
            else if (ra == rb && it.Length >= cur.Length) dict[it.Path] = it;
        }
        return dict.Values.ToList();
    }

    private static async Task<List<FileItem>> LoadFromPayloadAsync(string payloadRoot, Options opt, CancellationToken ct)
    {
        var eo = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            ReturnSpecialDirectories = false,
            AttributesToSkip = 0,
            IgnoreInaccessible = true,
            MatchCasing = MatchCasing.PlatformDefault
        };
        var files = Directory.EnumerateFiles(payloadRoot, "*", eo).Select(p => new FileInfo(p)).ToArray();

        var list = new ConcurrentBag<FileItem>();
        if (!opt.UseContentHash)
        {
            foreach (var fi in files)
            {
                ct.ThrowIfCancellationRequested();
                list.Add(new FileItem(Rel(payloadRoot, fi.FullName), fi.Length, null));
            }
            return list.ToList();
        }

        await Parallel.ForEachAsync(files, new ParallelOptions { CancellationToken = ct }, async (fi, ct2) =>
        {
            var rel = Rel(payloadRoot, fi.FullName);
            var len = fi.Length;
            string? fp = null;
            if (len <= opt.MaxHashBytes)
                fp = await ComputeSha256Async(fi.FullName, ct2);
            list.Add(new FileItem(rel, len, fp));
        });

        return list.ToList();
    }

    private static string Rel(string root, string full)
    {
        var r = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(full));
        return r.Replace('\\', '/').Normalize(NormalizationForm.FormC);
    }

    private static async Task<string> ComputeSha256Async(string fullPath, CancellationToken ct)
    {
        using var sha = SHA256.Create();
        await using var fs = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.SequentialScan);
        var buf = new byte[1 << 16];
        int n;
        while ((n = await fs.ReadAsync(buf.AsMemory(0, buf.Length), ct)) > 0)
            sha.TransformBlock(buf, 0, n, null, 0);
        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return Convert.ToHexString(sha.Hash!);
    }

    private sealed record ManiItem(string Path, long Length, string? Fingerprint);

    private static async Task<IReadOnlyList<ManiItem>?> TryLoadFromManifestAsync(string manifestPath, CancellationToken ct)
    {
        await using var fs = File.OpenRead(manifestPath);
        using var doc = await JsonDocument.ParseAsync(fs, cancellationToken: ct);
        var root = doc.RootElement;

        if (TryGetArrayCI(root, out var filesArr, "Files", "files"))
            return ParseFilesArray(filesArr);

        if (root.ValueKind == JsonValueKind.Array)
            return ParseFilesArray(root);

        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in root.EnumerateObject())
            {
                if (p.Value.ValueKind == JsonValueKind.Array)
                {
                    var l = ParseFilesArray(p.Value);
                    if (l is not null) return l;
                }
                if (p.Value.ValueKind == JsonValueKind.Object)
                {
                    foreach (var p2 in p.Value.EnumerateObject())
                    {
                        if (p2.Value.ValueKind == JsonValueKind.Array)
                        {
                            var l = ParseFilesArray(p2.Value);
                            if (l is not null) return l;
                        }
                    }
                }
            }
        }

        return null;

        static List<ManiItem>? ParseFilesArray(JsonElement arr)
        {
            if (arr.ValueKind != JsonValueKind.Array) return null;
            var list = new List<ManiItem>(Math.Max(16, arr.GetArrayLength()));
            foreach (var e in arr.EnumerateArray())
            {
                if (e.ValueKind != JsonValueKind.Object) return null;
                var rel = GetStringCI(e, "RelativePath", "Path", "relPath", "name", "rel", "relativepath");
                if (string.IsNullOrEmpty(rel)) return null;
                var len = GetInt64CI(e, "Bytes", "Size", "Length", "TotalBytes", "bytes", "size", "length", "totalbytes");
                if (len is null || len < 0) return null;
                var fp = GetStringCI(e, "sha256", "hash", "fingerprint");
                list.Add(new ManiItem(rel, len.Value, string.IsNullOrEmpty(fp) ? null : fp));
            }
            return list;
        }

        static string? GetStringCI(JsonElement obj, params string[] names)
        {
            if (obj.ValueKind != JsonValueKind.Object) return null;
            foreach (var n in names)
                if (obj.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String)
                    return v.GetString();
            foreach (var n in names)
                if (obj.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number)
                    return v.GetRawText();
            return null;
        }

        static long? GetInt64CI(JsonElement obj, params string[] names)
        {
            if (obj.ValueKind != JsonValueKind.Object) return null;
            foreach (var n in names)
                if (obj.TryGetProperty(n, out var v))
                {
                    if (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var i64))
                        return i64;
                    if (v.ValueKind == JsonValueKind.String)
                    {
                        var s = v.GetString();
                        if (!string.IsNullOrEmpty(s) && long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i64s))
                            return i64s;
                    }
                }
            return null;
        }

        static bool TryGetArrayCI(JsonElement obj, out JsonElement arr, params string[] names)
        {
            if (obj.ValueKind == JsonValueKind.Object)
            {
                foreach (var p in obj.EnumerateObject())
                {
                    foreach (var n in names)
                    {
                        if (p.Name.Equals(n, StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.Array)
                        {
                            arr = p.Value;
                            return true;
                        }
                    }
                }
            }
            arr = default;
            return false;
        }
    }
}
