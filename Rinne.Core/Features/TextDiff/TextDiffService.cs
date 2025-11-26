using System.Collections.Concurrent;
using System.Text;
using DiffPlex;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
using Microsoft.Extensions.FileSystemGlobbing;
using Rinne.Core.Common;
using Rinne.Core.Features.Cas.Pipes;
using Rinne.Core.Features.Diff;

namespace Rinne.Core.Features.TextDiff;

public sealed class TextDiffService
{
    private readonly RinnePaths _paths;
    private readonly DiffService _diff;

    public TextDiffService(RinnePaths paths)
    {
        _paths = paths;
        _diff = new DiffService(paths);
    }

    public sealed record Options(
        string[]? IncludeGlobs = null,
        string[]? ExcludeGlobs = null,
        long MaxBytesPerFile = 2 * 1024 * 1024,
        bool TempHydrateWhenNeeded = true,
        int HydrateWorkers = 0,
        long HydrateMaxTotalBytes = long.MaxValue,
        int ContextLines = 3,
        bool IgnoreTrim = false,
        bool NormalizeNewlines = true
    );

    public enum FileStatus { Added, Removed, Modified, Renamed }

    public sealed record FileTextDiff(
        FileStatus Status,
        string PathA,
        string PathB,
        bool IsBinary,
        long BytesA,
        long BytesB,
        string? UnifiedDiffText
    );

    public sealed record Result(
        string IdA,
        string IdB,
        IReadOnlyList<FileTextDiff> Diffs
    );

    public async Task<Result> DiffTextAsync(
        string space,
        string selectorA,
        string selectorB,
        Options opt,
        CancellationToken ct)
    {
        var diffRes = await _diff.DiffAsync(space, selectorA, selectorB,
            new DiffService.Options(UseContentHash: false), ct);

        var target = FilterTargets(diffRes, opt);

        var (rootA, cleanupA) = await EnsureReadableRootAsync(space, diffRes.IdA, opt, ct);
        var (rootB, cleanupB) = await EnsureReadableRootAsync(space, diffRes.IdB, opt, ct);

        try
        {
            var bag = new ConcurrentBag<FileTextDiff>();

            await Parallel.ForEachAsync(target, new ParallelOptions { CancellationToken = ct }, async (change, token) =>
            {
                switch (change.Kind)
                {
                    case DiffService.ChangeKind.Added:
                        {
                            var pB = change.PathB;
                            var fullB = Path.Combine(rootB, pB);
                            if (!TryLoadText(fullB, opt, out var isBinB, out var textB, out var bytesB))
                                break;

                            if (isBinB) break;

                            var udiff = BuildUnified(null, textB, "/dev/null", pB, opt);
                            bag.Add(new FileTextDiff(FileStatus.Added, "", pB, false, 0, bytesB, udiff));
                            break;
                        }
                    case DiffService.ChangeKind.Removed:
                        {
                            var pA = change.PathA;
                            var fullA = Path.Combine(rootA, pA);
                            if (!TryLoadText(fullA, opt, out var isBinA, out var textA, out var bytesA))
                                break;

                            if (isBinA) break;

                            var udiff = BuildUnified(textA, null, pA, "/dev/null", opt);
                            bag.Add(new FileTextDiff(FileStatus.Removed, pA, "", false, bytesA, 0, udiff));
                            break;
                        }
                    case DiffService.ChangeKind.Modified:
                    case DiffService.ChangeKind.Renamed:
                        {
                            var pA = change.PathA;
                            var pB = change.PathB;
                            var fullA = Path.Combine(rootA, string.IsNullOrEmpty(pA) ? pB : pA);
                            var fullB = Path.Combine(rootB, string.IsNullOrEmpty(pB) ? pA : pB);

                            if (!TryLoadText(fullA, opt, out var isBinA2, out var textA2, out var bytesA2)) break;
                            if (!TryLoadText(fullB, opt, out var isBinB2, out var textB2, out var bytesB2)) break;

                            if (isBinA2 || isBinB2) break;

                            var udiff = BuildUnified(textA2, textB2, pA, pB, opt);
                            bag.Add(new FileTextDiff(
                                change.Kind == DiffService.ChangeKind.Renamed ? FileStatus.Renamed : FileStatus.Modified,
                                pA, pB, false, bytesA2, bytesB2, udiff));
                            break;
                        }
                }
            });

            var ordered = bag.OrderBy(d => d.PathB ?? d.PathA).ToList();
            return new Result(diffRes.IdA, diffRes.IdB, ordered);
        }
        finally
        {
            TryDeleteDirectory(cleanupA);
            TryDeleteDirectory(cleanupB);
        }
    }

