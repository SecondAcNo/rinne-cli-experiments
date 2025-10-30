using DiffPlex;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
using System.Text;

namespace Rinne.Cli.Utility
{
    /// <summary>
    /// フォルダツリー内の「テキストファイルのみ」を対象に、DiffPlex で差分を計算するユーティリティ。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 指定した 2 つのルートディレクトリ配下の全ファイルを相対パスで突き合わせ、
    /// テキストと推定されるファイルのみ、行単位のインライン差分を算出します。
    /// </para>
    /// <para>
    /// バイナリと推定されたファイルは <see cref="FileChangeKind.SkippedNonText"/> としてスキップします。
    /// テキスト判定は NULL バイトおよび制御文字比率に基づく簡易判定であり、100%の正確性は保証されません。
    /// </para>
    /// </remarks>
    public static class FolderTextDiffer
    {
        /// <summary>
        /// フォルダツリー内のテキストファイルのみを対象に差分を算出します（非同期）。
        /// </summary>
        /// <param name="leftRoot">比較対象1のルートディレクトリ。</param>
        /// <param name="rightRoot">比較対象2のルートディレクトリ。</param>
        /// <param name="cancellationToken">キャンセル要求を受け取るトークン。</param>
        /// <returns>フォルダ全体の差分結果</returns>
        public static async Task<FolderTextDiffResult> CompareAsync(
            string leftRoot,
            string rightRoot,
            CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(leftRoot))
                throw new DirectoryNotFoundException($"Left root not found: {leftRoot}");
            if (!Directory.Exists(rightRoot))
                throw new DirectoryNotFoundException($"Right root not found: {rightRoot}");

            // 左右のツリーを相対パスで列挙（キー：相対パス、値：絶対パス）
            var leftMap = EnumerateAllFiles(leftRoot);
            var rightMap = EnumerateAllFiles(rightRoot);

            // 結合キー集合（相対パス）
            var allKeys = new HashSet<string>(leftMap.Keys, StringComparer.OrdinalIgnoreCase);
            allKeys.UnionWith(rightMap.Keys);

            // DiffPlex 準備（インライン差分）
            var differ = new Differ();
            var inlineBuilder = new InlineDiffBuilder(differ);

            var results = new List<FileTextDiffResult>(capacity: allKeys.Count);

            foreach (var rel in allKeys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var hasLeft = leftMap.TryGetValue(rel, out var leftPath);
                var hasRight = rightMap.TryGetValue(rel, out var rightPath);

                // 片側のみ存在（追加 or 削除）
                if (hasLeft && !hasRight)
                {
                    if (!IsProbablyText(leftPath!))
                    {
                        results.Add(new FileTextDiffResult(rel, FileChangeKind.SkippedNonText, Array.Empty<LineTextDiff>()));
                        continue;
                    }

                    var leftText = await ReadAllTextAsync(leftPath!, cancellationToken);
                    var lines = leftText.Length == 0
                        ? Array.Empty<LineTextDiff>()
                        : ToLineTextDiffs(leftText, ChangeType.Deleted);
                    results.Add(new FileTextDiffResult(rel, FileChangeKind.Removed, lines));
                    continue;
                }
                if (!hasLeft && hasRight)
                {
                    if (!IsProbablyText(rightPath!))
                    {
                        results.Add(new FileTextDiffResult(rel, FileChangeKind.SkippedNonText, Array.Empty<LineTextDiff>()));
                        continue;
                    }

                    var rightText = await ReadAllTextAsync(rightPath!, cancellationToken);
                    var lines = rightText.Length == 0
                        ? Array.Empty<LineTextDiff>()
                        : ToLineTextDiffs(rightText, ChangeType.Inserted);
                    results.Add(new FileTextDiffResult(rel, FileChangeKind.Added, lines));
                    continue;
                }

                // 両側に存在
                var leftIsText = IsProbablyText(leftPath!);
                var rightIsText = IsProbablyText(rightPath!);
                if (!(leftIsText && rightIsText))
                {
                    results.Add(new FileTextDiffResult(rel, FileChangeKind.SkippedNonText, Array.Empty<LineTextDiff>()));
                    continue;
                }

                var leftContent = await ReadAllTextAsync(leftPath!, cancellationToken);
                var rightContent = await ReadAllTextAsync(rightPath!, cancellationToken);

                if (string.Equals(leftContent, rightContent, StringComparison.Ordinal))
                {
                    results.Add(new FileTextDiffResult(rel, FileChangeKind.Unchanged, Array.Empty<LineTextDiff>()));
                    continue;
                }

                // DiffPlex によるインライン差分
                var inline = inlineBuilder.BuildDiffModel(leftContent, rightContent);

                // InlineDiffModel を軽量な行単位モデルに変換
                var lineDiffs = inline.Lines.Select(l => new LineTextDiff(
                        Text: l.Text ?? string.Empty,
                        Kind: l.Type switch
                        {
                            ChangeType.Unchanged => LineChangeKind.Unchanged,
                            ChangeType.Deleted => LineChangeKind.Deleted,
                            ChangeType.Inserted => LineChangeKind.Inserted,
                            ChangeType.Modified => LineChangeKind.Modified,
                            _ => LineChangeKind.Unchanged
                        }))
                    .ToArray();

                results.Add(new FileTextDiffResult(rel, FileChangeKind.Modified, lineDiffs));
            }

