using System.Collections.Concurrent;
using Rinne.Core.Common;
using Rinne.Core.Features.Cas.Pipes;
using Rinne.Core.Features.Meta;

namespace Rinne.Core.Features.Export;

public sealed class ExportService
{
    private readonly RinnePaths _paths;

    public ExportService(RinnePaths paths) => _paths = paths;

    public sealed record Options(
        string Space,
        IReadOnlyList<string> IdSelectors,
        string DestinationRoot,
        bool Flat = false,
        bool Overwrite = false,
        int Workers = 0
    );

    public sealed record ItemResult(string Id, string Status, string Message, string OutputPath);
    public sealed record Result(string Space, string Destination, int Total, int Ok, int Skipped, int Error, IReadOnlyList<ItemResult> Details);

    public async Task<Result> RunAsync(Options opt, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(opt.Space)) throw new ArgumentException("space is required.");
        if (opt.IdSelectors is null || opt.IdSelectors.Count == 0) throw new ArgumentException("at least one selector is required.");
        if (string.IsNullOrWhiteSpace(opt.DestinationRoot)) throw new ArgumentException("DestinationRoot is required.");

        var destRoot = Path.GetFullPath(opt.DestinationRoot);
        Directory.CreateDirectory(destRoot);

        var ids = ResolveIds(opt.Space, opt.IdSelectors);
        var details = new ConcurrentBag<ItemResult>();
        int ok = 0, skipped = 0, err = 0;
        int workers = opt.Workers > 0 ? opt.Workers : Environment.ProcessorCount;

        await Parallel.ForEachAsync(ids, new ParallelOptions { MaxDegreeOfParallelism = workers, CancellationToken = ct }, async (id, token) =>
        {
            var outBase = opt.Flat ? destRoot : Path.Combine(destRoot, opt.Space);
            var outDir = Path.Combine(outBase, id);
            var tmpRoot = Path.Combine(destRoot, ".export_tmp", id + "_" + Guid.NewGuid().ToString("N"));

            try
            {
                token.ThrowIfCancellationRequested();

                if (!opt.Overwrite && Directory.Exists(outDir))
                {
                    details.Add(new(id, "SKIPPED", "destination directory already exists", outDir));
                    Interlocked.Increment(ref skipped);
                    return;
                }

                Directory.CreateDirectory(tmpRoot);
                var tmpMeta = Path.Combine(tmpRoot, "meta.json");
                var tmpNote = Path.Combine(tmpRoot, "note.md");
                var tmpPayload = Path.Combine(tmpRoot, "snapshots");
                Directory.CreateDirectory(tmpPayload);

                var srcMeta = Path.Combine(_paths.Snapshot(opt.Space, id), "meta.json");
                if (!File.Exists(srcMeta))
                    throw new FileNotFoundException($"meta.json not found for id: {id}", srcMeta);
#if NET8_0_OR_GREATER
                File.Copy(srcMeta, tmpMeta, overwrite: true);
#else
                if (File.Exists(tmpMeta)) File.Delete(tmpMeta);
                File.Copy(srcMeta, tmpMeta);
#endif

                var srcNote = Path.Combine(_paths.Snapshot(opt.Space, id), "note.md");
                if (File.Exists(srcNote))
                {
#if NET8_0_OR_GREATER
                    File.Copy(srcNote, tmpNote, overwrite: true);
#else
                    if (File.Exists(tmpNote)) File.Delete(tmpNote);
                    File.Copy(srcNote, tmpNote);
#endif
                }

                var srcPayload = _paths.SnapshotPayload(opt.Space, id);
                if (Directory.Exists(srcPayload))
                {
                    await CopyDirectoryAsync(srcPayload, tmpPayload, token);
                }
                else
                {
                    var manifest = _paths.StoreManifest(id);
                    if (!File.Exists(manifest))
                        throw new FileNotFoundException($"manifest not found for id: {id}", manifest);

                    await RestoreDirectoryPipe.RunAsync(
                        manifestPath: manifest,
                        storeDir: _paths.StoreRoot,
                        outputDir: tmpPayload,
                        workers: workers,
                        ct: token);
                }

                var metaSvc = new MetaService();
                var computed = metaSvc.ComputeMeta(tmpRoot, token);
                var expected = await ReadMetaAsync(tmpMeta, token);
                if (!Equal(computed.SnapshotHash, expected.SnapshotHash) ||
                    computed.FileCount != expected.FileCount ||
                    computed.TotalBytes != expected.TotalBytes)
                {
                    throw new InvalidOperationException(
                        $"verification failed: expected {expected.SnapshotHash}/{expected.FileCount}/{expected.TotalBytes}, actual {computed.SnapshotHash}/{computed.FileCount}/{computed.TotalBytes}");
                }

                Directory.CreateDirectory(outBase);
                if (opt.Overwrite && Directory.Exists(outDir)) TryDeleteDirectory(outDir);
#if NET8_0_OR_GREATER
                Directory.Move(tmpRoot, outDir);
                tmpRoot = "";
#else
                MergeMoveDirectory(tmpRoot, outDir);
                TryDeleteDirectory(tmpRoot);
                tmpRoot = "";
#endif

                details.Add(new(id, "OK", "exported", outDir));
                Interlocked.Increment(ref ok);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                details.Add(new(id, "ERROR", ex.Message, outDir));
                Interlocked.Increment(ref err);
            }
            finally
            {
                TryDeleteDirectory(tmpRoot);
                TryDeleteDirectoryIfEmpty(Path.Combine(destRoot, ".export_tmp"));
            }
        });

