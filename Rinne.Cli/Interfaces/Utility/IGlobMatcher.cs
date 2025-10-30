namespace Rinne.Cli.Interfaces.Utility
{
    /// <summary>
    /// グロブパターンによるファイルパスの一致判定を行うインターフェイス。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 「**」「*」「?」などをサポートし、ZIP 内パスなどの '/' 区切りに対してマッチングを実施します。
    /// </para>
    /// </remarks>
    public interface IGlobMatcher
    {
        /// <summary>
        /// 指定されたパスがパターンに一致するかを判定します。
        /// </summary>
        /// <param name="path">ZIP 内などの相対パス。</param>
        /// <returns>一致する場合は true。</returns>
        bool IsMatch(string path);
    }

    /// <summary>
    /// <see cref="IGlobMatcher"/> のインスタンスを生成するファクトリ。
    /// </summary>
    public interface IGlobMatcherFactory
    {
        /// <summary>
        /// 指定された include/exclude パターン群からマッチャを生成します。
        /// </summary>
        /// <param name="includes">含めるパターン群。</param>
        /// <param name="excludes">除外するパターン群。</param>
        /// <returns>生成されたマッチャ。</returns>
        IGlobMatcher Create(IEnumerable<string> includes, IEnumerable<string> excludes);
    }
}
