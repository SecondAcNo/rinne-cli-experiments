using Rinne.Cli.Interfaces.Services;
using Rinne.Cli.Models;

namespace Rinne.Cli.Services
{
    /// <summary>
    /// セーブ履歴（ZIP 一覧）を列挙するサービスの既定実装。
    /// </summary>
    /// <remarks>
    /// space の解決（省略時は current 読み取り）、
    /// ディレクトリ検証、ZIP 列挙（更新日時降順）を担当します。
    /// </remarks>
    public sealed class LogService : ILogService
    {
        /// <inheritdoc/>
        public async Task<SaveLogResult> GetLogAsync(
            RepositoryLayout layout,
            string? space,
            CancellationToken cancellationToken = default)
        {
            // Rinne リポジトリ存在チェック
            if (!Directory.Exists(layout.RinneDir))
                throw new InvalidOperationException("このディレクトリは Rinne リポジトリではありません。先に init を実行してください。");

            // space を解決（省略時は current）
            var resolvedSpace = await layout.ResolveSpaceStrictAsync(space, cancellationToken).ConfigureAwait(false);

            // space ディレクトリの検証
            var spaceDir = Path.Combine(layout.DataRootDir, resolvedSpace);
            if (!Directory.Exists(spaceDir))
                throw new InvalidOperationException($"指定されたスペース '{resolvedSpace}' のディレクトリが見つかりません。");

            // ZIP 一覧（更新日時の降順）
            var files = new DirectoryInfo(spaceDir)
                .GetFiles("*.zip", SearchOption.TopDirectoryOnly)
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Select(f => new SaveLogEntry(
                    FileName: f.Name,
                    FullPath: f.FullName,
                    LastWriteTimeLocal: f.LastWriteTime,
                    LengthBytes: f.Length))
                .ToList()
                .AsReadOnly();

            return new SaveLogResult(resolvedSpace, files);
        }
    }
}
