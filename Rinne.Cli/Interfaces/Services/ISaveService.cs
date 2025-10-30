using Rinne.Cli.Models;

namespace Rinne.Cli.Interfaces.Services
{
    /// <summary>
    /// セーブ処理（ZIPスナップショット作成とメタ出力）を司るアプリケーションサービス。
    /// </summary>
    public interface ISaveService
    {
        /// <summary>
        /// 現在のワーキングツリーを保存し、メタデータを出力します。
        /// </summary>
        /// <param name="repoRoot">リポジトリ（.rinneを含む）のルート。</param>
        /// <param name="space">セーブ先スペース。</param>
        /// <param name="message">セーブメッセージ。</param>
        /// <param name="cancellationToken">キャンセルトークン。</param>
        Task<SaveResult> SaveAsync(string repoRoot, string? space, string? message, CancellationToken cancellationToken = default);

        /// <summary>
        /// 指定されたフォルダを保存し、メタデータを出力します。
        /// </summary>
        /// <remarks>
        /// Recompose や外部一時ディレクトリのスナップショットを取りたい場合に使用します。
        /// </remarks>
        /// <param name="repoRoot">保存先となるリポジトリ（.rinneを含む）のルート。</param>
        /// <param name="targetRoot">保存対象フォルダのルート。</param>
        /// <param name="space">セーブ先スペース</param>
        /// <param name="message">セーブメッセージ。</param>
        /// <param name="cancellationToken">キャンセルトークン。</param>
        Task<SaveResult> SaveAsync(string repoRoot, string targetRoot, string? space, string? message, CancellationToken cancellationToken = default);
    }
}
