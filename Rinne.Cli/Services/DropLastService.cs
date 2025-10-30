using System.Text;
using System.Text.RegularExpressions;
using Rinne.Cli.Interfaces.Services;
using Rinne.Cli.Models;
using Rinne.Cli.Utility; // LockFile を想定

namespace Rinne.Cli.Services
{
    /// <summary>最新履歴（1件）を削除するサービス。</summary>
    public sealed class DropLastService : IDropLastService
    {
        private static readonly Regex ZipIdRegex = new(@"^(?<seq>\d{8})_.+\.zip$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex MetaSeqRegex = new(@"^(?<seq>\d{8})(?:_.+)?\.json$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <inheritdoc/>
        public Task<DropLastResult> DropLastAsync(string repoRoot, string? space, bool confirmed, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(repoRoot))
                return Task.FromResult(DropLastResult.Fail(2, "Repository root is required."));

            var layout = new RepositoryLayout(repoRoot);

            if (!Directory.Exists(layout.RinneDir) || !Directory.Exists(layout.DataRootDir))
                return Task.FromResult(DropLastResult.Fail(3, ".rinne が見つかりません。init を実行してください。"));

            var resolvedSpace = layout.ResolveSpace(space);
            layout.EnsureSpaceStructure(resolvedSpace);

            var dataDir = layout.GetSpaceDataDir(resolvedSpace);
            var metaDir = layout.GetSpaceMetaDir(resolvedSpace);

            // 最新 zip を決定
            var zipFiles = Directory.EnumerateFiles(dataDir, "*.zip", SearchOption.TopDirectoryOnly)
                                    .OrderBy(p => Path.GetFileName(p), StringComparer.Ordinal)
                                    .ToList();

            if (zipFiles.Count == 0)
                return Task.FromResult(DropLastResult.Ok(resolvedSpace, null)); // 何もない → 何もしない成功

            var latestZipPath = zipFiles[^1];
            var latestZipName = Path.GetFileName(latestZipPath)!;

            // zip 名から seq を取得（例: 00000012_20251026010101000.zip → 00000012）
            var m = ZipIdRegex.Match(latestZipName);
            if (!m.Success)
                return Task.FromResult(DropLastResult.Fail(4, $"zip ファイル名から seq を解釈できません: {latestZipName}"));

            var seq = m.Groups["seq"].Value;

            // meta を seq 一致で探索（<seq>.json または <seq>_*.json）
            var metaPath = FindMetaBySeq(metaDir, seq);
            if (metaPath is null)
                return Task.FromResult(DropLastResult.Fail(5, $"対応する meta json が見つかりません（seq={seq}）。"));

            if (!confirmed)
                return Task.FromResult(DropLastResult.Fail(10, "削除の確認がされていません。（--yes が必要）"));

            using var _ = LockFile.Acquire(layout.RinneDir, "drop-last", TimeSpan.FromMinutes(5));
            cancellationToken.ThrowIfCancellationRequested();

            // 削除
            DeleteIfExists(latestZipPath);
            DeleteIfExists(metaPath);

            // 表示用に削除IDは zip の先頭8桁＋後続までをそのまま渡す
            var deletedId = Path.GetFileNameWithoutExtension(latestZipName);

            return Task.FromResult(DropLastResult.Ok(resolvedSpace, deletedId));
        }

        /// <summary>seq 一致の meta を探す（<seq>.json または <seq>_*.json）。</summary>
        /// <param name="metaDir">meta ディレクトリ。</param>
        /// <param name="seq">8桁連番。</param>
        private static string? FindMetaBySeq(string metaDir, string seq)
        {
            if (!Directory.Exists(metaDir)) return null;

            // 候補を列挙して seq 一致でフィルタ
            var candidates = Directory.EnumerateFiles(metaDir, "*.json", SearchOption.TopDirectoryOnly)
                                      .Where(p =>
                                      {
                                          var name = Path.GetFileName(p)!;
                                          var mm = MetaSeqRegex.Match(name);
                                          return mm.Success && mm.Groups["seq"].Value == seq;
                                      })
                                      .OrderByDescending(p => p.Length) // <seq>_*.json を <seq>.json より優先（長い方）
                                      .ThenBy(p => Path.GetFileName(p), StringComparer.Ordinal)
                                      .ToList();

            return candidates.FirstOrDefault();
        }

        private static void DeleteIfExists(string path)
        {
            if (File.Exists(path))
            {
                File.SetAttributes(path, FileAttributes.Normal);
                File.Delete(path);
            }
        }
    }
}
