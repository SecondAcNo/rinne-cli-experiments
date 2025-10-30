namespace Rinne.Cli.Utility
{
    /// <summary>
    /// .rinneignore の読み込みおよび除外リスト操作を行うユーティリティ。
    /// </summary>
    public static class IgnoreUtility
    {
        /// <summary>
        /// 指定された .rinneignore ファイルを読み込み、有効な除外パターンを返します。
        /// </summary>
        /// <param name="ignoreFilePath">.rinneignore の絶対パス。</param>
        /// <returns>有効な除外パターンのリスト。存在しない場合は空リスト。</returns>
        /// <remarks>
        /// コメント行 (# 開始) や空行は無視します。
        /// 各行はトリム済みで返されます。
        /// </remarks>
        public static List<string> LoadIgnoreList(string ignoreFilePath)
        {
            if (string.IsNullOrWhiteSpace(ignoreFilePath))
                throw new ArgumentNullException(nameof(ignoreFilePath));

            if (!File.Exists(ignoreFilePath))
                return new List<string>();

            return File.ReadAllLines(ignoreFilePath)
                       .Where(l => !string.IsNullOrWhiteSpace(l))
                       .Select(l => l.Trim())
                       .Where(l => !l.StartsWith("#"))
                       .ToList();
        }

        /// <summary>
        /// 除外リストに強制的に特定パス（またはプレフィックス）を追加します。
        /// 既に同等の除外が存在する場合は追加しません。
        /// </summary>
        /// <param name="excludes">既存の除外リスト。</param>
        /// <param name="force">強制的に追加したいパスやディレクトリ名。</param>
        public static void EnsureForceExclude(List<string> excludes, string force)
        {
            if (excludes is null)
                throw new ArgumentNullException(nameof(excludes));

            if (string.IsNullOrWhiteSpace(force))
                return;

            // 既に登録されているならスキップ
            if (excludes.Any(e =>
                    e.Equals(force, StringComparison.OrdinalIgnoreCase) ||
                    e.StartsWith(force.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)))
                return;

            excludes.Insert(0, force.Trim());
        }
    }
}
