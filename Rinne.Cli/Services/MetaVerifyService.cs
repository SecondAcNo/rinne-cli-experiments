using System.Text.Json;
using Rinne.Cli.Interfaces.Services;
using Rinne.Cli.Models;
using Rinne.Cli.Utility;

namespace Rinne.Cli.Services
{
    /// <summary>
    /// メタデータおよびハッシュチェーンの整合性検証を行うサービス。
    /// </summary>
    /// <remarks>
    /// 対象のメタ情報を読み、
    /// ① ZIP ハッシュ一致、② チェーン this 再計算一致、③ 連結整合（prevId/prev）、
    /// ④ .rinneignore の有効ルール指紋一致（任意）を検証します。
    /// </remarks>
    public sealed class MetaVerifyService : IMetaVerifyService
    {
        /// <summary>JSON 読み込み設定（キャメルケースキー対応）。</summary>
        private static readonly JsonSerializerOptions ReadJsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        /// <inheritdoc/>
        public async Task<MetaVerifyReport> VerifySpaceAsync(
            string repoRoot,
            string space,
            CancellationToken ct = default)
        {
            // RepositoryLayout から必要パスを解決（相対戻りを排除）
            var layout = new RepositoryLayout(repoRoot);
            var metaDir = layout.GetSpaceMetaDir(space);

            var details = new List<string>();
            int ok = 0, ng = 0;

            if (!Directory.Exists(metaDir))
            {
                return new MetaVerifyReport
                {
                    Target = $"space:{space}",
                    IsOk = false,
                    Summary = "meta directory not found",
                    Details = new[] { metaDir }
                };
            }

            // メタファイル一覧を辞書順（＝時系列）で取得
            var metas = Directory.EnumerateFiles(metaDir, "*.json", SearchOption.TopDirectoryOnly)
                                 .OrderBy(p => p, StringComparer.Ordinal)
                                 .ToList();

            string? prevThis = null;
            string? prevId = null;

            foreach (var path in metas)
            {
                // スペース検証では .rinneignore の絶対パスが明確に取れる
                var r = await VerifyMetaCoreAsync(
                    metaPath: path,
                    expectedPrevId: prevId,
                    expectedPrevHash: prevThis,
                    ignoreAbs: layout.IgnorePath,
                    ct: ct
                ).ConfigureAwait(false);

                details.AddRange(r.Details);
                if (r.IsOk) ok++; else ng++;

                // チェーン更新（成功時のみ次の prev に使用）
                if (r.IsOk)
                {
                    var doc = MetaShared.ReadMeta(path, ReadJsonOptions);
                    prevThis = doc?.Hash?.Chain?.This;
                    prevId = doc?.Id;
                }
            }

            return new MetaVerifyReport
            {
                Target = $"space:{space}",
                IsOk = ng == 0,
                Summary = $"OK={ok}, NG={ng}, Total={metas.Count}",
                Details = details.ToArray()
            };
        }

        /// <inheritdoc/>
        public Task<MetaVerifyReport> VerifyMetaFileAsync(
            string metaPath,
            CancellationToken ct = default)
        {
            // 単一ファイル検証時は metaPath から「.rinne を含むリポジトリルート」を探索
            var repoRoot = TryResolveRepoRootFromMeta(metaPath);
            var ignoreAbs = repoRoot is null ? null : new RepositoryLayout(repoRoot).IgnorePath;

            return VerifyMetaCoreAsync(
                metaPath: metaPath,
                expectedPrevId: null,
                expectedPrevHash: null,
                ignoreAbs: ignoreAbs,
                ct: ct
            );
        }

