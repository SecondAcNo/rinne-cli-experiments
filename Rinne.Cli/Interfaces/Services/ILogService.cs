using Rinne.Cli.Models;

namespace Rinne.Cli.Interfaces.Services
{
    /// <summary>
    /// セーブ履歴を取得するサービスの契約。
    /// </summary>
    public interface ILogService
    {
        /// <summary>
        /// 指定スペースのセーブログを取得します。
        /// </summary>
        /// <param name="layout">リポジトリのレイアウト情報。</param>
        /// <param name="space">スペース名。省略またはnullの場合はcurrentの内容を使用。 </param>
        /// <param name="cancellationToken">キャンセル要求トークン。</param>
        /// <returns>解決済みスペース名と ZIP エントリ一覧を含む <see cref="SaveLogResult"/>。</returns>
        Task<SaveLogResult> GetLogAsync(
            RepositoryLayout layout,
            string? space,
            CancellationToken cancellationToken = default);
    }
}
