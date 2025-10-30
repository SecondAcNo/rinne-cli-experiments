using Rinne.Cli.Interfaces.Services;
using Rinne.Cli.Models;

namespace Rinne.Cli.Services
{
    /// <summary>
    /// ISpaceServiceの既定実装。
    /// </summary>
    public sealed class SpaceService : ISpaceService
    {
        /// <inheritdoc/>
        public Task<string?> GetCurrentAsync(string repoRoot, CancellationToken cancellationToken = default)
        {
            var layout = EnsureRinneExists(repoRoot);
            if (!File.Exists(layout.CurrentSpacePath)) return Task.FromResult<string?>(null);

            var first = File.ReadLines(layout.CurrentSpacePath).FirstOrDefault();
            return Task.FromResult(string.IsNullOrWhiteSpace(first) ? null : first!.Trim());
        }

        /// <inheritdoc/>
        public Task<string[]> ListAsync(string repoRoot, CancellationToken cancellationToken = default)
        {
            var layout = EnsureRinneExists(repoRoot);
            var spaces = layout.EnumerateSpaces();
            return Task.FromResult(spaces
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                .ToArray());
        }

        /// <inheritdoc/>
        public Task SelectAsync(string repoRoot, string name, bool createIfMissing, CancellationToken cancellationToken = default)
        {
            var layout = EnsureRinneExists(repoRoot);

            var sanitized = Sanitize(layout, name);
            var spaceDir = layout.GetSpaceDataDir(sanitized);

            if (createIfMissing)
            {
                layout.EnsureSpaceStructure(sanitized);
            }

            if (!Directory.Exists(spaceDir))
                throw new DirectoryNotFoundException($"Space '{sanitized}' does not exist. Use --create to create it.");

            WriteCurrent(layout, sanitized);
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task CreateAsync(string repoRoot, string name, CancellationToken cancellationToken = default)
        {
            var layout = EnsureRinneExists(repoRoot);

            var sanitized = Sanitize(layout, name);
            var spaceDir = layout.GetSpaceDataDir(sanitized);

            if (Directory.Exists(spaceDir))
                throw new InvalidOperationException($"Space '{sanitized}' already exists.");

            layout.EnsureSpaceStructure(sanitized);
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task RenameAsync(string repoRoot, string oldName, string newName, CancellationToken cancellationToken = default)
        {
            var layout = EnsureRinneExists(repoRoot);

            var oldSan = Sanitize(layout, oldName);
            var newSan = Sanitize(layout, newName);

            var oldDir = layout.GetSpaceDataDir(oldSan);
            var newDir = layout.GetSpaceDataDir(newSan);

            if (!Directory.Exists(oldDir))
                throw new DirectoryNotFoundException($"Space '{oldSan}' does not exist.");
            if (Directory.Exists(newDir))
                throw new InvalidOperationException($"Space '{newSan}' already exists.");

            Directory.Move(oldDir, newDir);

            var current = ReadCurrent(layout);
            if (string.Equals(current, oldSan, StringComparison.Ordinal))
            {
                WriteCurrent(layout, newSan);
            }

            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public Task DeleteAsync(string repoRoot, string name, bool force, CancellationToken cancellationToken = default)
        {
            var layout = EnsureRinneExists(repoRoot);

            var sanitized = Sanitize(layout, name);
            var dir = layout.GetSpaceDataDir(sanitized);

            if (!Directory.Exists(dir))
                throw new DirectoryNotFoundException($"Space '{sanitized}' does not exist.");

            var current = ReadCurrent(layout);
            if (string.Equals(current, sanitized, StringComparison.Ordinal))
                throw new InvalidOperationException($"Cannot delete the current space '{sanitized}'. Select another space first.");

            if (!force && Directory.EnumerateFileSystemEntries(dir).Any())
                throw new InvalidOperationException($"Space '{sanitized}' is not empty. Use --force to delete it.");

            Directory.Delete(dir, recursive: true);
            return Task.CompletedTask;
        }

        /// <summary>
        /// .rinneが存在するか確認
        /// </summary>
        /// <param name="repoRoot">対象のルートフォルダ</param>
        /// <returns>リポジトリレイアウト</returns>
        private static RepositoryLayout EnsureRinneExists(string repoRoot)
        {
            var layout = new RepositoryLayout(repoRoot);
            if (!Directory.Exists(layout.RinneDir))
                throw new InvalidOperationException($".rinne directory not found at '{layout.RinneDir}'. Run 'rinne init' first.");
            return layout;
        }

        /// <summary>
        /// パス注入を避けるため、スペース名を無害化します。
        /// </summary>
        /// <param name="layout">リポジトリ構造</param>
        /// <param name="raw">スペース名</param>
        private static string Sanitize(RepositoryLayout layout, string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                throw new ArgumentException("Space name is required.", nameof(raw));

            // layoutのAPIで正規化：GetSpaceDataDir で一旦結合→FileName 抜き出し
            return Path.GetFileName(layout.GetSpaceDataDir(raw));
        }

        /// <summary>
        /// カレントスペース名取得
        /// </summary>
        /// <param name="layout">リポジトリ構造</param>
        /// <returns>スペース名</returns>
        private static string? ReadCurrent(RepositoryLayout layout)
            => File.Exists(layout.CurrentSpacePath)
                ? (File.ReadLines(layout.CurrentSpacePath).FirstOrDefault() ?? string.Empty).Trim()
                : null;

        /// <summary>
        /// カレントスペース名の書き込み
        /// </summary>
        /// <param name="layout">リポジトリ構造</param>
        /// <param name="space">スペース名</param>
        private static void WriteCurrent(RepositoryLayout layout, string space)
            => File.WriteAllText(layout.CurrentSpacePath, space + Environment.NewLine);
    }
}
