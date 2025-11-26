using System.Text.RegularExpressions;

namespace Rinne.Core.Common
{
    public static class SpaceNameRules
    {
        public const string Pattern = "^[a-z][a-z0-9_-]{0,63}$";
        public const string HumanReadable = "[a-z][a-z0-9_-]{0,63}";

        public static readonly Regex NameRegex =
            new Regex(Pattern, RegexOptions.CultureInvariant | RegexOptions.Compiled);
    }
}
