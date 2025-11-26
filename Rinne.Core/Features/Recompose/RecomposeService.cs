using Rinne.Core.Common;
using Rinne.Core.Features.Cas.Models;
using Rinne.Core.Features.Cas.Pipes;
using Rinne.Core.Features.Cas.Services;
using System.Globalization;

namespace Rinne.Core.Features.Recompose;

public sealed class RecomposeService
{
    private readonly RinnePaths _paths;

    public RecomposeService(RinnePaths paths) => _paths = paths;

    public sealed record SourceSpec(
        string? Space,
        string? IdPrefix = null,
        int? NthFromNewest = null
    );

    public sealed record Options(
        string TargetSpace,
        IReadOnlyList<SourceSpec> Sources,
        string? NewSnapshotId = null,
        bool AutoHydrate = false,
        bool EphemeralHydrate = false
    );

    public sealed record Result(
        bool Created,
        string? NewSnapshotId,
        string? OutputPayloadDir,
        IReadOnlyList<string> ResolvedIds,
        long FilesCopied,
        long DirsCreated,
        string? Error = null
    );

    public async Task<Result> RunAsync(Options opt, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(opt.TargetSpace))
            return Fail("target space is required.");

        if (opt.Sources is null || opt.Sources.Count < 1)
            return Fail("at least one source is required to recompose.");

        var targetSpaceDir = _paths.SnapshotsSpace(opt.TargetSpace);
        if (!Directory.Exists(targetSpaceDir))
            return Fail($"space not found: {opt.TargetSpace}");

        var orderedSources = new List<(string space, SnapshotInfo snap)>(opt.Sources.Count);
        var resolvedIds = new List<string>(opt.Sources.Count);

        foreach (var src in opt.Sources)
        {
            ct.ThrowIfCancellationRequested();

            var space = string.IsNullOrWhiteSpace(src.Space) ? opt.TargetSpace : src.Space!;
            var spaceDir = _paths.SnapshotsSpace(space);
            if (!Directory.Exists(spaceDir))
                return Fail($"source space not found: {space}");

            var snaps = SnapshotSelector.Enumerate(spaceDir)
                                        .OrderByDescending(s => s.CreatedUtc)
                                        .ToList();
            if (snaps.Count == 0)
                return Fail($"(no snapshots) in space: {space}");

            SnapshotInfo pick;
            if (!string.IsNullOrWhiteSpace(src.IdPrefix))
            {
                var exact = snaps.Where(s => s.Id.Equals(src.IdPrefix, StringComparison.OrdinalIgnoreCase)).ToList();
                if (exact.Count == 1)
                {
                    pick = exact[0];
                }
                else
                {
                    var hits = snaps.Where(s => s.Id.StartsWith(src.IdPrefix, StringComparison.OrdinalIgnoreCase)).ToList();
                    if (hits.Count == 0) return Fail($"no snapshot matches '{src.IdPrefix}' in space '{space}'");
                    if (hits.Count > 1)
                    {
                        var show = string.Join(", ", hits.Take(5).Select(h => h.Id));
                        return Fail($"ambiguous '{src.IdPrefix}' in space '{space}': {show}{(hits.Count > 5 ? "…" : "")}");
                    }
                    pick = hits[0];
                }
            }
            else
            {
                var n = Math.Max(0, src.NthFromNewest ?? 0);
                if (n >= snaps.Count) return Fail($"index out of range in space '{space}': {n} (count={snaps.Count})");
                pick = snaps[n];
            }

            orderedSources.Add((space, pick));
            resolvedIds.Add(pick.Id);
        }

