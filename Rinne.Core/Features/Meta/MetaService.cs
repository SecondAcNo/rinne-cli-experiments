using System.Text.Json;
using System.Text.Json.Serialization;
using Rinne.Core.Common;
using Rinne.Core.Features.Snapshots;

namespace Rinne.Core.Features.Meta;

public sealed record SnapshotMeta(
    [property: JsonPropertyName("v")] int Version,
    [property: JsonPropertyName("hashAlg")] string HashAlgorithm,
    [property: JsonPropertyName("snapshotHash")] string SnapshotHash,
    [property: JsonPropertyName("fileCount")] long FileCount,
    [property: JsonPropertyName("totalBytes")] long TotalBytes
);

public sealed class MetaService
{
    private const int CurrentVersion = 1;
    private const string HashAlgorithmName = "sha256";

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = null,
        WriteIndented = true
    };

    public SnapshotMeta WriteMeta(string snapshotRoot, CancellationToken ct = default)
    {
        if (snapshotRoot is null) throw new ArgumentNullException(nameof(snapshotRoot));
        if (!Directory.Exists(snapshotRoot))
            throw new DirectoryNotFoundException($"Snapshot directory not found: {snapshotRoot}");

        var effectiveRoot = GetEffectiveRoot(snapshotRoot);
        var meta = ComputeMetaCore(effectiveRoot, ct);

        var metaPath = Path.Combine(snapshotRoot, "meta.json");
        using (var stream = File.Create(metaPath))
            JsonSerializer.Serialize(stream, meta, _jsonOptions);

        return meta;
    }

    public SnapshotMeta ComputeMeta(string snapshotRoot, CancellationToken ct = default)
    {
        if (snapshotRoot is null) throw new ArgumentNullException(nameof(snapshotRoot));
        if (!Directory.Exists(snapshotRoot))
            throw new DirectoryNotFoundException($"Snapshot directory not found: {snapshotRoot}");

        var effectiveRoot = GetEffectiveRoot(snapshotRoot);
        return ComputeMetaCore(effectiveRoot, ct);
    }

    public SnapshotMeta WriteMeta(RinnePaths paths, string space, string id, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(space)) throw new ArgumentException("space is required", nameof(space));
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("id is required", nameof(id));

        var idDir = paths.Snapshot(space, id);
        if (!Directory.Exists(idDir))
            throw new DirectoryNotFoundException($"Snapshot id not found: {idDir}");

        var effectiveRoot = GetEffectiveRoot(idDir);
        var meta = ComputeMetaCore(effectiveRoot, ct);

        var metaPath = Path.Combine(idDir, "meta.json");
        using (var stream = File.Create(metaPath))
            JsonSerializer.Serialize(stream, meta, _jsonOptions);

        return meta;
    }

    public SnapshotMeta ComputeMeta(RinnePaths paths, string space, string id, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(space)) throw new ArgumentException("space is required", nameof(space));
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("id is required", nameof(id));

        var idDir = paths.Snapshot(space, id);
        if (!Directory.Exists(idDir))
            throw new DirectoryNotFoundException($"Snapshot id not found: {idDir}");

        var effectiveRoot = GetEffectiveRoot(idDir);
        return ComputeMetaCore(effectiveRoot, ct);
    }

    private static string GetEffectiveRoot(string snapshotRoot)
    {
        var payload = Path.Combine(snapshotRoot, "snapshots");
        return Directory.Exists(payload) ? payload : snapshotRoot;
    }

    private static SnapshotMeta ComputeMetaCore(string effectiveRoot, CancellationToken ct)
    {
        var triples = Directory.EnumerateFiles(effectiveRoot, "*", SearchOption.AllDirectories)
            .Select(full => (FullPath: full, RelativePath: NormalizeRelativePath(effectiveRoot, full), Length: new FileInfo(full).Length));

        var items = SnapshotHash.ItemsFromPlan(triples, excludeMetaJson: false, excludeRinneDir: true);
        var res = SnapshotHash.Compute(items);

        return new SnapshotMeta(
            Version: CurrentVersion,
            HashAlgorithm: HashAlgorithmName,
            SnapshotHash: res.HashHex,
            FileCount: res.FileCount,
            TotalBytes: res.TotalBytes);
    }

    private static string NormalizeRelativePath(string root, string fullPath)
    {
        return Path.GetRelativePath(root, fullPath)
                   .Replace(Path.DirectorySeparatorChar, '/')
                   .Replace(Path.AltDirectorySeparatorChar, '/');
    }
}
