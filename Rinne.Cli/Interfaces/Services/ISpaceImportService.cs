using Rinne.Cli.Models;

namespace Rinne.Cli.Interfaces.Services
{
    /// <summary>
    /// 他リポジトリから space フォルダをコピーするサービス。
    /// </summary>
    public interface ISpaceImportService
    {
        /// <summary>
        /// 別の .rinne から space をそのままコピーする。
        /// </summary>
        /// <param name="targetLayout">取り込み先のレイアウト。</param>
        /// <param name="request">取り込み設定。</param>
        /// <param name="ct">キャンセル トークン。</param>
        /// <returns>取り込み結果。</returns>
        Task<SpaceImportResult> ImportAsync(RepositoryLayout targetLayout, SpaceImportRequest request, CancellationToken ct = default);
    }
}
