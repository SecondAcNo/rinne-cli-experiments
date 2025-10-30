using Rinne.Cli.Interfaces.Services;
using Rinne.Cli.Models;
using System.Text.Json;

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
                if (string.IsNullOrWhiteSpace(space))
                {
                    if (File.Exists(layout.CurrentSpacePath))
                    {
                        space = (await File.ReadAllTextAsync(layout.CurrentSpacePath, cancellationToken)).Trim();
                    }
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

                    if (latest == null)
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

                var json = await File.ReadAllTextAsync(metaPath, cancellationToken);

                // 整形
                using var doc = JsonDocument.Parse(json);
                var formatted = JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });

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