        return new Result(
            Space: opt.Space,
            Destination: destRoot,
            Total: ids.Count,
            Ok: ok,
            Skipped: skipped,
            Error: err,
            Details: details.OrderBy(d => d.Id, StringComparer.Ordinal).ToList()
        );
    }

    private List<string> ResolveIds(string space, IReadOnlyList<string> selectors)
    {
        var spaceDir = _paths.SnapshotsSpace(space);
        if (!Directory.Exists(spaceDir)) throw new DirectoryNotFoundException($"space not found: {space}");

        var all = Directory.EnumerateDirectories(spaceDir)
            .Select(Path.GetFileName)
            .Where(n => !string.IsNullOrEmpty(n))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList()!;

        string ResolveOne(string sel)
        {
            sel = sel.Trim();
            if (sel.StartsWith("@", StringComparison.Ordinal))
            {
                if (!int.TryParse(sel.AsSpan(1), out var n) || n < 0)
                    throw new ArgumentException($"invalid selector: {sel}");
                if (all.Count == 0) throw new InvalidOperationException("space has no snapshots.");
                int idx = all.Count - 1 - n;
                if (idx < 0 || idx >= all.Count)
                    throw new ArgumentOutOfRangeException(nameof(sel), $"selector out of range: {sel}");
                return all[idx]!;
            }

            var hits = all.Where(id => id!.StartsWith(sel, StringComparison.Ordinal)).ToList();
            if (hits.Count == 0) throw new FileNotFoundException($"no snapshot matches: {sel}");
            if (hits.Count > 1) throw new InvalidOperationException($"ambiguous id prefix: {sel}");
            return hits[0]!;
        }

        var result = new List<string>(selectors.Count);
        foreach (var s in selectors) result.Add(ResolveOne(s));
        return result;
    }

    private static async Task CopyDirectoryAsync(string srcDir, string dstDir, CancellationToken ct)
    {
        foreach (var dir in Directory.EnumerateDirectories(srcDir, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            var rel = Path.GetRelativePath(srcDir, dir);
            Directory.CreateDirectory(Path.Combine(dstDir, rel));
        }
        foreach (var file in Directory.EnumerateFiles(srcDir, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            var rel = Path.GetRelativePath(srcDir, file);
            var dst = Path.Combine(dstDir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
#if NET8_0_OR_GREATER
            File.Copy(file, dst, overwrite: true);
#else
            if (File.Exists(dst)) File.Delete(dst);
            File.Copy(file, dst);
#endif
            await Task.Yield();
        }
    }

    private static async Task<SnapshotMeta> ReadMetaAsync(string metaPath, CancellationToken ct)
    {
        await using var fs = File.OpenRead(metaPath);
        var meta = await System.Text.Json.JsonSerializer.DeserializeAsync<SnapshotMeta>(fs, new System.Text.Json.JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }, ct);
        if (meta is null) throw new InvalidDataException($"invalid meta.json: {metaPath}");
        if (meta.Version != 1) throw new NotSupportedException($"unsupported meta version: {meta.Version}");
        if (!string.Equals(meta.HashAlgorithm, "sha256", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException($"unsupported hashAlg: {meta.HashAlgorithm}");
        return meta;
    }

    private static bool Equal(string a, string b) =>
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static void TryDeleteDirectory(string? dir)
    {
        try { if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
    }

    private static void TryDeleteDirectoryIfEmpty(string dir)
    {
        try
        {
            if (!Directory.Exists(dir)) return;
            if (!Directory.EnumerateFileSystemEntries(dir).Any()) { Directory.Delete(dir); return; }
            foreach (var sub in Directory.EnumerateDirectories(dir))
                TryDeleteDirectoryIfEmpty(sub);
            if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);
        }
        catch { }
    }

#if !NET8_0_OR_GREATER
    private static void MergeMoveDirectory(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var dir in Directory.EnumerateDirectories(src, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(src, dir);
            Directory.CreateDirectory(Path.Combine(dst, rel));
        }
        foreach (var file in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(src, file);
            var to = Path.Combine(dst, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            if (File.Exists(to)) File.Delete(to);
            File.Move(file, to);
        }
        Directory.Delete(src, true);
    }
#endif
}
