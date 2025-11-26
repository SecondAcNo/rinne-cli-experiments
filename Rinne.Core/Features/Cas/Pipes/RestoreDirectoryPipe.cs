using Rinne.Core.Features.Cas.Hashing;
using Rinne.Core.Features.Cas.Storage;
using ZstdSharp;

namespace Rinne.Core.Features.Cas.Pipes;

public static class RestoreDirectoryPipe
{
    public static IEnumerable<(string OutputPath, List<string> ChunkHashes)> ExpandManifest(Manifest mani, string outputDir)
    {
        foreach (var f in mani.Files)
        {
            if (f.Bytes == 0)
                continue;

            if (f.ChunkHashes is null || f.ChunkHashes.Count == 0 || f.ChunkHashes.Any(string.IsNullOrWhiteSpace))
                throw new InvalidDataException($"Empty chunk hash in manifest for {f.RelativePath}");

            yield return (SafeCombineUnderRoot(outputDir, f.RelativePath), f.ChunkHashes);
        }
    }

    public static async Task<RestoreResult> RunAsync(
        string storeDir,
        string manifestPath,
        string outputDir,
        int workers = 0,
        bool verify = false,
        CancellationToken ct = default)
    {
        if (!File.Exists(manifestPath))
            throw new FileNotFoundException("manifest not found", manifestPath);
        if (!Directory.Exists(storeDir))
            throw new DirectoryNotFoundException(storeDir);

        Directory.CreateDirectory(outputDir);

        var mani = System.Text.Json.JsonSerializer.Deserialize<Manifest>(
            await File.ReadAllTextAsync(manifestPath, ct).ConfigureAwait(false),
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("Invalid manifest.");

        if (mani.Files.Count == 0)
            throw new InvalidOperationException("Manifest has no files.");

        if (workers <= 0) workers = Math.Clamp(Environment.ProcessorCount, 1, 16);

        var root = Path.GetFullPath(outputDir);
        foreach (var z in mani.Files.Where(f => f.Bytes == 0))
        {
            var dst = SafeCombineUnderRoot(root, z.RelativePath);
            var parent = Path.GetDirectoryName(dst);
            if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
            using (File.Create(dst)) { }
        }

        var store = new ZstdContentAddressableStore(storeDir, mani.Level);

        var entries = ExpandManifest(mani, root).ToArray();

        await Parallel.ForEachAsync(
            entries,
            new ParallelOptions { MaxDegreeOfParallelism = workers, CancellationToken = ct },
            async (entry, token) =>
            {
                token.ThrowIfCancellationRequested();

                var outPath = entry.OutputPath;
                var dir = Path.GetDirectoryName(outPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                using var dst = new FileStream(outPath, new FileStreamOptions
                {
                    Mode = FileMode.Create,
                    Access = FileAccess.Write,
                    Share = FileShare.None,
                    Options = FileOptions.SequentialScan | FileOptions.Asynchronous
                });

                foreach (var hash in entry.ChunkHashes)
                {
                    token.ThrowIfCancellationRequested();

                    var zstPath = store.GetPathFor(hash);
                    if (!File.Exists(zstPath))
                        throw new FileNotFoundException($"Chunk missing in store: {hash}", zstPath);

                    using var src = new FileStream(zstPath, new FileStreamOptions
                    {
                        Mode = FileMode.Open,
                        Access = FileAccess.Read,
                        Share = FileShare.Read,
                        Options = FileOptions.SequentialScan | FileOptions.Asynchronous
                    });

                    using var zstd = new DecompressionStream(src);
                    await zstd.CopyToAsync(dst, 1024 * 1024, token).ConfigureAwait(false);
                }
            });

        string? computed = null;
        bool verified = false;
        if (verify)
        {
            var filePaths = mani.Files
                .Select(f => SafeCombineUnderRoot(outputDir, f.RelativePath))
                .ToList();

            computed = await Sha256Hasher.ComputeHexFromFilesAsync(filePaths, ct: ct).ConfigureAwait(false);
            verified = string.Equals(mani.OriginalSha256, computed, StringComparison.OrdinalIgnoreCase);
        }

        return new RestoreResult(
            TotalFiles: mani.Files.Count,
            Verified: verified,
            ExpectedHash: mani.OriginalSha256,
            ComputedHash: computed,
            OutputDir: outputDir
        );
    }

    private static string SafeCombineUnderRoot(string root, string relative)
    {
        var rootAbs = Path.GetFullPath(root);
        var pathAbs = Path.GetFullPath(Path.Combine(rootAbs, relative));
        if (!pathAbs.StartsWith(rootAbs, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Path escapes output root: {relative}");
        return pathAbs;
    }

    public sealed record RestoreResult(
        int TotalFiles,
        bool Verified,
        string? ExpectedHash,
        string? ComputedHash,
        string OutputDir
    );

    public sealed class Manifest
    {
        public string Version { get; init; } = "";
        public string Root { get; init; } = "";
        public string OriginalSha256 { get; init; } = "";
        public long TotalBytes { get; init; }
        public int AvgSizeBytes { get; init; }
        public int MinSizeBytes { get; init; }
        public int MaxSizeBytes { get; init; }
        public int Level { get; init; }
        public int FileCount { get; init; }
        public List<FileEntry> Files { get; init; } = new();
    }

    public sealed class FileEntry
    {
        public string RelativePath { get; init; } = "";
        public long Bytes { get; init; }
        public List<string> ChunkHashes { get; init; } = new();
    }
}
