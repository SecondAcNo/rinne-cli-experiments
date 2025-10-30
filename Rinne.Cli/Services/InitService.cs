using Rinne.Cli.Interfaces.Services;
using Rinne.Cli.Models;
using System.Text.Json;

namespace Rinne.Cli.Services
{
    /// <summary>
    /// Rinne リポジトリ初期化の既定実装。
    /// </summary>
    public sealed class InitService : IInitService
    {
        /// <inheritdoc/>
        public async Task<bool> InitializeAsync(string repoRoot, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(repoRoot);

            var layout = new RepositoryLayout(repoRoot);
            bool created = layout.WriteFolderStructure();
            WriteInitJson(layout);

            await Task.CompletedTask;

            return created;
        }

        /// <summary>
        /// 指定されたリポジトリレイアウトに基づいて .rinne/config/init.json を生成します。
        /// </summary>
        /// <param name="layout">リポジトリレイアウト。</param>
        private static void WriteInitJson(RepositoryLayout layout)
        {
            ArgumentNullException.ThrowIfNull(layout);

            var obj = new
            {
                created = DateTime.UtcNow.ToString("o"),
                uuid = Guid.NewGuid().ToString(),
                root = layout.RepoRoot
            };

            var json = JsonSerializer.Serialize(obj, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            var path = Path.Combine(layout.ConfigDir, "init.json");
            File.WriteAllText(path, json);
        }
    }
}
