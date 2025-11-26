using Rinne.Core.Common;
using System.Globalization;

namespace Rinne.Core.Features.Import;

public sealed class ImportService
{
    private readonly RinnePaths _destPaths;

    public ImportService(RinnePaths destPaths) => _destPaths = destPaths;

    public sealed record Options(
        string SourceDirectory,
        string DestSpace,
        bool DryRun = false
    );

    public sealed record Result(
        bool Imported,
        string DestSpace,
        string? SnapshotId,
        DateTimeOffset CreatedUtc,
        string? Error = null
    );

    public async Task<Result> RunAsync(Options opt, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var srcDir = Path.GetFullPath(opt.SourceDirectory);
        if (!Directory.Exists(srcDir))
            return Fail(opt, $"source directory not found: {srcDir}");

        var destSpaceDir = _destPaths.SnapshotsSpace(opt.DestSpace);
        if (!opt.DryRun) Directory.CreateDirectory(destSpaceDir);

        var createdUtc = DateTimeOffset.UtcNow;
        var newId = NewIdFor(createdUtc);

        if (opt.DryRun)
            return new Result(true, opt.DestSpace, newId, createdUtc, null);

        var sessionRoot = Path.Combine(destSpaceDir, ".import_tmp_" + UuidV7.CreateString());
        Directory.CreateDirectory(sessionRoot);

        try
        {
            ct.ThrowIfCancellationRequested();

            var stageSnapDir = Path.Combine(sessionRoot, newId);
            var stagePayloadDir = Path.Combine(stageSnapDir, "snapshots");
            Directory.CreateDirectory(stagePayloadDir);

            CloneTree(srcDir, stagePayloadDir, ct, excludeDotRinne: true);

            var finalSnapDir = _destPaths.Snapshot(opt.DestSpace, newId);
            if (Directory.Exists(finalSnapDir))
                throw new IOException($"destination snapshot already exists: {finalSnapDir}");

            Directory.Move(stageSnapDir, finalSnapDir);
            TryDeleteDirectory(sessionRoot);

            return new Result(true, opt.DestSpace, newId, createdUtc, null);
        }
        catch (Exception ex)
        {
            try { TryDeleteDirectory(sessionRoot); } catch { }
            return Fail(opt, ex.Message);
        }

        static string NewIdFor(DateTimeOffset t)
            => $"{t.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture)}_{UuidV7.CreateString()}";

        static void CloneTree(string src, string dst, CancellationToken ct, bool excludeDotRinne)
        {
            foreach (var dir in Directory.EnumerateDirectories(src, "*", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();
                var rel = Path.GetRelativePath(src, dir);
                if (excludeDotRinne && IsUnderDotRinne(rel)) continue;
                Directory.CreateDirectory(Path.Combine(dst, rel));
            }
            foreach (var file in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();
                var rel = Path.GetRelativePath(src, file);
                if (excludeDotRinne && IsUnderDotRinne(rel)) continue;
                var to = Path.Combine(dst, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                File.Copy(file, to, overwrite: false);
            }
        }

        static bool IsUnderDotRinne(string rel)
        {
            var first = rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).FirstOrDefault();
            return string.Equals(first, ".rinne", StringComparison.OrdinalIgnoreCase);
        }

        static void TryDeleteDirectory(string dir)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
            catch { }
        }

        static Result Fail(Options opt, string msg) => new(
            Imported: false, DestSpace: opt.DestSpace, SnapshotId: null,
            CreatedUtc: DateTimeOffset.MinValue, Error: msg
        );
    }
}
