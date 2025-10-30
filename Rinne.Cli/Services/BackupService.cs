using Rinne.Cli.Interfaces.Services;
using Rinne.Cli.Models;
using Rinne.Cli.Utility;
using System.IO.Compression;
using System.Text;

namespace Rinne.Cli.Services
{
    /// <summary>
    /// .rinne/ を一時コピー → ZIP → SHA256 出力 → 一時削除、まで行う実装。
    /// </summary>
    public sealed class BackupService : IBackupService
    {
        /// <inheritdoc/>
        public async Task<BackupResult> BackupRinneAsync(string rootdir, string outputDir, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(rootdir))
                throw new ArgumentNullException(nameof(rootdir));
            if (string.IsNullOrWhiteSpace(outputDir))
                throw new ArgumentNullException(nameof(outputDir));

            var repoRoot = Path.GetFullPath(rootdir);
            if (!Directory.Exists(repoRoot))
                throw new DirectoryNotFoundException($"Repository root not found: {repoRoot}");

            var layout = new RepositoryLayout(repoRoot);
            var sourceDir = layout.RinneDir;

            if (!Directory.Exists(sourceDir))
                throw new DirectoryNotFoundException($".rinne ディレクトリが見つかりません: {sourceDir}");

            // 出力先（絶対化）
            var finalOutputDir = Path.GetFullPath(
                Path.IsPathRooted(outputDir) ? outputDir : Path.Combine(layout.RepoRoot, outputDir));
            Directory.CreateDirectory(finalOutputDir);

            // タイムスタンプ（ミリ秒）
            var timestamp = DateTime.Now.ToString("yyyyMMdd'T'HHmmssfff");
            var baseName = $"rinne_backup_{timestamp}";
            var zipPath = Path.Combine(finalOutputDir, baseName + ".zip");
            var hashPath = Path.Combine(finalOutputDir, baseName + ".sha256.txt");

            // カレント直下の一時フォルダ
            string? tempDir = null;
            try
            {
                tempDir = Path.Combine(layout.RepoRoot, $"temp_{timestamp}");
                var staging = Path.Combine(tempDir, ".rinne");

                Directory.CreateDirectory(staging);

                // 1) .rinne → staging に丸コピー（空フォルダ保持）
                await CopyDirectoryAsync(sourceDir, staging, cancellationToken).ConfigureAwait(false);

                // 2) staging から ZIP 作成（空フォルダも入る）
                ZipFile.CreateFromDirectory(staging, zipPath, CompressionLevel.NoCompression, includeBaseDirectory: false);

                // 3) SHA-256 計算 & 出力
                var sha256 = HashUtility.ComputeSha256(zipPath);
                var line = $"SHA256  {sha256}  {Path.GetFileName(zipPath)}{Environment.NewLine}";
                await File.WriteAllTextAsync(hashPath, line, Encoding.UTF8, cancellationToken).ConfigureAwait(false);

                return new BackupResult
                {
                    ZipPath = zipPath,
                    HashPath = hashPath,
                    Sha256 = sha256,
                    BaseName = baseName
                };
            }
            finally
            {
                // 4) 一時フォルダ削除（ベストエフォート）
                if (!string.IsNullOrEmpty(tempDir))
                {
                    try { Directory.Delete(tempDir, recursive: true); } catch { /* ignore */ }
                }
            }
        }

        /// <summary>
        /// ディレクトリを再帰コピー（空フォルダ保持）。
        /// </summary>
        /// <param name="sourceDir">対象フォルダ</param>
        /// <param name="destDir">出力フォルダ</param>
        /// <param name="ct">キャンセルトークン</param>
        /// <returns></returns>
        private static async Task CopyDirectoryAsync(string sourceDir, string destDir, CancellationToken ct)
        {
            foreach (var dir in Directory.EnumerateDirectories(sourceDir, "*", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();
                var rel = Path.GetRelativePath(sourceDir, dir);
                Directory.CreateDirectory(Path.Combine(destDir, rel));
            }

            Directory.CreateDirectory(destDir);

            foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();
                var rel = Path.GetRelativePath(sourceDir, file);
                var dst = Path.Combine(destDir, rel);

                Directory.CreateDirectory(Path.GetDirectoryName(dst)!);

                await using var src = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, useAsync: true);
                await using var dstFs = new FileStream(dst, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, useAsync: true);
                await src.CopyToAsync(dstFs, 1 << 20, ct).ConfigureAwait(false);

                try { File.SetLastWriteTime(dst, File.GetLastWriteTime(file)); } catch { /* ignore */ }
            }
        }
    }
}
