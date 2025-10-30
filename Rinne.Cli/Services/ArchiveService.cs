using Rinne.Cli.Interfaces.Services;
using Rinne.Cli.Models;
using Rinne.Cli.System;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace Rinne.Cli.Services
{
    /// <summary>
    /// ディレクトリ配下のファイルを ZIP アーカイブとしてまとめるサービス。
    /// </summary>
    public sealed class ArchiveService : IArchiveService
    {
        private readonly AtomicFileWriter _atomic = new();

        /// <inheritdoc/>
        public async Task<string> CreateZipAsync(
            string rootDir,
            string outputZipPath,
            ArchiveZipOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            options ??= new ArchiveZipOptions();

            // --- 検証 ---
            if (string.IsNullOrWhiteSpace(rootDir))
                throw new ArgumentException("rootDir is null or empty.", nameof(rootDir));
            if (!Directory.Exists(rootDir))
                throw new DirectoryNotFoundException($"Root directory not found: {rootDir}");
            if (string.IsNullOrWhiteSpace(outputZipPath))
                throw new ArgumentException("outputZipPath is null or empty.", nameof(outputZipPath));

            var fullRoot = Path.GetFullPath(rootDir);
            var fullZip = Path.GetFullPath(outputZipPath);
            var excludeRegexes = CompileGlobRegexes(options.ExcludeGlobs);

            // --- Atomic ZIP 生成 ---
            return await _atomic.WriteStreamAsync(
                finalPath: fullZip,
                overwrite: options.Overwrite,
                writeToStream: (stream, ct) => ProduceZipAsync(stream, fullRoot, excludeRegexes, options, ct),
                cancellationToken
            ).ConfigureAwait(false);
        }

        /// <summary>
        /// 指定ディレクトリを ZIP に圧縮します
        /// </summary>
        /// <param name="stream">ZIP 出力先ストリーム。</param>
        /// <param name="fullRoot">圧縮対象ディレクトリの絶対パス。</param>
        /// <param name="excludeRegexes">除外対象の正規表現リスト。</param>
        /// <param name="options">ZIP 出力オプション。</param>
        /// <param name="ct">キャンセル トークン。</param>
        private static async Task ProduceZipAsync(
            Stream stream,
            string fullRoot,
            List<Regex> excludeRegexes,
            ArchiveZipOptions options,
            CancellationToken ct)
        {
            using var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);

            long fileCount = 0;
            long totalBytes = 0;

            foreach (var file in Directory.EnumerateFiles(fullRoot, "*", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();

                // 隠しファイル除外
                if (!options.IncludeHidden && IsHidden(file))
                    continue;

                // ルート相対パス（Unix形式）
                var rel = Path.GetRelativePath(fullRoot, file).Replace(Path.DirectorySeparatorChar, '/');
                if (rel.StartsWith("..", StringComparison.Ordinal))
                    continue;

                // 除外パターン
                if (IsExcluded(rel, excludeRegexes))
                    continue;

                // カスタムフィルタ
                if (options.IncludePredicate is not null && !options.IncludePredicate(rel))
                    continue;

                // --- ZIP エントリ作成 ---
                var entry = zip.CreateEntry(rel, options.CompressionLevel);
                entry.LastWriteTime = File.GetLastWriteTime(file);

                // --- ファイルコピー ---
                using var src = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, useAsync: true);
                using var dst = entry.Open();
                await src.CopyToAsync(dst, 1 << 16, ct).ConfigureAwait(false);

                fileCount++;
                totalBytes += src.Length;

                // --- 進捗通知 ---
                options.Progress?.Report(new ArchiveZipProgress(fileCount, totalBytes));
            }
        }

        /// <summary>
        /// 指定ファイルが隠し属性またはシステム属性を持つかを判定します。
        /// </summary>
        private static bool IsHidden(string path)
        {
            try
            {
                var attr = File.GetAttributes(path);
                return (attr & FileAttributes.Hidden) != 0 || (attr & FileAttributes.System) != 0;
            }
            catch
            {
                // アクセス不可は「非隠し」として扱う
                return false;
            }
        }

        /// <summary>
        /// 除外正規表現に一致するかを判定します。
        /// </summary>
        private static bool IsExcluded(string relativePathUnix, List<Regex> regexes)
        {
            foreach (var rx in regexes)
                if (rx.IsMatch(relativePathUnix))
                    return true;
            return false;
        }

        /// <summary>
        /// グロブ（ワイルドカード）パターンを正規表現へ変換します。
        /// </summary>
        /// <param name="globs">除外パターンのリスト。</param>
        /// <returns>正規表現のリスト。</returns>
        private static List<Regex> CompileGlobRegexes(IReadOnlyList<string> globs)
        {
            var list = new List<Regex>(globs.Count);

            foreach (var g in globs)
            {
                if (string.IsNullOrWhiteSpace(g))
                    continue;

                var normalized = NormalizeGlob(g);

                var pattern = "^" + Regex.Escape(normalized)
                    .Replace(@"\*\*", "§§DOUBLESTAR§§")
                    .Replace(@"\*", "[^/]*")
                    .Replace(@"\?", "[^/]")
                    .Replace("§§DOUBLESTAR§§", ".*")
                    + "$";

                list.Add(new Regex(pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase));
            }
            return list;

            // 内部関数: glob正規化
            static string NormalizeGlob(string glob)
            {
                var s = glob.Replace('\\', '/').Trim();

                if (s.StartsWith("./", StringComparison.Ordinal))
                    s = s[2..];

                // 末尾スラッシュ → ディレクトリ指定
                if (s.EndsWith("/"))
                    return s + "**";

                // ワイルドカード → そのまま
                if (s.Contains('*') || s.Contains('?'))
                    return s;

                // スラッシュ無し → ディレクトリ想定 (.rinne, node_modules 等)
                if (!s.Contains('/'))
                    return s + "/**";

                // その他はそのまま
                return s;
            }
        }
    }
}
