using System.Globalization;
using Rinne.Core.Features.Cas.Models;

namespace Rinne.Core.Features.Cas.Services;

public static class SnapshotSelector
{
    public static IEnumerable<SnapshotInfo> Enumerate(string spaceDir)
    {
        foreach (var dir in Directory.EnumerateDirectories(spaceDir))
        {
            var name = Path.GetFileName(dir);
            yield return new SnapshotInfo(name, dir, ResolveCreatedUtc(name, dir));
        }
    }

    public static IReadOnlyList<SnapshotInfo> SelectOlderThanIndex(IReadOnlyList<SnapshotInfo> orderedNewestFirst, int n)
    {
        if (n < 1) n = 1;
        return orderedNewestFirst.Skip(n).ToList();
    }

    public static IReadOnlyList<SnapshotInfo> SelectBefore(IReadOnlyList<SnapshotInfo> orderedNewestFirst, DateTimeOffset cutoffUtc)
        => orderedNewestFirst.Where(s => s.CreatedUtc < cutoffUtc).ToList();

    private static DateTimeOffset ResolveCreatedUtc(string name, string fullPath)
    {
        if (name.Length >= 17 && name[8] == 'T' && name[15] == 'Z')
        {
            var ts = name.Substring(0, 16);
            if (DateTime.TryParseExact(ts, "yyyyMMdd'T'HHmmss'Z'",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var dtA))
            {
                var utc = DateTime.SpecifyKind(dtA, DateTimeKind.Utc);
                return new DateTimeOffset(utc);
            }
        }

        var us = name.IndexOf('_');
        if (us > 0 && us + 1 < name.Length)
        {
            var datePart = name[(us + 1)..];
            if (DateTime.TryParseExact(datePart, "yyyyMMdd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var dtB))
            {
                var utcMidnight = DateTime.SpecifyKind(dtB, DateTimeKind.Utc);
                return new DateTimeOffset(utcMidnight);
            }
        }

        var fsUtc = Directory.GetLastWriteTimeUtc(fullPath);
        return new DateTimeOffset(fsUtc);
    }
}
