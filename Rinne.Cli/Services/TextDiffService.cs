using Rinne.Cli.Interfaces.Services;
using Rinne.Cli.Utility;
using System.Collections.ObjectModel;
using System.IO.Compression;

namespace Rinne.Cli.Models
{
    /// <summary>
    /// ZIP スナップショット間のテキスト差分を実行するサービス実装。
    /// </summary>
    public sealed class TextDiffService : ITextDiffService
    {
        /// <inheritdoc/>
        public async Task<TextDiffRun> RunAsync(TextDiffRequest request, CancellationToken cancellationToken = default)
        {
            if (request is null) throw new ArgumentNullException(nameof(request));

            var layout = new RepositoryLayout(Directory.GetCurrentDirectory());

            // スペース解決
            var space = layout.ResolveSpace(request.Space);

            // data ディレクトリ検証
            var dataDir = layout.GetSpaceDataDir(space);
            if (!Directory.Exists(dataDir))
                throw new DirectoryNotFoundException($"data ディレクトリが見つかりません: {dataDir}");

            // 比較対象 ZIP の解決
            var (oldZipPath, newZipPath) = ResolveZipPair(dataDir, request.OldId, request.NewId);

            // 作業ディレクトリの確保
            Directory.CreateDirectory(layout.TempDir);
            var workName = string.IsNullOrWhiteSpace(request.WorkName)
                ? $"TextDiff_{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid():N}"
                : request.WorkName!;
            var workDir = Path.Combine(layout.TempDir, workName);
            var oldDir = Path.Combine(workDir, "old");
            var newDir = Path.Combine(workDir, "new");
            Directory.CreateDirectory(oldDir);
            Directory.CreateDirectory(newDir);

            var cleaned = false;

            try
            {
                // ZIP 展開
                ZipFile.ExtractToDirectory(oldZipPath, oldDir, overwriteFiles: true);
                ZipFile.ExtractToDirectory(newZipPath, newDir, overwriteFiles: true);

                // フォルダ比較
                var result = await FolderTextDiffer.CompareAsync(oldDir, newDir, cancellationToken).ConfigureAwait(false);

                // サマリ計算
                var files = result.Files
                    .OrderBy(f => f.RelativePath, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var run = new TextDiffRun
                {
                    Space = space,
                    OldZipPath = oldZipPath,
                    NewZipPath = newZipPath,
                    OldExtractDir = oldDir,
                    NewExtractDir = newDir,
                    Files = new ReadOnlyCollection<FileTextDiffResult>(files),
                    TotalCount = files.Count,
                    AddedCount = files.Count(f => f.Change == FileChangeKind.Added),
                    RemovedCount = files.Count(f => f.Change == FileChangeKind.Removed),
                    ModifiedCount = files.Count(f => f.Change == FileChangeKind.Modified),
                    SkippedCount = files.Count(f => f.Change == FileChangeKind.SkippedNonText),
                    UnchangedCount = files.Count(f => f.Change == FileChangeKind.Unchanged),
                    IsWorkDirectoryCleaned = false
                };

                // 要求に従いクリーンアップ
                if (!request.KeepWorkDirectory)
                {
                    TryDelete(workDir);
                    cleaned = true;
                    run = run with { IsWorkDirectoryCleaned = true };
                }

                return run;
            }
            catch
            {
                // 例外時も後始末（Keep 指定が無い場合）
                if (!request.KeepWorkDirectory)
                {
                    TryDelete(workDir);
                    cleaned = true;
                }
                throw;
            }
            finally
            {
                // 二重削除防止
                if (!cleaned && !request.KeepWorkDirectory)
                {
                    TryDelete(workDir);
                }
            }
        }

        /// <summary>
        /// 比較対象となる 2 つの ZIP パスを解決します。
        /// </summary>
        private static (string oldZipPath, string newZipPath) ResolveZipPair(string dataDir, string? oldId, string? newId)
        {
            if (string.IsNullOrWhiteSpace(oldId) && string.IsNullOrWhiteSpace(newId))
            {
                var (latest, previous) = GetLatestTwoZipFiles(dataDir);
                if (latest is null || previous is null)
                    throw new InvalidOperationException("比較に必要な ZIP が 2 件未満です。");
                return (previous, latest);
            }

            if (string.IsNullOrWhiteSpace(oldId) ^ string.IsNullOrWhiteSpace(newId))
                throw new InvalidOperationException("ID を片方だけ指定することはできません。両方指定するか、両方省略してください。");

            var oldZipPath = GetZipPath(dataDir, oldId!);
            var newZipPath = GetZipPath(dataDir, newId!);

            if (!File.Exists(oldZipPath))
                throw new FileNotFoundException($"ZIP が存在しません: {oldZipPath}", oldZipPath);

            if (!File.Exists(newZipPath))
                throw new FileNotFoundException($"ZIP が存在しません: {newZipPath}", newZipPath);

            return (oldZipPath, newZipPath);
        }

        /// <summary>
        /// 指定スペース内の直近２つのファイルを返す。
        /// </summary>
        private static (string? latest, string? previous) GetLatestTwoZipFiles(string dataDir)
        {
            var files = Directory.EnumerateFiles(dataDir, "*.zip", SearchOption.TopDirectoryOnly)
                                 .OrderByDescending(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                                 .ToArray();

            if (files.Length >= 2) return (files[0], files[1]);
            if (files.Length == 1) return (files[0], null);
            return (null, null);
        }

        /// <summary>
        /// ID から ZIP のフルパスを得ます（拡張子省略時は.zipを付与）。
        /// </summary>
        private static string GetZipPath(string dataDir, string idOrFileName)
        {
            var name = idOrFileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                ? idOrFileName
                : idOrFileName + ".zip";
            return Path.Combine(dataDir, name);
        }

        /// <summary>
        /// ディレクトリの削除（例外を握りつぶして続行）。
        /// </summary>
        private static void TryDelete(string dir)
        {
            try
            {
                if (Directory.Exists(dir))
                    Directory.Delete(dir, recursive: true);
            }
            catch { /* ignore */ }
        }
    }
}