        var preparedSrcDirs = new List<string>(orderedSources.Count);
        var ephemeralDirs = new List<string>();
        try
        {
            foreach (var (space, snap) in orderedSources)
            {
                ct.ThrowIfCancellationRequested();
                var payload = _paths.SnapshotPayload(space, snap.Id);
                if (Directory.Exists(payload))
                {
                    preparedSrcDirs.Add(payload);
                    continue;
                }

                if (opt.EphemeralHydrate)
                {
                    var manifest = _paths.StoreManifest(snap.Id);
                    if (!File.Exists(manifest))
                        return Fail($"manifest not found for '{snap.Id}' (space '{space}')");

                    var tmpSrc = Path.Combine(targetSpaceDir, ".recompose_src_" + UuidV7.CreateString());
                    Directory.CreateDirectory(tmpSrc);

                    try
                    {
                        await RestoreDirectoryPipe.RunAsync(
                            manifestPath: manifest,
                            storeDir: _paths.StoreRoot,
                            outputDir: tmpSrc,
                            workers: 0,
                            ct: ct);
                    }
                    catch (Exception ex)
                    {
                        TryDeleteDirectory(tmpSrc);
                        return Fail($"ephemeral hydrate failed for '{snap.Id}': {ex.Message}");
                    }

                    preparedSrcDirs.Add(tmpSrc);
                    ephemeralDirs.Add(tmpSrc);
                }
                else if (opt.AutoHydrate)
                {
                    var hydrate = new HydrateService(_paths);
                    var sum = await hydrate.RunAsync(space, new List<SnapshotInfo> { snap }, workers: 0, removeManifest: false, ct);
                    if (sum.SuccessCount == 0)
                        return Fail($"hydrate failed for '{snap.Id}' in space '{space}' (success={sum.SuccessCount}, fail={sum.FailCount})");

                    if (!Directory.Exists(payload))
                        return Fail($"hydrate reported success but payload missing: {payload}");

                    preparedSrcDirs.Add(payload);
                }
                else
                {
                    return Fail($"payload not found for '{snap.Id}' (space '{space}'). Use '--hydrate' or '--hydrate=ephemeral'.");
                }
            }

            var stagingRoot = Path.Combine(targetSpaceDir, ".recompose_tmp_" + UuidV7.CreateString());
            Directory.CreateDirectory(stagingRoot);

            var stagingPayload = Path.Combine(stagingRoot, "snapshots");
            Directory.CreateDirectory(stagingPayload);

            long filesCopied = 0, dirsCreated = 0;

            foreach (var srcDir in preparedSrcDirs)
            {
                ct.ThrowIfCancellationRequested();
                MergeTreeLeftWins(srcDir, stagingPayload, ref filesCopied, ref dirsCreated, ct);
            }

            var newId = opt.NewSnapshotId ?? GenerateSnapshotId();
            var finalDir = _paths.Snapshot(opt.TargetSpace, newId);
            if (Directory.Exists(finalDir))
                return Fail($"destination snapshot already exists: {finalDir}");

            Directory.Move(stagingRoot, finalDir);

            var finalPayload = Path.Combine(finalDir, "snapshots");
            return new Result(
                Created: true,
                NewSnapshotId: newId,
                OutputPayloadDir: finalPayload,
                ResolvedIds: resolvedIds,
                FilesCopied: filesCopied,
                DirsCreated: dirsCreated,
                Error: null
            );
        }
        catch (Exception ex)
        {
            return Fail(ex.Message);
        }
        finally
        {
            foreach (var e in ephemeralDirs)
                TryDeleteDirectory(e);
        }

        Result Fail(string msg) => new(
            Created: false,
            NewSnapshotId: null,
            OutputPayloadDir: null,
            ResolvedIds: Array.Empty<string>(),
            FilesCopied: 0,
            DirsCreated: 0,
            Error: msg
        );
    }

    private static string GenerateSnapshotId()
        => $"{DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture)}_{UuidV7.CreateString()}";

    private static void MergeTreeLeftWins(
        string src, string dst, ref long filesCopied, ref long dirsCreated, CancellationToken ct)
    {
        foreach (var dir in Directory.EnumerateDirectories(src, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            var rel = Path.GetRelativePath(src, dir);
            if (IsUnderDotRinne(rel)) continue;

            var toDir = Path.Combine(dst, rel);
            if (!Directory.Exists(toDir))
            {
                Directory.CreateDirectory(toDir);
                dirsCreated++;
            }
        }

        foreach (var file in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            var rel = Path.GetRelativePath(src, file);
            if (IsUnderDotRinne(rel)) continue;

            var to = Path.Combine(dst, rel);
            if (File.Exists(to)) continue;
            if (Directory.Exists(to)) continue;

            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            File.Copy(file, to, overwrite: false);
            filesCopied++;
        }
    }

    private static bool IsUnderDotRinne(string rel)
    {
        var first = rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                       .FirstOrDefault();
        return string.Equals(first, ".rinne", StringComparison.OrdinalIgnoreCase);
    }

    private static void TryDeleteDirectory(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch { }
    }
}
