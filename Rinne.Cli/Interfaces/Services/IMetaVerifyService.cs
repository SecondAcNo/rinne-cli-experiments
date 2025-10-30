using Rinne.Cli.Models;

namespace Rinne.Cli.Interfaces.Services
{
    /// <summary>メタとハッシュチェーンの検証サービス。</summary>
    public interface IMetaVerifyService
    {
        /// <summary>
        /// 指定スペース（.rinne/data/&lt;space&gt;/meta）配下の全メタを古い順に検証する。
        /// </summary>
        Task<MetaVerifyReport> VerifySpaceAsync(string repoRoot, string space, CancellationToken ct = default);

        /// <summary>
        /// 単一メタ（ファイルパス指定）を検証する。
        /// </summary>
        Task<MetaVerifyReport> VerifyMetaFileAsync(string metaPath, CancellationToken ct = default);
    }
}