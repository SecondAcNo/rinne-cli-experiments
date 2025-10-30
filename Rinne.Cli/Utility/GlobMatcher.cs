using System.Text;
using System.Text.RegularExpressions;
using Rinne.Cli.Interfaces.Utility;

namespace Rinne.Cli.Utility
{
    /// <summary>
    /// 簡易的なグロブマッチャの既定実装。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 「**」「*」「?」をサポートし、大小文字を無視します。<br/>
    /// ZIP 内のパスは常に '/' 区切りで扱うことを前提とします。
    /// </para>
    /// </remarks>
    internal sealed class GlobMatcher : IGlobMatcher
    {
        private readonly Regex[] _includes;
        private readonly Regex[] _excludes;

        /// <summary>
        /// 新しい <see cref="GlobMatcher"/> を初期化します。
        /// </summary>
        public GlobMatcher(IEnumerable<string> includes, IEnumerable<string> excludes)
        {
            _includes = (includes?.Any() == true ? includes : new[] { "**" })
                .Select(ToRegex).ToArray();
            _excludes = (excludes ?? Array.Empty<string>())
                .Select(ToRegex).ToArray();
        }

        /// <inheritdoc/>
        public bool IsMatch(string path)
        {
            var inc = _includes.Any(r => r.IsMatch(path));
            if (!inc) return false;
            var exc = _excludes.Any(r => r.IsMatch(path));
            return !exc;
        }

        /// <summary>
        /// グロブ文字列を正規表現へ変換します。
        /// </summary>
        private static Regex ToRegex(string glob)
        {
            glob = (glob ?? string.Empty).Replace('\\', '/').Trim();
            var sb = new StringBuilder("^");

            for (int i = 0; i < glob.Length; i++)
            {
                var c = glob[i];
                if (c == '*')
                {
                    var dbl = i + 1 < glob.Length && glob[i + 1] == '*';
                    if (dbl) { sb.Append(".*"); i++; }
                    else { sb.Append("[^/]*"); }
                }
                else if (c == '?') sb.Append("[^/]");
                else if ("+()^$.{}![]|\\".Contains(c)) sb.Append('\\').Append(c);
                else sb.Append(c);
            }

            sb.Append("$");
            return new Regex(sb.ToString(), RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        }
    }

    /// <summary>
    /// <see cref="IGlobMatcherFactory"/> の既定実装。
    /// </summary>
    internal sealed class GlobMatcherFactory : IGlobMatcherFactory
    {
        /// <inheritdoc/>
        public IGlobMatcher Create(IEnumerable<string> includes, IEnumerable<string> excludes)
            => new GlobMatcher(includes, excludes);
    }
}
