namespace Rinne.Cli.Interfaces.Services
{
    /// <summary>
    /// RINNE の ZIP アーカイブ同士の差分を計算するサービス。
    /// </summary>
    /// <remarks>
    /// space / id1 / id2 の解決（current の読取り、最新ペアの自動選択）、
    /// ZIP の展開、フォルダ差分の計算、作業フォルダのクリーンアップまでを一括で行います。
    /// </remarks>
    public interface IArchiveDiffService
    {
        /// <summary>
        /// アーカイブ差分を計算します。
        /// </summary>
        /// <param name="repoRoot">リポジトリのルート</param>
        /// <param name="id1">比較対象1（null の場合は自動解決）。</param>
        /// <param name="id2">比較対象2（null の場合は自動解決）。</param>
        /// <param name="space">space（null の場合は current を参照し、無ければ既定）。</param>
        /// <param name="cancellationToken">キャンセル トークン。</param>
        /// <returns>差分結果および解決済みメタ情報を含む DTO。</returns>
        Task<ArchiveDiffOutcome> DiffAsync(
            string repoRoot,
            string? id1,
            string? id2,
            string? space,
            CancellationToken cancellationToken = default);
    }
}
