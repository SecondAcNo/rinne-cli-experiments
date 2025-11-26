namespace Rinne.Core.Features.Init;

public sealed record InitLayoutResult(
    string RinneRoot,
    IReadOnlyList<string> CreatedDirectories,
    IReadOnlyList<string> CreatedFiles,
    IReadOnlyList<string> Warnings
)
{
    public bool HasWarnings => Warnings.Count > 0;
}
