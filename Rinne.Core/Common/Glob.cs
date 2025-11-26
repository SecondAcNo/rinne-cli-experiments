using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;

namespace Rinne.Core.Common
{
    public static class Glob
    {
        private static readonly ConcurrentDictionary<string, Regex> _cache = new();

        public static Regex ToRegex(string pattern, bool caseInsensitive = true, bool anchor = true)
        {
            var key = $"{pattern}\u0001i:{caseInsensitive}\u0001a:{anchor}";
            return _cache.GetOrAdd(key, _ =>
            {
                var sb = new StringBuilder(anchor ? "^" : "");
                foreach (var ch in pattern)
                {
                    sb.Append(ch switch
                    {
                        '*' => ".*",
                        '?' => ".",
                        _ => Regex.Escape(ch.ToString())
                    });
                }
                if (anchor) sb.Append("$");

                var opt = RegexOptions.Compiled | RegexOptions.CultureInvariant;
                if (caseInsensitive) opt |= RegexOptions.IgnoreCase;
                return new Regex(sb.ToString(), opt);
            });
        }

        public static bool IsMatch(string text, string pattern, bool caseInsensitive = true, bool anchor = true)
            => ToRegex(pattern, caseInsensitive, anchor).IsMatch(text);
    }
}
