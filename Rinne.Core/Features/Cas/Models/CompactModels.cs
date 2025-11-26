namespace Rinne.Core.Features.Cas.Models;

public sealed record SnapshotInfo(string Id, string FullPath, DateTimeOffset CreatedUtc);

public sealed record CompactOptions(int AvgMiB, int MinKiB, int MaxMiB, int ZstdLevel, int Workers, bool FullHashCheck);
