using Rinne.Cli.Models;

namespace Rinne.Cli.Interfaces.Services
{
    /// <summary>
    /// アーカイブ（ZIP 等）を取り扱うサービス
    /// </summary>
    public interface IArchiveService
    {
        /// <summary>
        /// 指定ディレクトリ配下を ZIP ファイルとして作成します。
        /// </summary>
        /// <param name="rootDir">圧縮対象のルートディレクトリ。</param>
        /// <param name="outputZipPath">出力 ZIP ファイルパス。</param>
        /// <param name="options">ZIP 作成オプション（省略可）。</param>
        /// <param name="cancellationToken">キャンセル トークン。</param>
        /// <returns>作成された ZIP の絶対パス。</returns>
        Task<string> CreateZipAsync(
            string rootDir,
            string outputZipPath,
            ArchiveZipOptions? options = null,
            CancellationToken cancellationToken = default);
    }
}