    private static IReadOnlyList<DiffService.Change> FilterTargets(DiffService.Result r, Options opt)
    {
        var candidates = r.Changes.Where(c =>
            c.Kind is DiffService.ChangeKind.Modified
                 or DiffService.ChangeKind.Renamed
                 or DiffService.ChangeKind.Added
                 or DiffService.ChangeKind.Removed);

        var inc = (opt.IncludeGlobs is { Length: > 0 }) ? new Matcher(StringComparison.Ordinal) : null;
        inc?.AddIncludePatterns(opt.IncludeGlobs!);

        var exc = (opt.ExcludeGlobs is { Length: > 0 }) ? new Matcher(StringComparison.Ordinal) : null;
        exc?.AddExcludePatterns(opt.ExcludeGlobs!);

        bool Match(string path)
        {
            path = path.Replace('\\', '/');
            if (exc is not null && exc.Match(path).HasMatches) return false;
            if (inc is null) return true;
            return inc.Match(path).HasMatches;
        }

        return candidates.Where(c =>
        {
            var pa = string.IsNullOrEmpty(c.PathA) ? null : c.PathA;
            var pb = string.IsNullOrEmpty(c.PathB) ? null : c.PathB;
            return (pa is not null && Match(pa)) || (pb is not null && Match(pb));
        }).ToList();
    }

    private async Task<(string root, string cleanupDir)> EnsureReadableRootAsync(
        string space, string id, Options opt, CancellationToken ct)
    {
        var payload = _paths.SnapshotPayload(space, id);
        if (Directory.Exists(payload))
            return (payload, "");

        var manifestPath = _paths.StoreManifest(id);
        if (!File.Exists(manifestPath))
            throw new FileNotFoundException($"manifest not found and payload missing: {manifestPath} / {payload}");

        if (!opt.TempHydrateWhenNeeded)
            throw new IOException("payload missing (set TempHydrateWhenNeeded=true to allow temp restore).");

        var idDir = _paths.Snapshot(space, id);
        var tmpRoot = Path.Combine(idDir, ".textdiff_tmp");
        var tmpPayload = Path.Combine(tmpRoot, "snapshots");

        TryDeleteDirectory(tmpRoot);
        Directory.CreateDirectory(tmpPayload);

        await RestoreDirectoryPipe.RunAsync(
            manifestPath: manifestPath,
            storeDir: _paths.StoreRoot,
            outputDir: tmpPayload,
            workers: opt.HydrateWorkers,
            ct: ct);

        if (opt.HydrateMaxTotalBytes != long.MaxValue)
        {
            long total = DirSize(tmpPayload);
            if (total > opt.HydrateMaxTotalBytes)
            {
                TryDeleteDirectory(tmpRoot);
                throw new IOException($"temp hydrate exceeded HydrateMaxTotalBytes: {total} > {opt.HydrateMaxTotalBytes}");
            }
        }

        return (tmpPayload, tmpRoot);

        static long DirSize(string root)
        {
            long sum = 0;
            foreach (var p in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                sum += new FileInfo(p).Length;
            return sum;
        }
    }

    private static void TryDeleteDirectory(string dir)
    {
        try { if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) Directory.Delete(dir, true); }
        catch { }
    }