            return new FolderTextDiffResult(
                LeftRoot: Path.GetFullPath(leftRoot),
                RightRoot: Path.GetFullPath(rightRoot),
                Files: results);
        }

        /// <summary>
        /// 指定ルート配下の全ファイルを列挙し、相対パス→絶対パスの辞書を返します。
        /// </summary>
        /// <param name="root">列挙するルートディレクトリ。</param>
        /// <returns>相対パスをキー、絶対パスを値とする辞書。</returns>
        private static Dictionary<string, string> EnumerateAllFiles(string root)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var rootFull = Path.GetFullPath(root);

            foreach (var abs in Directory.EnumerateFiles(rootFull, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(rootFull, abs).Replace('\\', '/');
                map[rel] = abs;
            }
            return map;
        }

        /// <summary>
        /// 簡易テキスト判定：先頭数 KB を検査し、NULL バイトの存在や制御文字比率からバイナリ/テキストを推定します。
        /// </summary>
        /// <param name="path">判定対象のファイルパス。</param>
        /// <param name="probeBytes">検査する最大バイト数（既定 4096）。</param>
        /// <returns>テキストと推定できる場合は true。</returns>
        private static bool IsProbablyText(string path, int probeBytes = 4096)
        {
            try
            {
                using var fs = File.OpenRead(path);
                var len = (int)Math.Min(probeBytes, fs.Length);
                if (len == 0) return true; // 空ファイルはテキスト扱い

                Span<byte> buf = stackalloc byte[Math.Max(16, len)];
                var read = fs.Read(buf);
                if (read <= 0) return true;

                var span = buf.Slice(0, read);

                // NULL バイトが含まれていればバイナリとみなす
                if (span.IndexOf((byte)0) >= 0) return false;

                // 制御文字（\t, \n, \r を除く）比率でざっくり判定（2% 以上でバイナリ傾向）
                int ctrl = 0;
                for (int i = 0; i < span.Length; i++)
                {
                    byte b = span[i];
                    if (b < 0x20 && b != 0x09 && b != 0x0A && b != 0x0D)
                        ctrl++;
                }
                return (ctrl / (double)span.Length) < 0.02;
            }
            catch
            {
                // 読み取り不能な場合は保守的に非テキスト扱い
                return false;
            }
        }

