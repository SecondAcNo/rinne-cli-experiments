using Rinne.Core.Common;
using Rinne.Core.Features.Cas.Models;
using Rinne.Core.Features.Cas.Pipes;
using Rinne.Core.Features.Cas.Services;

namespace Rinne.Core.Features.Restore;

public sealed class RestoreService
{
    private readonly RinnePaths _paths;

    public RestoreService(RinnePaths paths) => _paths = paths;

    public sealed record Options(
        string Space,
        string? IdPrefix = null,
        int? NthFromNewest = 0,
        string? Destination = null,
        bool AutoHydrate = false,
        bool EphemeralHydrate = false,
        bool PurgeAll = false
    );

    public sealed record Result(string SnapshotId, string SourcePayload, string Destination, bool Restored, string? Error = null);

    public async Task<Result> RunAsync(Options opt, CancellationToken ct)
    {
        var spaceDir = _paths.SnapshotsSpace(opt.Space);
        if (!Directory.Exists(spaceDir))
            return new Result("", "", "", false, $"space not found: {opt.Space}");

        var snaps = SnapshotSelector.Enumerate(spaceDir)
                                    .OrderByDescending(s => s.CreatedUtc)
                                    .ToList();
        if (snaps.Count == 0)
            return new Result("", "", "", false, "(no snapshots)");

        SnapshotInfo target;
        if (!string.IsNullOrWhiteSpace(opt.IdPrefix))
        {
            var hits = snaps.Where(s => s.Id.StartsWith(opt.IdPrefix, StringComparison.OrdinalIgnoreCase)).ToList();
            if (hits.Count == 0) return new Result("", "", "", false, $"no snapshot matches '{opt.IdPrefix}'");
            if (hits.Count > 1)
            {
                var show = string.Join(", ", hits.Take(5).Select(h => h.Id));
                return new Result("", "", "", false, $"ambiguous '{opt.IdPrefix}': {show}{(hits.Count > 5 ? "…" : "")}");
            }
            target = hits[0];
        }
        else
        {
            var n = Math.Max(0, opt.NthFromNewest ?? 0);
            if (n >= snaps.Count) return new Result("", "", "", false, $"index out of range: {n} (count={snaps.Count})");
            target = snaps[n];
        }

        var payloadDir = _paths.SnapshotPayload(opt.Space, target.Id);

        string? srcDir = null;
        string? ephemeralSrc = null;

        if (Directory.Exists(payloadDir))
        {
            srcDir = payloadDir;
        }
        else
        {
            if (opt.EphemeralHydrate)
            {
                var storeDir = _paths.StoreRoot;
                var manifest = _paths.StoreManifest(target.Id);
                if (!File.Exists(manifest))
                    return new Result(target.Id, payloadDir, "", false, $"manifest not found: {manifest}");

                var parentForSrc = _paths.SnapshotsSpace(opt.Space);
                ephemeralSrc = Path.Combine(parentForSrc, ".restore_src_" + UuidV7.CreateString());
                Directory.CreateDirectory(ephemeralSrc);

                try
                {
                    await RestoreDirectoryPipe.RunAsync(
                        manifestPath: manifest,
                        storeDir: storeDir,
                        outputDir: ephemeralSrc,
                        workers: 0,
                        ct: ct);
                }
                catch (Exception ex)
                {
                    TryDeleteDirectory(ephemeralSrc);
                    return new Result(target.Id, payloadDir, "", false, $"ephemeral hydrate failed: {ex.Message}");
                }

                srcDir = ephemeralSrc;
            }
            else if (opt.AutoHydrate)
            {
                var hydrate = new HydrateService(_paths);
                var sum = await hydrate.RunAsync(opt.Space, new List<SnapshotInfo> { target }, workers: 0, removeManifest: false, ct);
                if (sum.SuccessCount == 0)
                    return new Result(target.Id, payloadDir, "", false, $"hydrate failed for {target.Id} (success={sum.SuccessCount}, fail={sum.FailCount})");

                if (!Directory.Exists(payloadDir))
                    return new Result(target.Id, payloadDir, "", false, $"hydrate reported success but payload missing: {payloadDir}");

                srcDir = payloadDir;
            }
            else
            {
                return new Result(
                    target.Id, payloadDir, "",
                    false,
                    $"payload not found: {payloadDir}. Run 'rinne hydrate --id {target.Id}' or use '--hydrate' / '--hydrate=ephemeral'."
                );
            }
        }

        var destRoot = Path.GetFullPath(opt.Destination ?? Environment.CurrentDirectory);
        Directory.CreateDirectory(destRoot);

        var parent = Path.GetDirectoryName(destRoot) ?? destRoot;
        var tmp = Path.Combine(parent, ".restore_tmp_" + UuidV7.CreateString());
        var bak = Path.Combine(parent, ".restore_bak_" + UuidV7.CreateString());

        try
        {
            ct.ThrowIfCancellationRequested();

            Directory.CreateDirectory(tmp);
            CloneTree(srcDir!, tmp, ct, excludeDotRinne: true);

            if (opt.PurgeAll)
            {
                Directory.CreateDirectory(bak);
                MoveAllChildren(destRoot, bak, ct, excludeDotRinne: true);
                MoveAllChildren(tmp, destRoot, ct, excludeDotRinne: false);

                TryDeleteDirectory(tmp);
                TryDeleteDirectory(bak);
            }
            else
            {
                var (dirs, files) = EnumerateRelativeTree(tmp);

                Directory.CreateDirectory(bak);

                foreach (var relDir in dirs)
                {
                    ct.ThrowIfCancellationRequested();
                    var destPath = Path.Combine(destRoot, relDir);
                    var bakPath = Path.Combine(bak, relDir);

                    if (File.Exists(destPath))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(bakPath)!);
#if NET8_0_OR_GREATER
                        File.Move(destPath, bakPath, overwrite: true);
#else
                        if (File.Exists(bakPath)) File.Delete(bakPath);
                        File.Move(destPath, bakPath);
#endif
                    }
                    Directory.CreateDirectory(destPath);
                }

                foreach (var relFile in files)
                {
                    ct.ThrowIfCancellationRequested();
                    var destPath = Path.Combine(destRoot, relFile);
                    var tmpPath = Path.Combine(tmp, relFile);
                    var bakPath = Path.Combine(bak, relFile);

                    Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

                    if (Directory.Exists(destPath))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(bakPath)!);
                        if (Directory.Exists(bakPath)) TryDeleteDirectory(bakPath);
                        Directory.Move(destPath, bakPath);
                    }
                    else if (File.Exists(destPath))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(bakPath)!);
