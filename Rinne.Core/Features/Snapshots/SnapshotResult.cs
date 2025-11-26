namespace Rinne.Core.Features.Snapshots;

public sealed record SnapshotResult(
    string TargetDir,
    long CopiedFiles,
    long CopiedBytes,
    long SkippedFiles,
    IReadOnlyList<string> Errors)
{
    public bool HasErrors => Errors.Count > 0;
    public bool SkippedAny => SkippedFiles > 0;
}
