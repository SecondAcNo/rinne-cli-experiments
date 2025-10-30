using Rinne.Cli.Models;

namespace Rinne.Cli.Interfaces.Services
{
    /// <summary>
    /// Rinne リポジトリのバックアップを実行するサービス。
    /// </summary>
    public interface IBackupService
    {
        /// <summary>
        /// 指定ディレクトリ直下の .rinne/ をバックアップとして出力します。
        /// 検証用のハッシュテキストも出力します
        /// </summary>
        /// <param name="rootdir">対象のルートディレクトリ。</param>
        /// <param name="outputDir">バックアップとハッシュの出力先ディレクトリ（相対 or 絶対）。</param>
        /// <param name="cancellationToken">キャンセル トークン。</param>
        Task<BackupResult> BackupRinneAsync(string rootdir, string outputDir, CancellationToken cancellationToken = default);
    }
}
