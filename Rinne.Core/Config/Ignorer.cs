namespace Rinne.Core.Config;

public sealed class Ignorer
{
    private readonly GlobSet _common;
    private readonly GlobSet _fileOnly;
    private readonly GlobSet _dirOnly;

    public Ignorer(ExcludeConfig cfg)
    {
        _common = new GlobSet(cfg.Exclude ?? new List<string>());
        _fileOnly = new GlobSet(cfg.ExcludeFiles ?? new List<string>());
        _dirOnly = new GlobSet(cfg.ExcludeDirs ?? new List<string>());
    }

    public bool IsDirExcluded(string relPath)
        => _common.IsMatchDir(relPath) || _dirOnly.IsMatchDir(relPath);

    public bool IsFileExcluded(string relPath)
        => _common.IsMatch(relPath) || _fileOnly.IsMatch(relPath);
}
