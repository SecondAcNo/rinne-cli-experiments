using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Rinne.Cli.Interfaces.Services;
using Rinne.Cli.Models;

namespace Rinne.Cli.Services
{
    /// <summary>
    /// セーブのメタ情報（meta.json）を取得・整形表示するサービス実装。
    /// </summary>
    public sealed class ShowService : IShowService
    {
        /// <inheritdoc/>
        public async Task<ShowResult> GetFormattedMetaAsync(
            string repoRoot,
            string? id,
            string? space,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var layout = new RepositoryLayout(repoRoot);

                if (!Directory.Exists(layout.RinneDir))
                {
                    return ShowResult.Fail(1, "[error] Rinne リポジトリが見つかりません。init を実行してください。");
                }

                // space の既定解決（current）
                if (string.IsNullOrWhiteSpace(space) && File.Exists(layout.CurrentSpacePath))
                {
                    space = (await File.ReadAllTextAsync(layout.CurrentSpacePath, cancellationToken)).Trim();
                }

                if (string.IsNullOrWhiteSpace(space))
                {
                    return ShowResult.Fail(1, "[error] space が指定されておらず、current も存在しません。");
                }

                var spaceMetaDir = layout.GetSpaceMetaDir(space);
                if (!Directory.Exists(spaceMetaDir))
                {
                    return ShowResult.Fail(1, $"[error] 指定されたスペース '{space}' のメタディレクトリが存在しません。");
                }

                // id が省略された場合は最新を選択
                if (string.IsNullOrWhiteSpace(id))
                {
                    var latest = new DirectoryInfo(spaceMetaDir)
                        .GetFiles("*.json", SearchOption.TopDirectoryOnly)
                        .OrderByDescending(f => f.LastWriteTimeUtc)
                        .FirstOrDefault();

                    if (latest is null)
                    {
                        return ShowResult.Fail(0, $"[info] スペース '{space}' にメタ情報は存在しません。");
                    }

                    id = Path.GetFileNameWithoutExtension(latest.Name);
                }

                var metaPath = Path.Combine(spaceMetaDir, $"{id}.json");
                if (!File.Exists(metaPath))
                {
                    return ShowResult.Fail(1, $"[error] meta ファイルが見つかりません: {metaPath}");
                }

                // 読み込み（ファイル内は \uXXXX のままでも OK）
                var json = await File.ReadAllTextAsync(metaPath, cancellationToken);

                // ここで JsonDocument と Utf8JsonWriter を使い、
                // エンコーダを明示して「日本語を素のまま」整形出力する
                using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true });

                using var ms = new MemoryStream();
                using (var writer = new Utf8JsonWriter(ms, new JsonWriterOptions
                {
                    Indented = true,
                    // JS埋め込みを想定しないローカル用途：全Unicode素通し
                    // （Webに埋め込む予定があるなら Default に切替）
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                }))
                {
                    doc.RootElement.WriteTo(writer);
                }

                var formatted = Encoding.UTF8.GetString(ms.ToArray());

                return ShowResult.Ok(formatted, space, id, metaPath);
            }
            catch (OperationCanceledException)
            {
                return ShowResult.Fail(1, "[error] 操作がキャンセルされました。");
            }
            catch (Exception ex)
            {
                return ShowResult.Fail(1, $"[error] show サービス実行中に例外が発生しました: {ex.Message}");
            }
        }
    }
}
