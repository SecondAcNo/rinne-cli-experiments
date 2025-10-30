using System.Text.RegularExpressions;

namespace Rinne.Cli.Utility
{
    public class SequenceUtility
    {
        /// <summary>
        /// 次のシーケンス番号の確保
        /// </summary>
        /// <param name="spaceDataDir">対象スペース名</param>
        /// <returns>シーケンス番号</returns>
        public static int GetNextSequence(string spaceDataDir)
        {
            if (!Directory.Exists(spaceDataDir))
                return 1;

            var regex = new Regex(@"^(?<seq>\d{8})_", RegexOptions.Compiled | RegexOptions.CultureInvariant);
            int maxSeq = 0;
            foreach (var file in Directory.EnumerateFiles(spaceDataDir, "*.zip", SearchOption.TopDirectoryOnly))
            {
                var name = Path.GetFileName(file);
                var m = regex.Match(name);
                if (!m.Success) continue;
                if (int.TryParse(m.Groups["seq"].Value, out var seq))
                    maxSeq = Math.Max(maxSeq, seq);
            }
            return maxSeq + 1;
        }
    }
}
