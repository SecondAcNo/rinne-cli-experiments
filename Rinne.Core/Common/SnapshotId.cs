namespace Rinne.Core.Common;

public static class SnapshotId
{
    public static string CreateUtc()
        => $"{Clock.UtcNow():yyyyMMdd'T'HHmmss'Z'}_{UuidV7.CreateString()}";
}