#if NET8_0_OR_GREATER
                        File.Move(destPath, bakPath, overwrite: true);
#else
                        if (File.Exists(bakPath)) File.Delete(bakPath);
                        File.Move(destPath, bakPath);
#endif
                    }

#if NET8_0_OR_GREATER
                    File.Move(tmpPath, destPath, overwrite: true);
#else
                    if (File.Exists(destPath)) File.Delete(destPath);
                    File.Move(tmpPath, destPath);
#endif
                }

                TryDeleteDirectory(tmp);
                TryDeleteDirectory(bak);
            }

            return new Result(target.Id, payloadDir, destRoot, true, null);
        }
        catch (Exception ex)
        {
            try
            {
                if (opt.PurgeAll)
                {
                    TryDeleteNonRinneChildren(destRoot);
                    if (Directory.Exists(bak))
                        MoveAllChildrenOverwrite(bak, destRoot, CancellationToken.None);
                }
                else
                {
                    if (Directory.Exists(bak))
                        MoveAllChildrenOverwrite(bak, destRoot, CancellationToken.None);

                    if (Directory.Exists(tmp))
                    {
                        var (dirs, files) = EnumerateRelativeTree(tmp);
                        foreach (var relFile in files)
                        {
                            var destPath = Path.Combine(destRoot, relFile);
                            var bakPath = Path.Combine(bak, relFile);
                            if (!ExistsFileOrDir(bakPath) && File.Exists(destPath))
                                TryDelete(destPath);
                        }
                        foreach (var relDir in dirs.OrderByDescending(d => d.Length))
                        {
                            var destPath = Path.Combine(destRoot, relDir);
                            var bakPath = Path.Combine(bak, relDir);
                            if (!ExistsFileOrDir(bakPath) && Directory.Exists(destPath))
                                TryDeleteDirectory(destPath);
                        }
                    }
                }
            }
            catch { }
            finally
            {
                TryDeleteDirectory(tmp);
                TryDeleteDirectory(bak);
                if (ephemeralSrc is not null) TryDeleteDirectory(ephemeralSrc);
            }

            return new Result(target.Id, payloadDir, destRoot, false, ex.Message);
        }
        finally
        {
            if (ephemeralSrc is not null) TryDeleteDirectory(ephemeralSrc);
        }
    }

    private static void CloneTree(string src, string dst, CancellationToken ct, bool excludeDotRinne)
    {
        foreach (var dir in Directory.EnumerateDirectories(src, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            var rel = Path.GetRelativePath(src, dir);
            if (excludeDotRinne && IsDotRinneTop(rel)) continue;
            Directory.CreateDirectory(Path.Combine(dst, rel));
        }

        foreach (var file in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            var rel = Path.GetRelativePath(src, file);
            if (excludeDotRinne && IsUnderDotRinne(rel)) continue;

            var to = Path.Combine(dst, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            File.Copy(file, to, overwrite: true);
        }
    }

    private static (List<string> dirs, List<string> files) EnumerateRelativeTree(string root)
    {
        var dirs = Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)
                            .Select(d => Path.GetRelativePath(root, d))
                            .Where(rel => !IsDotRinneTop(rel))
                            .OrderBy(d => d.Length)
                            .ToList();

        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                             .Select(f => Path.GetRelativePath(root, f))
                             .Where(rel => !IsUnderDotRinne(rel))
                             .ToList();

        return (dirs, files);
    }

    private static void MoveAllChildren(string fromDir, string toDir, CancellationToken ct, bool excludeDotRinne)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(fromDir))
        {
            ct.ThrowIfCancellationRequested();

            var name = Path.GetFileName(entry);
            if (excludeDotRinne && string.Equals(name, ".rinne", StringComparison.OrdinalIgnoreCase))
                continue;

            var dest = Path.Combine(toDir, name);
            if (Directory.Exists(entry))
            {
                Directory.CreateDirectory(toDir);
#if NET8_0_OR_GREATER
                Directory.Move(entry, dest);
#else
                if (Directory.Exists(dest)) throw new IOException($"target exists: {dest}");
                Directory.Move(entry, dest);
#endif
            }
            else if (File.Exists(entry))
            {
                Directory.CreateDirectory(toDir);
#if NET8_0_OR_GREATER
                File.Move(entry, dest, overwrite: true);
#else
                if (File.Exists(dest)) File.Delete(dest);
                File.Move(entry, dest);
#endif
            }
        }
    }

    private static void MoveAllChildrenOverwrite(string fromDir, string toDir, CancellationToken ct)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(fromDir))
        {
            ct.ThrowIfCancellationRequested();
            var name = Path.GetFileName(entry);
            var dest = Path.Combine(toDir, name);

            if (Directory.Exists(dest)) TryDeleteDirectory(dest);
            else if (File.Exists(dest)) TryDelete(dest);

            Directory.CreateDirectory(toDir);

            if (Directory.Exists(entry))
            {
                Directory.Move(entry, dest);
            }
            else if (File.Exists(entry))
            {
#if NET8_0_OR_GREATER
                File.Move(entry, dest, overwrite: true);
#else
                if (File.Exists(dest)) File.Delete(dest);
                File.Move(entry, dest);
#endif
            }
        }
    }

    private static void TryDeleteNonRinneChildren(string dir)
    {
        if (!Directory.Exists(dir)) return;

        foreach (var entry in Directory.EnumerateFileSystemEntries(dir))
        {
            var name = Path.GetFileName(entry);
            if (string.Equals(name, ".rinne", StringComparison.OrdinalIgnoreCase))
                continue;

            TryDelete(entry);
        }
    }

    private static bool ExistsFileOrDir(string path) => File.Exists(path) || Directory.Exists(path);

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
            else if (File.Exists(path)) File.Delete(path);
        }
        catch { }
    }

    private static void TryDeleteDirectory(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch { }
    }

    private static bool IsDotRinneTop(string rel)
        => string.Equals(NormalizeFirstSegment(rel), ".rinne", StringComparison.OrdinalIgnoreCase);

    private static bool IsUnderDotRinne(string rel)
        => rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
              .FirstOrDefault() is string first
              && string.Equals(first, ".rinne", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeFirstSegment(string rel)
        => rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
              .FirstOrDefault() ?? rel;
}
