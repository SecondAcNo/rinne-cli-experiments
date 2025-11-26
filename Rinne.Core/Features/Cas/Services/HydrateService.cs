using Rinne.Core.Common;
using Rinne.Core.Features.Cas.Models;
using Rinne.Core.Features.Cas.Pipes;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rinne.Core.Features.Cas.Services;

public sealed class HydrateService
{
    private readonly RinnePaths _paths;
    public HydrateService(RinnePaths paths) => _paths = paths;

    public sealed record Summary(int SuccessCount, int SkipCount, int FailCount);

    private sealed class ManifestModel
    {
        public string Version { get; set; } = "";
        public string Root { get; set; } = "";
        public string OriginalSha256 { get; set; } = "";
        public long TotalBytes { get; set; }
        public int AvgSizeBytes { get; set; }
        public int MinSizeBytes { get; set; }
        public int MaxSizeBytes { get; set; }
        public int Level { get; set; }
        public int FileCount { get; set; }
        public List<FileEntry> Files { get; set; } = new();
        public List<string> Dirs { get; set; } = new();
    }

    private sealed class FileEntry
    {
        public string RelativePath { get; set; } = "";
        public long Bytes { get; set; }
        public List<string> ChunkHashes { get; set; } = new();
    }

    public async Task<Summary> RunAsync(
        string space,
        IReadOnlyList<SnapshotInfo> targets,
        int workers,
        bool removeManifest,
        CancellationToken ct)
    {
        var storeDir = _paths.StoreRoot;
        int ok = 0, skip = 0, fail = 0;

        foreach (var s in targets)
        {
            ct.ThrowIfCancellationRequested();

            var idDir = s.FullPath;
            var payloadDir = _paths.SnapshotPayload(space, s.Id);
            var manifestPath = _paths.StoreManifest(s.Id);

            if (!File.Exists(manifestPath))
            {
                //Console.Error.WriteLine($"fail (manifest not found): {s.Id}  ({manifestPath})");
                fail++;
                continue;
            }

            if (Directory.Exists(payloadDir))
            {
                //Console.WriteLine($"skip (payload exists): {s.Id}");
                skip++;
                continue;
            }

            ManifestModel mani;
            try
            {
                await using var mf = File.OpenRead(manifestPath);
                mani = await JsonSerializer.DeserializeAsync<ManifestModel>(mf, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    NumberHandling = JsonNumberHandling.AllowReadingFromString
                }, ct) ?? new ManifestModel();
            }
            catch (Exception ex)
            {
                //Console.Error.WriteLine($"fail (invalid manifest): {s.Id} ({ex.Message})");
                fail++;
                continue;
            }

            if (!string.Equals(mani.Version, "cas:2", StringComparison.Ordinal))
            {
                //Console.Error.WriteLine($"fail (unsupported manifest version): {s.Id} ({mani.Version})");
                fail++;
                continue;
            }

            var tmp = Path.Combine(idDir, ".hydrate_tmp");
            TryDeleteDirectory(tmp);
            Directory.CreateDirectory(tmp);

            try
            {
                //Console.WriteLine($"hydrate: {s.Id} -> {payloadDir} ...");

                var dirSet = new HashSet<string>(mani.Dirs.Select(NormalizeRel), StringComparer.Ordinal);
                foreach (var rel in dirSet.OrderBy(x => x, StringComparer.Ordinal))
                {
                    var dst = Path.Combine(tmp, rel.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(dst);
                }

                var zeroFiles = mani.Files.Where(f => f.Bytes == 0).ToArray();
                foreach (var z in zeroFiles)
                {
                    var rel = NormalizeRel(z.RelativePath);
                    var dst = Path.Combine(tmp, rel.Replace('/', Path.DirectorySeparatorChar));
                    var parent = Path.GetDirectoryName(dst);
                    if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
                    using (File.Create(dst)) { }
                }

                var nonZero = mani.Files.Where(f => f.Bytes > 0).ToList();
                if (nonZero.Count > 0)
                {
                    var filtered = new
                    {
                        Version = mani.Version,
                        Root = mani.Root,
                        OriginalSha256 = mani.OriginalSha256,
                        TotalBytes = nonZero.Sum(x => x.Bytes),
                        AvgSizeBytes = mani.AvgSizeBytes,
                        MinSizeBytes = mani.MinSizeBytes,
                        MaxSizeBytes = mani.MaxSizeBytes,
                        Level = mani.Level,
                        FileCount = nonZero.Count,
                        Files = nonZero,
                        Dirs = mani.Dirs
                    };

                    var tmpManifest = Path.Combine(idDir, ".hydrate_manifest.tmp.json");
                    await File.WriteAllTextAsync(tmpManifest, JsonSerializer.Serialize(filtered, new JsonSerializerOptions { WriteIndented = true }), ct);

                    await RestoreDirectoryPipe.RunAsync(
                        manifestPath: tmpManifest,
                        storeDir: storeDir,
                        outputDir: tmp,
                        workers: workers,
                        ct: ct);

                    TryDeleteFile(tmpManifest);
                }

#if NET8_0_OR_GREATER
                Directory.Move(tmp, payloadDir);
#else
                if (Directory.Exists(payloadDir)) throw new IOException($"target exists: {payloadDir}");
                Directory.Move(tmp, payloadDir);
#endif
                //Console.WriteLine($"  ok: {s.Id}");
                ok++;

                if (removeManifest)
                {
                    TryDeleteFile(manifestPath);
                    //Console.WriteLine($"  removed manifest: {Path.GetFileName(manifestPath)}");
                }
            }
            catch (Exception ex)
            {
                //Console.Error.WriteLine($"  fail: {s.Id} ({ex.Message})");
                TryDeleteDirectory(tmp);
                fail++;
            }
        }

        return new Summary(ok, skip, fail);
    }

