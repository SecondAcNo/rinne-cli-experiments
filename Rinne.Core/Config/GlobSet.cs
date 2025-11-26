using System.Text.RegularExpressions;

namespace Rinne.Core.Config;

public sealed class GlobSet
{
    private readonly Regex[] _fileRegexes;
    private readonly Regex[] _dirRegexes;

    public GlobSet(IEnumerable<string> patterns)
    {
        var file = new List<Regex>();
        var dir = new List<Regex>();

        foreach (var raw in patterns)
        {
            var p = (raw ?? string.Empty).Replace('\\', '/').Trim();
            if (p.Length == 0) continue;

            var dirOnly = p.EndsWith("/");
            if (dirOnly) p = p[..^1];

            var rx = Compile(p, dirOnly);
            (dirOnly ? dir : file).Add(rx);

            if (!p.Contains('/'))
            {
                var seg = CompileSegment(p, dirOnly);
                (dirOnly ? dir : file).Add(seg);
            }
        }

        _fileRegexes = file.ToArray();
        _dirRegexes = dir.ToArray();
    }

    public bool IsMatch(string relPath)
    {
        var p = Normalize(relPath);
        return _fileRegexes.Any(r => r.IsMatch(p));
    }

    public bool IsMatchDir(string relDir)
    {
        var p = Normalize(relDir);
        if (!p.EndsWith("/")) p += "/";
        return _dirRegexes.Any(r => r.IsMatch(p)) || _fileRegexes.Any(r => r.IsMatch(p));
    }

    private static string Normalize(string p) => p.Replace('\\', '/');

    private static Regex Compile(string pattern, bool dirOnly)
    {
        var anchored = pattern.StartsWith("/");
        if (anchored) pattern = pattern.TrimStart('/');

        var rx = Regex.Escape(pattern)
            .Replace(@"\*\*", "___DS___")
            .Replace(@"\*", @"[^/]*")
            .Replace(@"\?", @"[^/]")
            .Replace("___DS___", @".*");

        if (dirOnly) rx += "/?";

        rx = anchored ? $"^{rx}$" : $"(^|/)({rx})(/|$)";
        return new Regex(rx, RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    }

    private static Regex CompileSegment(string segment, bool dirOnly)
    {
        var s = Regex.Escape(segment)
            .Replace(@"\*\*", "___DS___")
            .Replace(@"\*", @"[^/]*")
            .Replace(@"\?", @"[^/]")
            .Replace("___DS___", @".*");

        var rx = $"(^|/){s}(/|$)";
        return new Regex(rx, RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    }
}