    private static bool TryLoadText(string fullPath, Options opt, out bool isBinary, out string? text, out long bytes)
    {
        isBinary = false;
        text = null;
        bytes = 0;

        if (!File.Exists(fullPath)) return false;

        var fi = new FileInfo(fullPath);
        bytes = fi.Length;
        if (bytes > opt.MaxBytesPerFile) return false;

        if (LooksBinaryQuick(fullPath)) { isBinary = true; return true; }

        string s;
        using (var fs = File.OpenRead(fullPath))
        using (var sr = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
            s = sr.ReadToEnd();

        if (opt.NormalizeNewlines)
            s = s.Replace("\r\n", "\n").Replace("\r", "\n");

        if (opt.IgnoreTrim)
            s = string.Join("\n", s.Split('\n').Select(line => line.TrimEnd()));

        text = s;
        return true;

        static bool LooksBinaryQuick(string path)
        {
            const int N = 4096;
            var buf = new byte[N];
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            int n = fs.Read(buf, 0, Math.Min(N, (int)fs.Length));
            for (int i = 0; i < n; i++) if (buf[i] == 0) return true;
            int ctrl = 0;
            for (int i = 0; i < n; i++)
            {
                var b = buf[i];
                if (b < 0x09 || (b > 0x0D && b < 0x20)) ctrl++;
            }
            return ctrl > n / 10;
        }
    }

    private static string BuildUnified(string? a, string? b, string pathA, string pathB, Options opt)
    {
        var builder = new SideBySideDiffBuilder(new Differ());
        var model = builder.BuildDiffModel(a ?? string.Empty, b ?? string.Empty);

        var oldLines = model.OldText.Lines;
        var newLines = model.NewText.Lines;
        int count = Math.Max(oldLines.Count, newLines.Count);

        var records = new List<LineRec>(count * 2);
        int aLine = 1, bLine = 1;

        for (int i = 0; i < count; i++)
        {
            var ol = (i < oldLines.Count) ? oldLines[i] : new DiffPiece(string.Empty, ChangeType.Imaginary, i);
            var nl = (i < newLines.Count) ? newLines[i] : new DiffPiece(string.Empty, ChangeType.Imaginary, i);

            if (ol.Type == ChangeType.Unchanged && nl.Type == ChangeType.Unchanged)
            {
                records.Add(new LineRec(LineKind.Context, " " + (ol.Text ?? string.Empty), aLine, bLine));
                aLine++; bLine++;
                continue;
            }

            if (nl.Type == ChangeType.Imaginary || ol.Type == ChangeType.Deleted)
            {
                records.Add(new LineRec(LineKind.Delete, "-" + (ol.Text ?? string.Empty), aLine, bLine));
                aLine++;
                continue;
            }

            if (ol.Type == ChangeType.Imaginary || nl.Type == ChangeType.Inserted)
            {
                records.Add(new LineRec(LineKind.Insert, "+" + (nl.Text ?? string.Empty), aLine, bLine));
                bLine++;
                continue;
            }

            records.Add(new LineRec(LineKind.Delete, "-" + (ol.Text ?? string.Empty), aLine, bLine));
            records.Add(new LineRec(LineKind.Insert, "+" + (nl.Text ?? string.Empty), aLine, bLine));
            aLine++; bLine++;
        }

        var hunks = MakeHunks(records, opt.ContextLines);

        var sb = new StringBuilder();
        sb.AppendLine($"--- {pathA}");
        sb.AppendLine($"+++ {pathB}");
        foreach (var h in hunks)
        {
            sb.AppendLine($"@@ -{h.AStart},{h.ACount} +{h.BStart},{h.BCount} @@");
            foreach (var l in h.Lines) sb.AppendLine(l);
        }
        return sb.ToString().TrimEnd();
    }

    private enum LineKind { Context, Insert, Delete }

    private sealed record LineRec(LineKind Kind, string Text, int ALine, int BLine);

    private sealed record Hunk(int AStart, int ACount, int BStart, int BCount, List<string> Lines);

    private static List<Hunk> MakeHunks(List<LineRec> recs, int context)
    {
        var hunks = new List<Hunk>();
        int i = 0;
        while (i < recs.Count)
        {
            int j = i;
            while (j < recs.Count && recs[j].Kind == LineKind.Context) j++;
            if (j >= recs.Count) break;

            int start = Math.Max(i, j - context);

            int k = j;
            int lastChange = j;
            while (k < recs.Count)
            {
                if (recs[k].Kind != LineKind.Context) lastChange = k;
                if (recs[k].Kind == LineKind.Context && k - lastChange > context) break;
                k++;
            }
            int end = Math.Min(recs.Count - 1, lastChange + context);

            int aStart = 1, bStart = 1;
            if (start < recs.Count)
            {
                var first = recs[start];
                aStart = first.ALine;
                bStart = first.BLine;
            }

            var lines = new List<string>();
            int aCount = 0, bCount = 0;

            for (int t = start; t <= end; t++)
            {
                var r = recs[t];
                lines.Add(r.Text);
                switch (r.Kind)
                {
                    case LineKind.Context: aCount++; bCount++; break;
                    case LineKind.Delete: aCount++; break;
                    case LineKind.Insert: bCount++; break;
                }
            }

            hunks.Add(new Hunk(aStart, Math.Max(0, aCount), bStart, Math.Max(0, bCount), lines));
            i = end + 1;
        }
        return hunks;
    }
}