    public Task HydrateFileAsync(
        string manifestPath,
        string outputDir,
        string relativePath,
        int workers,
        CancellationToken ct)
    {
        if (relativePath is null) throw new ArgumentNullException(nameof(relativePath));
        return HydrateSelectionAsync(manifestPath, outputDir, new[] { relativePath }, workers, ct);
    }

    public async Task HydratePickAsync(
        string manifestPath,
        string selector,
        string outputPath,
        int workers,
        CancellationToken ct)
    {
        if (manifestPath is null) throw new ArgumentNullException(nameof(manifestPath));
        if (selector is null) throw new ArgumentNullException(nameof(selector));
        if (outputPath is null) throw new ArgumentNullException(nameof(outputPath));

        var normalizedSelector = NormalizeRel(selector);
        if (string.IsNullOrWhiteSpace(normalizedSelector))
            throw new ArgumentException("selector is empty after normalization", nameof(selector));

        var tmpRoot = Path.Combine(Path.GetTempPath(), "rinne_pick_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpRoot);

        try
        {
            await HydrateSelectionAsync(
                manifestPath: manifestPath,
                outputDir: tmpRoot,
                selectors: new[] { normalizedSelector },
                workers: workers,
                ct: ct);

            var relPath = normalizedSelector.Replace('/', Path.DirectorySeparatorChar);
            var hydratedPath = Path.Combine(tmpRoot, relPath);

            var isFile = File.Exists(hydratedPath);
            var isDir = Directory.Exists(hydratedPath);

            if (!isFile && !isDir)
            {
                throw new FileNotFoundException($"no files matched selector: {selector}");
            }

            if (isDir && !isFile)
            {
                if (!Directory.Exists(outputPath))
                {
                    Directory.CreateDirectory(outputPath);
                }

                CopyDirectoryContents(hydratedPath, outputPath);
                return;
            }

            var treatOutAsDir =
                Directory.Exists(outputPath) ||
                EndsWithDirectorySeparator(outputPath);

            if (!treatOutAsDir)
            {
                if (File.Exists(outputPath))
                {
                    throw new IOException($"destination file already exists: {outputPath}");
                }

                var parent = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(parent) && !Directory.Exists(parent))
                {
                    Directory.CreateDirectory(parent);
                }

                File.Copy(hydratedPath, outputPath, overwrite: false);
            }
            else
            {
                if (!Directory.Exists(outputPath))
                {
                    Directory.CreateDirectory(outputPath);
                }

                CopyDirectoryContents(tmpRoot, outputPath);
            }
        }
        finally
        {
            TryDeleteDirectory(tmpRoot);
        }
    }

