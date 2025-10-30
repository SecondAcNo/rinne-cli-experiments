namespace Rinne.Cli.Interfaces.Services
{
    /// <summary>
    /// Rinne リポジトリ初期化を担当するサービス。
    /// </summary>
    public interface IInitService
    {
        /// <summary>
        /// 現在の作業ディレクトリ（または指定ルート）を Rinne リポジトリとして初期化します。
        /// </summary>
        /// <param name="repoRoot">リポジトリのルートパス。</param>
        /// <param name="cancellationToken">キャンセル トークン。</param>
        /// <returns>
        /// 新規作成された場合は true、既に初期化済みの場合は false。
        /// </returns>
        Task<bool> InitializeAsync(string repoRoot, CancellationToken cancellationToken = default);
    }
}
