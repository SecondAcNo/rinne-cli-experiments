using Rinne.Cli.Models;

namespace Rinne.Cli.Interfaces.Services
{
    /// <summary>最新履歴（1件）を削除するサービス。</summary>
    public interface IDropLastService
    {
        /// <summary>
        /// 最新IDのみ削除。
        /// </summary>
        /// <param name="repoRoot">リポジトリルート。</param>
        /// <param name="space">スペース名。nullで.currentから解決。</param>
        /// <param name="confirmed">ユーザー確認済み（--yes）。</param>
        /// <param name="cancellationToken">キャンセル。</param>
        Task<DropLastResult> DropLastAsync(string repoRoot, string? space, bool confirmed, CancellationToken cancellationToken = default);
    }
}