        /// <summary>
        /// 単一メタファイルの検証処理本体。
        /// </summary>
        /// <param name="metaPath">対象メタファイルの絶対パス。</param>
        /// <param name="expectedPrevId">直前メタの ID（なければ null）。</param>
        /// <param name="expectedPrevHash">直前メタのチェーンハッシュ（なければ null）。</param>
        /// <param name="ignoreAbs">.rinneignore の絶対パス（不明なら null）。</param>
        /// <param name="ct">キャンセルトークン。</param>
        /// <returns>検証結果の <see cref="MetaVerifyReport"/>。</returns>
        private async Task<MetaVerifyReport> VerifyMetaCoreAsync(
            string metaPath,
            string? expectedPrevId,
            string? expectedPrevHash,
            string? ignoreAbs,
            CancellationToken ct)
        {
            var details = new List<string>();

            var doc = MetaShared.ReadMeta(metaPath, ReadJsonOptions);
            if (doc is null)
            {
                return new MetaVerifyReport
                {
                    Target = metaPath,
                    IsOk = false,
                    Summary = "invalid json",
                    Details = new[] { "[meta] JSON parse failed" }
                };
            }

            var metaDir = Path.GetDirectoryName(metaPath)!;
            var zipAbs = Path.GetFullPath(Path.Combine(metaDir, doc.Zip.Replace('/', Path.DirectorySeparatorChar)));
            
            // 1) ZIP ハッシュ検証            
            if (!File.Exists(zipAbs))
            {
                details.Add($"[zip] missing: {zipAbs}");
                return new MetaVerifyReport
                {
                    Target = doc.Id,
                    IsOk = false,
                    Summary = "zip missing",
                    Details = details.ToArray()
                };
            }

            var zipHash = await MetaShared.Sha256FileAsync(zipAbs, ct).ConfigureAwait(false);
            if (!MetaShared.HexEquals(zipHash, doc.Hash.Zip))
            {
                details.Add($"[zip] hash mismatch: meta={doc.Hash.Zip} actual={zipHash}");
                return new MetaVerifyReport
                {
                    Target = doc.Id,
                    IsOk = false,
                    Summary = "zip hash mismatch",
                    Details = details.ToArray()
                };
            }
            details.Add($"[zip] ok {MetaShared.ShortHex(zipHash)}");
            
            // 2) チェーン this 再計算            
            var thisRecalc = MetaShared.ComputeChainThis(doc.Id, doc.Hash.Zip, doc.Hash.Chain.Prev);
            if (!MetaShared.HexEquals(thisRecalc, doc.Hash.Chain.This))
            {
                details.Add($"[chain] this mismatch: meta={doc.Hash.Chain.This} calc={thisRecalc}");
                return new MetaVerifyReport
                {
                    Target = doc.Id,
                    IsOk = false,
                    Summary = "chain this mismatch",
                    Details = details.ToArray()
                };
            }
            details.Add($"[chain] this ok {MetaShared.ShortHex(thisRecalc)}");
            
            // 3) チェーン連結整合（前メタ → 現メタ）            
            if (expectedPrevId is not null || expectedPrevHash is not null)
            {
                if (!MetaShared.HexEquals(expectedPrevId, doc.Hash.Chain.PrevId) ||
                    !MetaShared.HexEquals(expectedPrevHash, doc.Hash.Chain.Prev))
                {
                    details.Add($"[chain] link mismatch: prevId/hash not match previous meta");
                    return new MetaVerifyReport
                    {
                        Target = doc.Id,
                        IsOk = false,
                        Summary = "chain link mismatch",
                        Details = details.ToArray()
                    };
                }
                details.Add($"[chain] link ok (← {expectedPrevId})");
            }
            else
            {
                if (doc.Hash.Chain.PrevId is not null || doc.Hash.Chain.Prev is not null)
                {
                    details.Add($"[chain] genesis expected but prev present");
                    return new MetaVerifyReport
                    {
                        Target = doc.Id,
                        IsOk = false,
                        Summary = "genesis mismatch",
                        Details = details.ToArray()
                    };
                }
                details.Add("[chain] genesis ok");
            }
            
            // すべての検証に成功            
            return new MetaVerifyReport
            {
                Target = doc.Id,
                IsOk = true,
                Summary = "ok",
                Details = details.ToArray()
            };
        }

        /// <summary>
        /// 単一メタファイルの場所から、親ディレクトリを辿って
        /// 「.rinne ディレクトリを含むリポジトリルート」を推定します。
        /// </summary>
        /// <param name="metaPath">メタファイルの絶対パス。</param>
        /// <returns>
        /// ルートディレクトリの絶対パス。見つからなければ null。
        /// </returns>
        private static string? TryResolveRepoRootFromMeta(string metaPath)
        {
            var dir = new DirectoryInfo(Path.GetDirectoryName(metaPath)!);
            // 期待構造: <repo>/.rinne/data/<space>/meta/...
            while (dir is not null)
            {
                var candidate = Path.Combine(dir.FullName, ".rinne");
                if (Directory.Exists(candidate))
                {
                    // ".rinne" の親がリポジトリルート
                    return dir.FullName;
                }
                dir = dir.Parent;
            }
            return null;
        }
    }
}
