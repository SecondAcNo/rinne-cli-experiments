using Rinne.Cli.Interfaces.Services;
using Rinne.Cli.Models;
using System.Security.Cryptography;

namespace Rinne.Cli.Services
{
    /// <summary>
    /// 単純コピーで取り込む実装。
    /// </summary>
    public sealed class SpaceImportService : ISpaceImportService
    {
        /// <inheritdoc/>
        public Task<SpaceImportResult> ImportAsync(RepositoryLayout targetLayout, SpaceImportRequest request, CancellationToken ct = default)
        {
            if (targetLayout is null) throw new ArgumentNullException(nameof(targetLayout));
            if (string.IsNullOrWhiteSpace(request.SourceRoot)) throw new ArgumentException("SourceRoot is required.", nameof(request));
            if (string.IsNullOrWhiteSpace(request.SourceSpace)) throw new ArgumentException("SourceSpace is required.", nameof(request));

            var sourceLayout = new RepositoryLayout(Path.GetFullPath(request.SourceRoot));
            var srcSpace = request.SourceSpace;
            var srcDir = sourceLayout.GetSpaceDataDir(srcSpace);

            if (!Directory.Exists(sourceLayout.RinneDir))
                return Task.FromResult(Fail("取り込み元の .rinne が見つかりません。"));
            if (!Directory.Exists(srcDir))
                return Task.FromResult(Fail("取り込み元の space フォルダが見つかりません。"));

            var effective = ResolveTargetSpaceName(targetLayout, srcSpace, request.OnConflict);
            if (effective is null)
                return Task.FromResult(Fail("取り込み先 space が既に存在します（fail）。"));

            var dstDir = targetLayout.GetSpaceDataDir(effective);

            // Clean 指定で既存削除
            if (request.OnConflict == SpaceImportConflictMode.Clean && Directory.Exists(dstDir))
            {
                Directory.Delete(dstDir, recursive: true);
            }

            // コピー実行
            CopyDirectory(srcDir, dstDir, ct);

            return Task.FromResult(new SpaceImportResult
            {
                ExitCode = 0,
                EffectiveSpace = effective
            });
        }


        /// <summary>
        /// 衝突動作に従い space 名を決定する。
        /// </summary>
        /// <param name="layout">取り込み先レイアウト。</param>
        /// <param name="desired">希望名。</param>
        /// <param name="mode">衝突モード。</param>
        /// <returns>決定名。失敗時は null。</returns>
        private static string? ResolveTargetSpaceName(RepositoryLayout layout, string desired, SpaceImportConflictMode mode)
        {
            var exists = Directory.Exists(layout.GetSpaceDataDir(desired));
            if (!exists) return desired;

            return mode switch
            {
                SpaceImportConflictMode.Fail => null,
                SpaceImportConflictMode.Clean => desired,
                _ => GenerateRenamedName(layout, desired),
            };
        }

        /// <summary>
        /// リネーム用にサフィックス付きの新しい space 名を生成する。
        /// </summary>
        private static string GenerateRenamedName(RepositoryLayout layout, string desired)
        {
            var ts = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            var candidate = $"{desired}-{ts}-{ShortRandom()}";
            int tries = 0;
            while (Directory.Exists(layout.GetSpaceDataDir(candidate)) && tries++ < 5)
                candidate = $"{desired}-{ts}-{ShortRandom()}";
            return candidate;
        }

        /// <summary>
        /// ディレクトリを再帰コピーする。
        /// </summary>
        private static void CopyDirectory(string src, string dst, CancellationToken ct)
        {
            Directory.CreateDirectory(dst);

            foreach (var file in Directory.EnumerateFiles(src))
            {
                ct.ThrowIfCancellationRequested();
                var name = Path.GetFileName(file);
                File.Copy(file, Path.Combine(dst, name), overwrite: false);
            }

            foreach (var dir in Directory.EnumerateDirectories(src))
            {
                ct.ThrowIfCancellationRequested();
                var name = Path.GetFileName(dir);
                CopyDirectory(dir, Path.Combine(dst, name), ct);
            }
        }

        /// <summary>
        /// 短いランダムサフィックス（base36/4文字）を作る。
        /// </summary>
        private static string ShortRandom()
        {
            Span<byte> b = stackalloc byte[3];
            RandomNumberGenerator.Fill(b);
            var n = (b[0] << 16) | (b[1] << 8) | b[2];
            const string a = "0123456789abcdefghijklmnopqrstuvwxyz";
            char[] s = new char[4];
            for (int i = 0; i < 4; i++)
            {
                s[3 - i] = a[n % 36];
                n /= 36;
            }
            return new string(s);
        }

        /// <summary>
        /// 失敗結果を返す。
        /// </summary>
        private static SpaceImportResult Fail(string msg)
            => new SpaceImportResult { ExitCode = 2, EffectiveSpace = string.Empty, Message = msg };

    }
}
