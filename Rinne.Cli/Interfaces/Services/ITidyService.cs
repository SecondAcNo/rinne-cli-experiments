using Rinne.Cli.Models;

namespace Rinne.Cli.Interfaces.Services
{
    /// <summary>
    /// リポジトリの「整理」を実行するサービス。
    /// 手順: 1) .rinne をバックアップ → 2) 古い履歴を削除（最新 N 件残す） → 3) meta のチェーンを再計算。
    /// </summary>
    public interface ITidyService
    {
        /// <summary>整理を実行します。</summary>
        /// <param name="repoRoot">リポジトリのルート（.rinne と同階層）。</param>
        /// <param name="options">実行オプション。</param>
        /// <param name="ct">キャンセル。</param>
        Task<int> RunAsync(string repoRoot, TidyOptions options, CancellationToken ct = default);
    }
}