    public async Task HydrateSelectionAsync(
        string manifestPath,
        string outputDir,
        IReadOnlyList<string> selectors,
        int workers,
        CancellationToken ct)
    {
        if (manifestPath is null) throw new ArgumentNullException(nameof(manifestPath));
        if (outputDir is null) throw new ArgumentNullException(nameof(outputDir));
        if (selectors is null) throw new ArgumentNullException(nameof(selectors));
        if (selectors.Count == 0) throw new ArgumentException("at least one selector is required", nameof(selectors));

        if (!File.Exists(manifestPath))
            throw new FileNotFoundException("manifest not found", manifestPath);

        ManifestModel mani;
        try
        {
            await using var mf = File.OpenRead(manifestPath);
            mani = await JsonSerializer.DeserializeAsync<ManifestModel>(mf, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                NumberHandling = JsonNumberHandling.AllowReadingFromString
            }, ct) ?? new ManifestModel();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"invalid manifest: {manifestPath}", ex);
        }

        if (!string.Equals(mani.Version, "cas:2", StringComparison.Ordinal))
        {
            throw new NotSupportedException($"unsupported manifest version: {mani.Version}");
        }

        Directory.CreateDirectory(outputDir);
        var storeDir = _paths.StoreRoot;

        var normalizedSelectors = selectors
            .Select(NormalizeRel)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (normalizedSelectors.Length == 0)
            throw new ArgumentException("selectors are empty after normalization", nameof(selectors));

        bool IsSelected(string rel)
        {
            if (string.IsNullOrEmpty(rel)) return false;

            foreach (var sel in normalizedSelectors)
            {
                if (string.Equals(rel, sel, StringComparison.Ordinal))
                    return true;
                if (rel.StartsWith(sel + "/", StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        var allFiles = mani.Files ?? new List<FileEntry>();
        var selectedFiles = allFiles
            .Select(f => new { File = f, Rel = NormalizeRel(f.RelativePath) })
            .Where(x => IsSelected(x.Rel))
            .ToList();

        if (selectedFiles.Count == 0)
        {
            var message = "no files matched selectors: " + string.Join(", ", normalizedSelectors);
            throw new FileNotFoundException(message);
        }

        var dirSet = new HashSet<string>(StringComparer.Ordinal);

        foreach (var d in mani.Dirs ?? new List<string>())
        {
            var rel = NormalizeRel(d);
            if (IsSelected(rel) || normalizedSelectors.Any(sel => rel.StartsWith(sel + "/", StringComparison.Ordinal)))
            {
                if (!string.IsNullOrEmpty(rel))
                    dirSet.Add(rel);
            }
        }

        foreach (var x in selectedFiles)
        {
            var rel = x.Rel;
            var idx = rel.LastIndexOf('/');
            while (idx > 0)
            {
                var parent = rel.Substring(0, idx);
                if (!dirSet.Add(parent))
                    break;
                idx = parent.LastIndexOf('/');
            }
        }

        foreach (var rel in dirSet.OrderBy(x => x, StringComparer.Ordinal))
        {
            var dst = Path.Combine(outputDir, rel.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(dst);
        }

        var zeroFiles = selectedFiles.Where(x => x.File.Bytes == 0).ToArray();
        foreach (var z in zeroFiles)
        {
            var rel = z.Rel;
            var dst = Path.Combine(outputDir, rel.Replace('/', Path.DirectorySeparatorChar));
            var parent = Path.GetDirectoryName(dst);
            if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
            using (File.Create(dst)) { }
        }

        var nonZeroFiles = selectedFiles.Where(x => x.File.Bytes > 0).Select(x => x.File).ToList();
        if (nonZeroFiles.Count > 0)
        {
            var filtered = new
            {
                Version = mani.Version,
                Root = mani.Root,
                OriginalSha256 = mani.OriginalSha256,
                TotalBytes = nonZeroFiles.Sum(x => x.Bytes),
                AvgSizeBytes = mani.AvgSizeBytes,
                MinSizeBytes = mani.MinSizeBytes,
                MaxSizeBytes = mani.MaxSizeBytes,
                Level = mani.Level,
                FileCount = nonZeroFiles.Count,
                Files = nonZeroFiles,
                Dirs = dirSet.ToList()
            };

            var tmpManifest = Path.Combine(outputDir, ".hydrate_selection.tmp.json");
            await File.WriteAllTextAsync(tmpManifest, JsonSerializer.Serialize(filtered, new JsonSerializerOptions { WriteIndented = true }), ct);

            try
            {
                await RestoreDirectoryPipe.RunAsync(
                    manifestPath: tmpManifest,
                    storeDir: storeDir,
                    outputDir: outputDir,
                    workers: workers,
                    ct: ct);
            }
            finally
            {
                TryDeleteFile(tmpManifest);
            }
        }
    }

    private static string NormalizeRel(string rel)
        => rel.Replace('\\', '/').TrimStart('/');

    private static bool EndsWithDirectorySeparator(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        var c = path[path.Length - 1];
        return c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar;
    }

    private static void CopyDirectoryContents(string sourceDir, string destinationDir)
    {
        foreach (var dir in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(sourceDir, dir);
            var targetDir = Path.Combine(destinationDir, rel);
            Directory.CreateDirectory(targetDir);
        }

        foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(sourceDir, file);
            var targetFile = Path.Combine(destinationDir, rel);

            if (File.Exists(targetFile))
            {
                throw new IOException($"destination file already exists: {targetFile}");
            }

            var parent = Path.GetDirectoryName(targetFile);
            if (!string.IsNullOrEmpty(parent) && !Directory.Exists(parent))
            {
                Directory.CreateDirectory(parent);
            }

            File.Copy(file, targetFile, overwrite: false);
        }
    }

    private static void TryDeleteDirectory(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch { }
    }

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
