namespace Rinne.Core.Features.Snapshots;

public sealed record SnapshotOptions(
    string SourceRoot,
    string Space = "main"
);
