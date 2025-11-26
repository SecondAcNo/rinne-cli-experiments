namespace Rinne.Core.Features.Init;

public sealed record InitLayoutOptions(
    string Space = "main",
    bool CreateDefaultConfigIfMissing = true,
    IEnumerable<string>? AddExclude = null,
    IEnumerable<string>? AddExcludeFiles = null,
    IEnumerable<string>? AddExcludeDirs = null
);