        /// <summary>
        /// BOM 自動検出を有効にしつつ UTF-8 既定でテキスト全体を読み込みます。
        /// </summary>
        /// <param name="path">読み込みパス。</param>
        /// <param name="ct">キャンセルトークン。</param>
        /// <returns>ファイル内容の文字列。</returns>
        private static async Task<string> ReadAllTextAsync(string path, CancellationToken ct)
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var sr = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var sb = new StringBuilder((int)Math.Min(fs.Length, 1_000_000));
            char[] buf = new char[8192];
            int read;
            while ((read = await sr.ReadAsync(buf.AsMemory(0, buf.Length), ct)) > 0)
            {
                sb.Append(buf, 0, read);
            }
            return sb.ToString();
        }

        /// <summary>
        /// ヘルパー：テキスト全体を行単位の <see cref="LineTextDiff"/> 配列へ変換します。
        /// </summary>
        /// <param name="text">変換対象のテキスト。</param>
        /// <param name="kind">差分種別（挿入/削除など）。</param>
        /// <returns>行ごとの差分配列。</returns>
        private static LineTextDiff[] ToLineTextDiffs(string text, ChangeType kind)
        {
            // 改行正規化（CRLF → LF）
            var split = text.Replace("\r\n", "\n").Split('\n');

            var lineKind = kind switch
            {
                ChangeType.Inserted => LineChangeKind.Inserted,
                ChangeType.Deleted => LineChangeKind.Deleted,
                _ => LineChangeKind.Modified
            };

            // 末尾が空行（末尾改行）の場合は見やすさのため除外
            var seq = (split.Length > 0 && split[^1].Length == 0)
                ? split.Take(split.Length - 1)
                : split.AsEnumerable();

            return seq.Select(s => new LineTextDiff(s, lineKind)).ToArray();
        }
    }

    /// <summary>
    /// フォルダ全体の差分結果を表します（テキストファイル対象）。
    /// </summary>
    /// <param name="LeftRoot">比較対象1の絶対パス。</param>
    /// <param name="RightRoot">比較対象2の絶対パス。</param>
    /// <param name="Files">ファイルごとの差分結果。</param>
    public sealed record FolderTextDiffResult(
        string LeftRoot,
        string RightRoot,
        IReadOnlyList<FileTextDiffResult> Files);

    /// <summary>
    /// 1 ファイルの差分結果を表します（テキストファイル対象）。
    /// </summary>
    /// <param name="RelativePath">ルートからの相対パス（区切りは '/'）。</param>
    /// <param name="Change">ファイルの変更種別。</param>
    /// <param name="Lines">行単位の差分情報（変更がない場合は空）。</param>
    public sealed record FileTextDiffResult(
        string RelativePath,
        FileChangeKind Change,
        IReadOnlyList<LineTextDiff> Lines);

    /// <summary>
    /// 1 行の差分情報（インライン差分の行種別を集約、テキスト）。
    /// </summary>
    /// <param name="Text">行テキスト。</param>
    /// <param name="Kind">行の変更種別。</param>
    public sealed record LineTextDiff(
        string Text,
        LineChangeKind Kind);

    /// <summary>
    /// ファイルの変更種別。
    /// </summary>
    public enum FileChangeKind
    {
        /// <summary>両者同一。</summary>
        Unchanged = 0,
        /// <summary>内容が変更された。</summary>
        Modified,
        /// <summary>右側にのみ存在（追加）。</summary>
        Added,
        /// <summary>左側にのみ存在（削除）。</summary>
        Removed,
        /// <summary>非テキストと推定されたため比較をスキップ。</summary>
        SkippedNonText,
    }

    /// <summary>
    /// 行の変更種別。
    /// </summary>
    public enum LineChangeKind
    {
        /// <summary>変更なし。</summary>
        Unchanged = 0,
        /// <summary>追加行。</summary>
        Inserted,
        /// <summary>削除行。</summary>
        Deleted,
        /// <summary>変更行（インライン差分で Modified 扱い）。</summary>
        Modified
    }
}
