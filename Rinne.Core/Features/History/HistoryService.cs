using Rinne.Core.Common;
using Rinne.Core.Features.Cas.Models;
using Rinne.Core.Features.Cas.Services;

namespace Rinne.Core.Features.History;

public sealed class HistoryService
{
    private readonly RinnePaths _paths;

    public HistoryService(RinnePaths paths) => _paths = paths;

    public sealed record Options(
        string Space,
        int? Take = null,
        DateTimeOffset? Since = null,
        DateTimeOffset? Before = null,
        IReadOnlyList<string>? MatchGlobs = null,
        bool IncludeSize = false
    );

    public sealed record Entry(
        string Id,
        DateTimeOffset CreatedUtc,
        bool HasPayload,
        bool HasManifest,
        long? PayloadBytes
    );

    public sealed record Result(IReadOnlyList<Entry> Entries);

    public Task<Result> RunAsync(Options opt, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(opt.Space))
            throw new ArgumentException("space is required.", nameof(opt));

        var spaceDir = _paths.SnapshotsSpace(opt.Space);
        if (!Directory.Exists(spaceDir))
            throw new DirectoryNotFoundException($"space not found: {opt.Space}");

        var snaps = SnapshotSelector.Enumerate(spaceDir)
                                    .OrderByDescending(s => s.CreatedUtc)
                                    .ToList();

        IEnumerable<SnapshotInfo> q = snaps;
        if (opt.Since is DateTimeOffset sinceUtc)
            q = q.Where(s => s.CreatedUtc >= sinceUtc);
        if (opt.Before is DateTimeOffset beforeUtc)
            q = q.Where(s => s.CreatedUtc < beforeUtc);

        if (opt.MatchGlobs is { Count: > 0 })
        {
            var regexes = opt.MatchGlobs.Select(pattern => Glob.ToRegex(pattern)).ToList();
            q = q.Where(s => regexes.All(rx => rx.IsMatch(s.Id)));
        }

        if (opt.Take is int take && take >= 0)
            q = q.Take(take);

        var list = new List<Entry>();
        foreach (var s in q)
        {
            ct.ThrowIfCancellationRequested();

            var payloadDir = _paths.SnapshotPayload(opt.Space, s.Id);
            var manifest = _paths.StoreManifest(s.Id);

            var hasPayload = Directory.Exists(payloadDir);
            var hasManifest = File.Exists(manifest);

            long? size = null;
            if (opt.IncludeSize && hasPayload)
                size = SafeDirSize(payloadDir, ct);

            list.Add(new Entry(
                Id: s.Id,
                CreatedUtc: s.CreatedUtc,
                HasPayload: hasPayload,
                HasManifest: hasManifest,
                PayloadBytes: size));
        }

        return Task.FromResult(new Result(list));
    }

    private static long SafeDirSize(string dir, CancellationToken ct)
    {
        long total = 0;
        if (!Directory.Exists(dir)) return 0;

        foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            try { total += new FileInfo(file).Length; }
            catch { }
        }
        return total;
    }
}
