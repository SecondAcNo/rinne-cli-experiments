using Rinne.Cli.Interfaces.Services;
using Rinne.Cli.Models;
using Rinne.Cli.Utility;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace Rinne.Cli.Services
{
    /// <summary>
    /// スナップショット（ZIP）をワーキングツリーへ復元する既定実装。
    /// </summary>
    /// <remarks>
    /// 指定された履歴をプロジェクトルートへ展開し、現在の作業ツリーを丸ごと置き換えます。
    /// </remarks>
    public sealed class RestoreService : IRestoreService
    {
        private const string RinneDirName = ".rinne";
        private const string RinneIgnoreName = ".rinneignore";

        /// <summary>無視パターン正規表現の既定オプション。</summary>
        private static readonly RegexOptions RxOpts = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled;

        /// <inheritdoc/>
        /// <exception cref="ArgumentException"><paramref name="rootDir"/>／<paramref name="space"/>／<paramref name="id"/> が空。</exception>
        /// <exception cref="FileNotFoundException">スナップショット ZIP が存在しない。</exception>
        /// <exception cref="IOException">Zip Slip 検出や I/O 例外。</exception>
        public async Task RestoreAsync(string rootDir, string space, string id, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(rootDir)) throw new ArgumentException("Root directory path is required.", nameof(rootDir));
            if (string.IsNullOrWhiteSpace(space)) throw new ArgumentException("Space name is required.", nameof(space));
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Snapshot ID is required.", nameof(id));

            // レイアウト確定と ZIP の解決
            var layout = new RepositoryLayout(rootDir);
            var zipPath = Path.Combine(layout.GetSpaceDataDir(space), $"{id}.zip");
            if (!File.Exists(zipPath))
                throw new FileNotFoundException($"Snapshot not found: {zipPath}", zipPath);

            // .rinneignore のロードと正規表現化
            var ignoreList = IgnoreUtility.LoadIgnoreList(layout.IgnorePath);
            // .rinne/ は常に保護
            IgnoreUtility.EnsureForceExclude(ignoreList, RinneDirName + "/");
            var normalized = NormalizePatternsForDirs(ignoreList);
            var ignoreRegex = CompileGlobs(normalized);

            // 取引（ロールバック用）オブジェクト
            var backupRoot = Path.Combine(layout.TempDir, $"restore_{DateTime.UtcNow:yyyyMMddTHHmmssfff}_tx");
            Directory.CreateDirectory(backupRoot);

            // 上書き前のバックアップ・新規作成ファイルの追跡
            var backedUpFiles = new List<string>(); // バックアップ側の絶対パス（復帰時に戻す）
            var createdFiles = new List<string>(); // 新規作成（失敗時に削除）

            var rootFull = EnsureTrailingSep(Path.GetFullPath(layout.RepoRoot));

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                // 1) クリーン（無視対象・.rinne/.rinneignore を残す）
                CleanWorkingTreeTransactional(layout.RepoRoot, ignoreRegex, backupRoot, backedUpFiles, cancellationToken);

                cancellationToken.ThrowIfCancellationRequested();

                // 2) 展開（無視対象へは書き込まない／上書き前にバックアップ）
                using var archive = ZipFile.OpenRead(zipPath);

                foreach (var entry in archive.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // ディレクトリエントリはスキップ
                    if (string.IsNullOrEmpty(entry.Name))
                        continue;

                    // .rinne 配下は展開禁止
                    if (entry.FullName.StartsWith(RinneDirName + "/", StringComparison.OrdinalIgnoreCase) ||
                        entry.FullName.StartsWith(RinneDirName + "\\", StringComparison.OrdinalIgnoreCase))
                        continue;

                    // 出力先の絶対パス決定（Zip Slip 防止）
                    var relative = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
                    var destPath = Path.GetFullPath(Path.Combine(layout.RepoRoot, relative));
                    if (!destPath.StartsWith(rootFull, StringComparison.Ordinal))
                        throw new IOException($"Unsafe path detected in zip entry: {entry.FullName}");

                    // .rinneignore 自体は出力しない
                    var relUnix = Path.GetRelativePath(layout.RepoRoot, destPath).Replace(Path.DirectorySeparatorChar, '/');
                    if (string.Equals(relUnix, RinneIgnoreName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    // 無視対象はスキップ（例: **/bin/**, **/obj/**, .vs/** など）
                    if (IsIgnored(relUnix, ignoreRegex, treatDirectory: false))
                        continue;

                    // 上書き前にバックアップ（既存がある場合のみ）
                    if (File.Exists(destPath))
                    {
                        var backupPath = Path.Combine(backupRoot, "before_write", relUnix.Replace('/', Path.DirectorySeparatorChar));
                        Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
                        TryMoveFile(destPath, backupPath); // Move（同一ボリューム前提）
                        backedUpFiles.Add(backupPath);
                    }
                    else
                    {
                        // 新規作成をトラッキング（失敗時に消すため）
                        createdFiles.Add(destPath);
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

                    using var src = entry.Open();
                    using var dst = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 64 * 1024, useAsync: true);
                    await src.CopyToAsync(dst, 64 * 1024, cancellationToken).ConfigureAwait(false);
                }

                // 3) 成功：バックアップを破棄
                TryDeleteDirectory(backupRoot);
            }
            catch
            {
                // 例外：可能な限りロールバック
                try
                {
                    // 展開で新規作成したファイルを削除
                    foreach (var f in createdFiles)
                    {
                        try { if (File.Exists(f)) File.Delete(f); } catch { /* best-effort */ }
                    }

                    // 上書き・削除前に退避したバックアップを元位置へ戻す
                    //   - 退避構造: backupRoot/removed/... および backupRoot/before_write/...
                    //   - 保存時に元の相対パス構造を埋め込んでいるため、そのまま戻せる
                    foreach (var fileInBackup in EnumerateFilesSafe(backupRoot))
                    {
                        var rel = Path.GetRelativePath(backupRoot, fileInBackup)
                                      .Replace(Path.DirectorySeparatorChar, '/');

                        // どのサブルート直下か（removed/ or before_write/）
                        var idx = rel.IndexOf('/');
                        if (idx <= 0) continue;
                        var sub = rel.Substring(idx + 1); // removed/xxx → xxx

                        var original = Path.Combine(layout.RepoRoot, sub.Replace('/', Path.DirectorySeparatorChar));
                        Directory.CreateDirectory(Path.GetDirectoryName(original)!);

                        // 既に何かあるなら消す（復帰を優先）
                        try { if (File.Exists(original)) File.Delete(original); } catch { /* best-effort */ }

                        TryMoveFile(fileInBackup, original);
                    }
                }
                finally
                {
                    // バックアップは最後に掃除（残しても良いが肥大防止）
                    TryDeleteDirectory(backupRoot);
                }

                throw; // 呼び出し側へ再スロー
            }
        }

        /// <summary>
        /// 無視ルールを考慮しながら既存ワーキングツリーをクリーンします。削除対象はバックアップへ移動します。
        /// </summary>
        /// <param name="rootDir">作業ツリールート。</param>
        /// <param name="ignore">無視パターン。</param>
        /// <param name="backupRoot">バックアップ格納ルート。</param>
        /// <param name="backedUpFiles">バックアップへ移動したファイルのリスト。</param>
        /// <param name="ct">キャンセル。</param>
        private static void CleanWorkingTreeTransactional(
            string rootDir,
            IReadOnlyList<Regex> ignore,
            string backupRoot,
            List<string> backedUpFiles,
            CancellationToken ct)
        {
            // ディレクトリ（トップから再帰的に個別判定）
            CleanDirectory(rootDir, rootDir, ignore, backupRoot, backedUpFiles, ct);

            // ルート直下のファイル処理
            foreach (var file in Directory.EnumerateFiles(rootDir))
            {
                ct.ThrowIfCancellationRequested();

                var name = Path.GetFileName(file);
                if (string.Equals(name, RinneIgnoreName, StringComparison.OrdinalIgnoreCase)) continue;
                if (string.Equals(name, RinneDirName, StringComparison.OrdinalIgnoreCase)) continue;

                var relUnix = Path.GetRelativePath(rootDir, file).Replace(Path.DirectorySeparatorChar, '/');
                if (IsIgnored(relUnix, ignore, treatDirectory: false)) continue;

                // 削除の代わりにバックアップへ移動
                var backupPath = Path.Combine(backupRoot, "removed", relUnix.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
                TryMoveFile(file, backupPath);
                backedUpFiles.Add(backupPath);
            }
        }

        /// <summary>
        /// ディレクトリ配下を無視判定しつつクリーン。削除対象はバックアップへ移動。
        /// </summary>
        private static void CleanDirectory(
            string currentDir,
            string rootDir,
            IReadOnlyList<Regex> ignore,
            string backupRoot,
            List<string> backedUpFiles,
            CancellationToken ct)
        {
            foreach (var dir in Directory.EnumerateDirectories(currentDir))
            {
                ct.ThrowIfCancellationRequested();

                var name = Path.GetFileName(dir);
                if (string.Equals(name, RinneDirName, StringComparison.OrdinalIgnoreCase))
                    continue; // .rinne は常に保持

                var relDirUnix = Path.GetRelativePath(rootDir, dir).Replace(Path.DirectorySeparatorChar, '/');
                var relDirForMatch = relDirUnix.EndsWith("/") ? relDirUnix : relDirUnix + "/";

                // 無視ディレクトリは中に入らず丸ごと保持
                if (IsIgnored(relDirForMatch, ignore, treatDirectory: true))
                    continue;

                // 子を処理
                CleanDirectory(dir, rootDir, ignore, backupRoot, backedUpFiles, ct);

                // 子を全て処理後、空なら削除（空にできない・ロック等はベストエフォート）
                try
                {
                    if (IsDirectoryEmpty(dir))
                    {
                        TryClearAttributesRecursively(dir);
                        Directory.Delete(dir, recursive: false);
                    }
                }
                catch { /* best-effort */ }
            }

            // ファイル
            foreach (var file in Directory.EnumerateFiles(currentDir))
            {
                ct.ThrowIfCancellationRequested();

                var name = Path.GetFileName(file);
                if (string.Equals(name, RinneIgnoreName, StringComparison.OrdinalIgnoreCase))
                    continue;

                var relUnix = Path.GetRelativePath(rootDir, file).Replace(Path.DirectorySeparatorChar, '/');
                if (IsIgnored(relUnix, ignore, treatDirectory: false))
                    continue;

                // 削除の代わりにバックアップへ移動
                var backupPath = Path.Combine(backupRoot, "removed", relUnix.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
                TryMoveFile(file, backupPath);
                backedUpFiles.Add(backupPath);
            }
        }

        /// <summary>
        /// ディレクトリ指定（末尾スラッシュ等）を正規化し、名前だけの行は name/** も想定させます。
        /// </summary>
        private static List<string> NormalizePatternsForDirs(IEnumerable<string> patterns)
        {
            var list = new List<string>();
            foreach (var raw in patterns)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                var t = raw.Trim();

                if (t.EndsWith("/", StringComparison.Ordinal) || t.EndsWith("\\", StringComparison.Ordinal))
                {
                    t += "**"; // "dir/" → "dir/**"
                    list.Add(t);
                }
                else if (!t.Contains('*') && !t.Contains('?'))
                {
                    // ".vs" や "temp" のような素の名前は "name" と "name/**" の両方に当たる想定
                    list.Add(t);
                    list.Add(t + "/**");
                }
                else
                {
                    list.Add(t);
                }
            }
            return list;
        }

        /// <summary>
        /// glob 風パターン一覧を正規表現へコンパイルします。
        /// </summary>
        private static List<Regex> CompileGlobs(IEnumerable<string> patterns)
        {
            var result = new List<Regex>();
            foreach (var p in patterns)
            {
                var rx = "^" + Regex.Escape(p)
                    .Replace(@"\*\*", ".*")
                    .Replace(@"\*", @"[^/\\]*")
                    .Replace(@"\?", @"[^/\\]") + "$";
                result.Add(new Regex(rx, RxOpts));
            }
            return result;
        }

        /// <summary>
        /// 無視判定を行います。ディレクトリを評価する場合は末尾 / を想定します。
        /// </summary>
        private static bool IsIgnored(string relativeUnix, IReadOnlyList<Regex> ignore, bool treatDirectory)
        {
            if (ignore.Count == 0) return false;

            // ディレクトリ評価時は末尾 '/' を付けておくと "**/bin/**" 等と自然に一致する
            var target = treatDirectory && !relativeUnix.EndsWith("/")
                ? relativeUnix + "/"
                : relativeUnix;

            foreach (var re in ignore)
                if (re.IsMatch(target))
                    return true;

            return false;
        }

        /// <summary>
        /// ファイルを移動します。移動先親ディレクトリは呼び出し側で作成してください。
        /// </summary>
        private static void TryMoveFile(string src, string dst)
        {
            try
            {
                // 既存があれば消す（復帰優先のため）
                if (File.Exists(dst)) File.Delete(dst);
                // 属性解除（読み取り専用対策）
                try { File.SetAttributes(src, FileAttributes.Normal); } catch { /* best-effort */ }

                File.Move(src, dst);
            }
            catch
            {
                // 移動が失敗した場合はコピー＋元削除を試みる（同一ボリュームでなくても対応）
                try
                {
                    File.Copy(src, dst, overwrite: true);
                    File.Delete(src);
                }
                catch
                {
                    // どうしても動かない場合は最後にスローさせるため、上位の try-catch に任せる
                    throw;
                }
            }
        }

        /// <summary>
        /// ディレクトリ配下のすべてのファイルを安全に列挙します（読み取り例外は無視）。
        /// </summary>
        private static IEnumerable<string> EnumerateFilesSafe(string root)
        {
            var stack = new Stack<string>();
            stack.Push(root);

            while (stack.Count > 0)
            {
                var cur = stack.Pop();
                string[]? files = null, dirs = null;

                try { files = Directory.GetFiles(cur); } catch { /* ignore */ }
                if (files != null)
                    foreach (var f in files) yield return f;

                try { dirs = Directory.GetDirectories(cur); } catch { /* ignore */ }
                if (dirs != null)
                    foreach (var d in dirs) stack.Push(d);
            }
        }

        /// <summary>ディレクトリが空であるかを判定します。</summary>
        private static bool IsDirectoryEmpty(string path)
        {
            try
            {
                using var e = Directory.EnumerateFileSystemEntries(path).GetEnumerator();
                return !e.MoveNext();
            }
            catch { return false; }
        }

        /// <summary>配下の読み取り専用などの属性を解除します（ベストエフォート）。</summary>
        private static void TryClearAttributesRecursively(string dir)
        {
            foreach (var p in Directory.EnumerateFileSystemEntries(dir, "*", SearchOption.AllDirectories))
            { try { File.SetAttributes(p, FileAttributes.Normal); } catch { /* ignore */ } }

            try { File.SetAttributes(dir, FileAttributes.Normal); } catch { /* ignore */ }
        }

        /// <summary>末尾に必ずディレクトリセパレータを持つ絶対パスへ正規化します。</summary>
        private static string EnsureTrailingSep(string absPath)
            => absPath.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? absPath : absPath + Path.DirectorySeparatorChar;

        /// <summary>ディレクトリをベストエフォートで削除します。</summary>
        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, recursive: true);
            }
            catch { /* ignore */ }
        }
    }
}
