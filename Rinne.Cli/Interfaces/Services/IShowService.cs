using Rinne.Cli.Models;

namespace Rinne.Cli.Interfaces.Services
{
    /// <summary>
    /// セーブのメタ情報（meta.json）を取得・整形表示用に提供するサービスのインターフェイス。
    /// </summary>
    public interface IShowService
    {
        /// <summary>
        /// 指定または既定解決されたスペースとIDに対応するmeta.json を読み込み、整形済み JSON を返します。
        /// </summary>
        /// <param name="repoRoot">リポジトリのルートディレクトリ（例: 現在の作業ディレクトリ）。</param>
        /// <param name="id">セーブ ID。省略（null/空白）の場合は、対象スペース内の最新メタを自動選択。 </param>
        /// <param name="space">スペース名。省略（null/空白）の場合は currentを参照して決定します。</param>
        /// <param name="cancellationToken">キャンセルトークン。</param>
        /// <returns>処理結果（成功/失敗・整形 JSON など）。</returns>
        Task<ShowResult> GetFormattedMetaAsync(
            string repoRoot,
            string? id,
            string? space,
            CancellationToken cancellationToken = default);
    }
}
