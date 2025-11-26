using Rinne.Core.Common;
using Rinne.Core.Features.Cas.Pipes;
using Rinne.Core.Features.Cas.Services;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Rinne.Core.Features.Tidy;

public sealed class TidyService
{
    private readonly RinnePaths _paths;

    public TidyService(RinnePaths paths) => _paths = paths;

    public sealed record Options(
        string Space,
        int? Keep = null,
        int? Latest = null,
        DateTimeOffset? Before = null,
        bool RunGc = true,
        bool DryRun = false,
        IReadOnlyList<string>? MatchGlobs = null
    );

    public sealed record Result(
        IReadOnlyList<string> TargetIds,
        int SnapshotDirsDeleted,
        int ManifestsDeleted,
        long GcExamined, long GcDeletable, long GcBytesFreed, bool GcDryRun,
        IReadOnlyList<string> GcCandidates
    );

    public async Task<Result> RunAsync(Options opt, CancellationToken ct)
    {
        var hasMatch = opt.MatchGlobs is { Count: > 0 };
        int selectorCount =
            (opt.Keep is not null ? 1 : 0) +
            (opt.Latest is not null ? 1 : 0) +
            (opt.Before is not null ? 1 : 0) +
            (hasMatch ? 1 : 0);

        if (selectorCount == 0)
            throw new ArgumentException("one of --keep, --latest/--newest, --before, or --match is required.");
        if (selectorCount > 1)
            throw new ArgumentException("exactly one of --keep, --latest/--newest, --before, or --match can be specified.");

        var spaceDir = _paths.SnapshotsSpace(opt.Space);
        if (!Directory.Exists(spaceDir))
            throw new DirectoryNotFoundException($"space not found: {opt.Space}");

        var snaps = SnapshotSelector.Enumerate(spaceDir)
                                    .OrderByDescending(s => s.CreatedUtc)
                                    .ToList();

        var targetIds = new HashSet<string>(StringComparer.Ordinal);

        if (opt.Keep is int k)
        {
            if (k < 0) k = 0;
            foreach (var s in snaps.Skip(k))
                targetIds.Add(s.Id);
        }
        else if (opt.Latest is int m)
        {
            if (m < 0) m = 0;
            foreach (var s in snaps.Take(m))
                targetIds.Add(s.Id);
        }
        else if (opt.Before is DateTimeOffset cutoff)
        {
            foreach (var s in snaps.Where(s => s.CreatedUtc < cutoff))
                targetIds.Add(s.Id);
        }
        else // --match を独立セレクタとして使用（他セレクタと併用不可）
        {
            var globs = opt.MatchGlobs!;
            var regexes = globs.Select(pattern => Glob.ToRegex(pattern)).ToList();
            foreach (var s in snaps)
            {
                // 既存挙動に合わせ、複数指定時は AND（全て一致）で判定
                if (regexes.All(rx => rx.IsMatch(s.Id)))
                    targetIds.Add(s.Id);
            }
        }

        var targets = snaps.Where(s => targetIds.Contains(s.Id))
                           .Select(s => s.Id)
                           .ToList();

        int snapDeleted = 0, manifestDeleted = 0;

        if (!opt.DryRun)
        {
            foreach (var id in targets)
            {
                ct.ThrowIfCancellationRequested();

                var snapDir = _paths.Snapshot(opt.Space, id);
                if (Directory.Exists(snapDir))
                {
                    try { Directory.Delete(snapDir, recursive: true); snapDeleted++; }
                    catch { }
                }

                var manifestPath = _paths.StoreManifest(id);
                if (File.Exists(manifestPath))
                {
                    try { File.Delete(manifestPath); manifestDeleted++; }
                    catch { }
                }
            }
        }

        long gcExamined = 0, gcDeletable = 0, gcBytesFreed = 0;
        bool gcDryRun = opt.DryRun;
        List<string> gcCandidates = new();

        if (opt.RunGc)
        {
            var manifestsDir = _paths.StoreManifests;
            var storeDir = _paths.StoreRoot;

            bool ManifestsExistAndNonEmpty()
                => Directory.Exists(manifestsDir)
                   && Directory.EnumerateFiles(manifestsDir, "*.json", SearchOption.AllDirectories).Any();

            var metaDir = _paths.StoreMeta;
            Directory.CreateDirectory(metaDir);

            string? refcountJson;

            if (opt.DryRun)
            {
                if (Directory.Exists(manifestsDir))
                {
                    var all = Directory.EnumerateFiles(manifestsDir, "*.json", SearchOption.AllDirectories);
                    var targetSet = new HashSet<string>(targets, StringComparer.Ordinal);
                    var remain = all.Where(p => !targetSet.Contains(Path.GetFileNameWithoutExtension(p)!))
                                    .ToList();

                    if (remain.Count > 0)
                    {
                        var docs = new List<JsonDocument>(remain.Count);
                        try
                        {
                            foreach (var p in remain)
                            {
                                ct.ThrowIfCancellationRequested();
                                using var fs = File.OpenRead(p);
                                var doc = await JsonDocument.ParseAsync(fs, cancellationToken: ct).ConfigureAwait(false);
                                docs.Add(doc);
                            }

                            var refmap = ReferenceScannerPipe.Analyze(docs);

                            Directory.CreateDirectory(_paths.TempDir);
                            refcountJson = Path.Combine(_paths.TempDir, "refcount.preview.json");
                            await File.WriteAllTextAsync(
                                refcountJson,
                                JsonSerializer.Serialize(refmap, new JsonSerializerOptions { WriteIndented = true }),
                                ct).ConfigureAwait(false);
                        }
                        finally
                        {
                            foreach (var d in docs) d.Dispose();
                        }
                    }
                    else
                    {
                        Directory.CreateDirectory(_paths.TempDir);
                        refcountJson = Path.Combine(_paths.TempDir, "refcount.preview.json");
                        await File.WriteAllTextAsync(refcountJson, "{}", ct).ConfigureAwait(false);
                    }
                }
                else
                {
                    Directory.CreateDirectory(_paths.TempDir);
                    refcountJson = Path.Combine(_paths.TempDir, "refcount.preview.json");
                    await File.WriteAllTextAsync(refcountJson, "{}", ct).ConfigureAwait(false);
                }
            }
            else
            {
                refcountJson = Path.Combine(metaDir, "refcount.json");
                if (ManifestsExistAndNonEmpty())
                {
                    await ReferenceScannerPipe.RunAsync(manifestsDir, refcountJson, ct).ConfigureAwait(false);
                }
                else
                {
                    await File.WriteAllTextAsync(refcountJson, "{}", ct).ConfigureAwait(false);
                }
            }

            if (!string.IsNullOrEmpty(refcountJson)
                && Directory.Exists(storeDir)
                && File.Exists(refcountJson))
            {
                var gc = await GarbageCollectionPipe.RunAsync(
                    storeDir: storeDir,
                    refcountJson: refcountJson,
                    dryRun: opt.DryRun,
                    ct: ct).ConfigureAwait(false);

                gcExamined = gc.Examined;
                gcDeletable = gc.Deletable;
                gcBytesFreed = gc.BytesFreed;
                gcDryRun = gc.DryRun;
                gcCandidates = gc.CandidatePaths.ToList();
            }
        }

        return new Result(
            TargetIds: targets,
            SnapshotDirsDeleted: snapDeleted,
            ManifestsDeleted: manifestDeleted,
            GcExamined: gcExamined,
            GcDeletable: gcDeletable,
            GcBytesFreed: gcBytesFreed,
            GcDryRun: gcDryRun,
            GcCandidates: gcCandidates
        );
    }
}
