using Rinne.Cli.Models;

namespace Rinne.Cli.Interfaces.Services
{
    /// <summary>
    /// RINNE Save用メタファイルの生成・保存を担うサービス。
    /// </summary>
    public interface IMetaService
    {
        /// <summary>
        /// Save用メタファイルを生成して保存します。
        /// </summary>
        /// <param name="input">生成に必要な入力パラメータ。</param>
        /// <param name="ct">キャンセル トークン。</param>
        /// <returns>書き出し結果（ファイルパスとメタオブジェクト）。</returns>
        Task<MetaWriteResult> WriteAsync(MetaWriteInput input, CancellationToken ct = default);

        /// <summary>
        /// Save用メタファイルのハッシュ(prevId/prev/this) を計算しなおす。
        /// </summary>
        /// <param name="repoRoot">リポジトリルート</param>
        /// <param name="space">スペース名</param>
        /// <param name="ct">キャンセルトークン</param>
        /// <returns></returns>
        Task<int> RestitchHashesAsync(string repoRoot, string space, CancellationToken ct = default);
    }
}
