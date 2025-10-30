using Rinne.Cli.Models;

namespace Rinne.Cli.Interfaces.Services
{
    /// <summary>
    /// Rinne の ZIP スナップショット間テキスト差分を実行するサービスの契約。
    /// </summary>
    public interface ITextDiffService
    {
        /// <summary>
        /// 2 つの ZIP スナップショットを展開・比較し、テキスト差分結果を返します。
        /// </summary>
        /// <param name="request">比較のための入力（ID、省略規則、スペースなど）。</param>
        /// <param name="cancellationToken">キャンセル トークン。</param>
        /// <returns>差分結果（表示用サマリとファイル単位の詳細）。</returns>
        Task<TextDiffRun> RunAsync(TextDiffRequest request, CancellationToken cancellationToken = default);
    }
}
