using System.Text.Json;
using System.Text.Json.Serialization;
using Rinne.Cli.Interfaces.Services;
using Rinne.Cli.Models;
using Rinne.Cli.Utility;

namespace Rinne.Cli.Services
{
    /// <summary>
    /// IMetaServiceの既定実装。
    /// 入力からメタを構築し、保存します。
    /// </summary>
    public sealed class MetaService : IMetaService
    {
        /// <summary>JSON 出力オプション（キャメルケース＋インデント）。</summary>
        private static readonly JsonSerializerOptions WriteJsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never
        };

        /// <summary>JSON 読み込み用オプション（キャメルケースキー）。</summary>
        private static readonly JsonSerializerOptions ReadJsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        /// <inheritdoc/>
        public async Task<MetaWriteResult> WriteAsync(MetaWriteInput input, CancellationToken ct = default)
        {
            Validate(input);
            
            // レイアウト解決            
            var space = MetaShared.SanitizeSpace(input.Space);
            var dataDir = Path.Combine(input.RepoRoot, ".rinne", "data", space);
            var metaDir = Path.Combine(dataDir, "meta");
            Directory.CreateDirectory(metaDir);
            
            // ZIP 検証 & ハッシュ            
            var zipAbs = Path.GetFullPath(input.ZipAbsolutePath);
            if (!File.Exists(zipAbs))
                throw new FileNotFoundException("Zip file not found.", zipAbs);

            var zipHash = await MetaShared.Sha256FileAsync(zipAbs, ct).ConfigureAwait(false);
            
            // ignore ルール抽出            
            var ignoreAbs = Path.Combine(input.RepoRoot, input.IgnoreSourceFileName);
            var rules = MetaShared.ReadIgnoreRules(ignoreAbs);
            
            // 直近メタ（prevId/prevThis）            
            var (prevId, prevThis) = MetaShared.TryReadLatestChain(metaDir, ReadJsonOptions);
            
            // ID・UTC（ISO と compact 文字列）            
            var nowUtc = DateTimeOffset.UtcNow;
            var utcIso = nowUtc.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'");
            var utcCompact = nowUtc.ToString("yyyyMMdd'T'HHmmssfff'Z'");
            var id = $"{input.Seq:D8}_{utcCompact}";

            // 相対 zip（メタからの相対パス）
            var zipRel = Path.GetRelativePath(metaDir, zipAbs).Replace('\\', '/');

            // チェーン this
            var chainThis = MetaShared.ComputeChainThis(id, zipHash, prevThis);
            
            // メタ構築            
            var meta = new MetaDocument
            {
                Schema = 1,
                Id = id,
                Seq = input.Seq,
                Utc = utcIso,
                Space = space,
                Zip = zipRel,
                Message = input.Message ?? string.Empty,
                Ignore = new MetaIgnore
                {
                    Source = input.IgnoreSourceFileName,
                    Rules = rules,
                },
                Hash = new MetaHash
                {
                    Algo = "SHA256",
                    Zip = zipHash,
                    Chain = new MetaHashChain
                    {
                        PrevId = prevId,
                        Prev = prevThis,
                        This = chainThis
                    }
                }
            };
            
            // 書き出し            
            var outPath = Path.Combine(metaDir, $"{id}.json");
            await using (var fs = new FileStream(outPath, FileMode.Create, FileAccess.Write, FileShare.None))
            await using (var writer = new Utf8JsonWriter(fs, new JsonWriterOptions { Indented = true }))
            {
                JsonSerializer.Serialize(writer, meta, WriteJsonOptions);
            }

            return new MetaWriteResult { MetaPath = outPath, Meta = meta };
        }

        /// <inheritdoc/>
        public async Task<int> RestitchHashesAsync(string repoRoot, string space, CancellationToken ct = default)
        {
            var sp = MetaShared.SanitizeSpace(space);
            var dataDir = Path.Combine(repoRoot, ".rinne", "data", sp);
            var metaDir = Path.Combine(dataDir, "meta");
            if (!Directory.Exists(metaDir)) return 0;

            var metaPaths = Directory.EnumerateFiles(metaDir, "*.json")
                                     .OrderBy(p => Path.GetFileNameWithoutExtension(p), StringComparer.Ordinal)
                                     .ToList();
            if (metaPaths.Count == 0) return 0;

            string? prevId = null;
            string? prevThis = null;

            for (int i = 0; i < metaPaths.Count; i++)
            {
                ct.ThrowIfCancellationRequested();

                MetaDocument meta;
                await using (var fs = new FileStream(metaPaths[i], FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    meta = (await JsonSerializer.DeserializeAsync<MetaDocument>(fs, ReadJsonOptions, ct).ConfigureAwait(false))
                          ?? throw new InvalidOperationException($"meta parse error: {metaPaths[i]}");
                }

                meta.Hash ??= new MetaHash { Algo = "SHA256" };
                meta.Hash.Chain ??= new MetaHashChain();

                // 先頭だけ null、以降は直前の値を設定
                meta.Hash.Chain.PrevId = prevId;
                meta.Hash.Chain.Prev = prevThis;

                var zipHash = meta.Hash.Zip ?? string.Empty;
                var thisHash = MetaShared.ComputeChainThis(meta.Id, zipHash, prevThis);

                meta.Hash.Chain.This = thisHash;

                await using (var ofs = new FileStream(metaPaths[i], FileMode.Create, FileAccess.Write, FileShare.None))
                await using (var writer = new Utf8JsonWriter(ofs, new JsonWriterOptions { Indented = true }))
                {
                    JsonSerializer.Serialize(writer, meta, WriteJsonOptions);
                }

                prevId = Path.GetFileNameWithoutExtension(metaPaths[i]); // 次の要素用に更新
                prevThis = thisHash;
            }

            return metaPaths.Count;
        }


        /// <summary>
        /// 入力パラメータの基本検証を行います。
        /// </summary>
        /// <param name="i">メタ生成入力。</param>
        private static void Validate(MetaWriteInput i)
        {
            if (string.IsNullOrWhiteSpace(i.RepoRoot))
                throw new ArgumentException("RepoRoot is required.", nameof(i.RepoRoot));
            if (string.IsNullOrWhiteSpace(i.Space))
                throw new ArgumentException("Space is required.", nameof(i.Space));
            if (i.Seq <= 0)
                throw new ArgumentException("Seq must be >= 1.", nameof(i.Seq));
            if (string.IsNullOrWhiteSpace(i.ZipAbsolutePath))
                throw new ArgumentException("ZipAbsolutePath is required.", nameof(i.ZipAbsolutePath));
        }
    }
}
