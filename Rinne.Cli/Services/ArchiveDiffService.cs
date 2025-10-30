using Rinne.Cli.Interfaces.Services;
using Rinne.Cli.Models;
using Rinne.Cli.Utility;
using System.IO.Compression;

namespace Rinne.Cli.Services
{
    /// <summary>
    /// ZIP を一時展開してフォルダ差分を計算するサービスの既定実装。
    /// </summary>
    public sealed class ArchiveDiffService : IArchiveDiffService
    {
        /// <inheritdoc/>
        public async Task<ArchiveDiffOutcome> DiffAsync(
            string repoRoot,
            string? id1,
            string? id2,
            string? space,
            CancellationToken cancellationToken = default)
        {
            var layout = new RepositoryLayout(repoRoot);

            EnsureRepository(layout);

            // space 解決
            var resolvedSpace = !string.IsNullOrWhiteSpace(space)
                ? space!
                : (ReadCurrentSpace(layout) ?? RepositoryLayout.DefaultSpace);

            var dataDir = layout.GetSpaceDataDir(resolvedSpace);
            if (!Directory.Exists(dataDir))
                throw new DirectoryNotFoundException($"space '{resolvedSpace}' の data ディレクトリが見つかりません: {dataDir}");

            // id 解決
            string rid1, rid2;
            if (!string.IsNullOrWhiteSpace(id1) && !string.IsNullOrWhiteSpace(id2))
            {
                rid1 = id1!;
                rid2 = id2!;
            }
            else
            {
                (rid1, rid2) = ResolveLatestPair(dataDir);
            }

            var zip1 = Path.Combine(dataDir, $"{rid1}.zip");
            var zip2 = Path.Combine(dataDir, $"{rid2}.zip");

            EnsureZip(zip1, rid1, resolvedSpace);
            EnsureZip(zip2, rid2, resolvedSpace);

            // 作業ディレクトリ
            var workRoot = Path.Combine(layout.TempDir, $"diff_{Sanitize(resolvedSpace)}_{DateTime.UtcNow.Ticks}");
            var extract1 = Path.Combine(workRoot, "A");
            var extract2 = Path.Combine(workRoot, "B");

            Directory.CreateDirectory(extract1);
            Directory.CreateDirectory(extract2);

            try
            {
                // 展開
                await Task.Run(() =>
                {
                    ZipFile.ExtractToDirectory(zip1, extract1, overwriteFiles: true);
                    ZipFile.ExtractToDirectory(zip2, extract2, overwriteFiles: true);
                }, cancellationToken);

                // 差分計算（ハッシュ厳密比較）
                var result = await Task.Run(
                    () => FolderDiffer.DiffDirectories(extract1, extract2, computeHash: true),
                    cancellationToken);

                return new ArchiveDiffOutcome
                {
                    Space = resolvedSpace,
                    Id1 = rid1,
                    Id2 = rid2,
                    Result = result,
                    ZipPath1 = zip1,
                    ZipPath2 = zip2
                };
            }
            finally
            {
                // クリーンアップ
                try
                {
                    if (Directory.Exists(workRoot))
                        Directory.Delete(workRoot, recursive: true);
                }
                catch { /* ignore */ }
            }
        }

        /// <summary>
        /// 一時リポジトリの確保
        /// </summary>
        /// <param name="layout">リポジトリ構造</param>
        private static void EnsureRepository(RepositoryLayout layout)
        {
            if (!Directory.Exists(layout.RinneDir))
                throw new DirectoryNotFoundException($".rinne ディレクトリが見つかりません: {layout.RinneDir}");
            Directory.CreateDirectory(layout.TempDir);
        }

        /// <summary>
        /// currentファイル内のスペース名の読み取り
        /// </summary>
        /// <param name="layout">リポジトリ構造</param>
        /// <returns>スペース名</returns>
        private static string? ReadCurrentSpace(RepositoryLayout layout)
        {
            try
            {
                if (File.Exists(layout.CurrentSpacePath))
                {
                    var text = File.ReadAllText(layout.CurrentSpacePath).Trim();
                    return string.IsNullOrWhiteSpace(text) ? null : text;
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// 最新履歴のペアのID解決
        /// </summary>
        /// <param name="dataDir">対象ディレクトリ</param>
        /// <returns>最新-1IDと最新ID</returns>
        /// <exception cref="InvalidOperationException"></exception>
        private static (string olderId, string newerId) ResolveLatestPair(string dataDir)
        {
            var zips = Directory.EnumerateFiles(dataDir, "*.zip", SearchOption.TopDirectoryOnly)
                                .Select(p => new FileInfo(p))
                                .OrderBy(fi => fi.Name, StringComparer.Ordinal)
                                .ToArray();
            if (zips.Length < 2)
                throw new InvalidOperationException($"比較可能な ZIP が 2 件以上ありません。（dir: {dataDir}）");

            var older = zips[^2];
            var newer = zips[^1];
            return (Path.GetFileNameWithoutExtension(older.Name),
                    Path.GetFileNameWithoutExtension(newer.Name));
        }

        /// <summary>
        /// 履歴の存在確認
        /// </summary>
        /// <param name="zipPath">対象パス</param>
        /// <param name="id">id（表示用）</param>
        /// <param name="space">space（表示用）</param>
        private static void EnsureZip(string zipPath, string id, string space)
        {
            if (!File.Exists(zipPath))
                throw new FileNotFoundException($"id '{id}' の ZIP が見つかりません（space='{space}'）", zipPath);
        }

        /// <summary>
        /// 簡易サニタイズ
        /// </summary>
        /// <param name="s">対象文字列</param>
        /// <returns>サニタイズ後文字列</returns>
        private static string Sanitize(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "x";
            foreach (var c in Path.GetInvalidFileNameChars())
                s = s.Replace(c, '-');
            return s.Replace('/', '-').Replace('\\', '-').Trim();
        }
    }
}
