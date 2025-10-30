namespace Rinne.Cli.Utility
{
    /// <summary>
    /// フォルダツリー間の差分結果を保持するデータモデル。
    /// </summary>
    public sealed class FolderDiffResult
    {
        /// <summary>比較対象2にのみ存在するファイル一覧。</summary>
        public IReadOnlyList<DiffEntry> Added { get; init; } = Array.Empty<DiffEntry>();

        /// <summary>比較対象1にのみ存在するファイル一覧。</summary>
        public IReadOnlyList<DiffEntry> Removed { get; init; } = Array.Empty<DiffEntry>();

        /// <summary>両方に存在し、内容が異なるファイル一覧。</summary>
        public IReadOnlyList<DiffEntry> Modified { get; init; } = Array.Empty<DiffEntry>();

        /// <summary>両方に存在し、内容が完全に一致するファイル一覧。</summary>
        public IReadOnlyList<DiffEntry> Unchanged { get; init; } = Array.Empty<DiffEntry>();
    }

    /// <summary>
    /// 単一ファイルの差分エントリを表します。
    /// </summary>
    public sealed class DiffEntry
    {
        /// <summary>
        /// ルートディレクトリからの相対パス（常にスラッシュ区切り）。
        /// </summary>
        public string RelativePath { get; init; } = "";

        /// <summary>
        /// 比較対象1のファイルサイズ（バイト単位）。
        /// 存在しない場合は null。
        /// </summary>
        public long? Size1 { get; init; }

        /// <summary>
        /// 比較対象2のファイルサイズ（バイト単位）。
        /// 存在しない場合は null。
        /// </summary>
        public long? Size2 { get; init; }

        /// <summary>
        /// 比較対象1のSHA-256ハッシュ（16進文字列）。
        /// 存在しない場合は null。
        /// </summary>
        public string? Hash1 { get; init; }

        /// <summary>
        /// 比較対象2のSHA-256ハッシュ（16進文字列）。
        /// 存在しない場合は null。
        /// </summary>
        public string? Hash2 { get; init; }
    }

    /// <summary>
    /// 2つのディレクトリ配下のファイル群を比較し、差分を抽出するユーティリティクラス。
    /// </summary>
    public static class FolderDiffer
    {
        /// <summary>
        /// 2つのディレクトリを比較し、差分情報を返します。
        /// </summary>
        /// <param name="root1">比較対象1のルートディレクトリ。</param>
        /// <param name="root2">比較対象2のルートディレクトリ。</param>
        /// <param name="computeHash">
        /// ハッシュを用いた厳密比較を行う場合は true（既定値）。
        /// false の場合、サイズ比較のみによる高速判定を行います。
        /// </param>
        /// <returns>フォルダツリー間の差分結果。</returns>
        /// <exception cref="DirectoryNotFoundException">
        /// いずれかのディレクトリが存在しない場合に発生します。
        /// </exception>
        public static FolderDiffResult DiffDirectories(string root1, string root2, bool computeHash = true)
        {
            if (!Directory.Exists(root1))
                throw new DirectoryNotFoundException(root1);
            if (!Directory.Exists(root2))
                throw new DirectoryNotFoundException(root2);

            // 相対パス -> FileMeta
            var map1 = BuildFileMap(root1);
            var map2 = BuildFileMap(root2);

            var allKeys = new SortedSet<string>(map1.Keys, StringComparer.OrdinalIgnoreCase);
            allKeys.UnionWith(map2.Keys);

            var added = new List<DiffEntry>();
            var removed = new List<DiffEntry>();
            var modified = new List<DiffEntry>();
            var unchanged = new List<DiffEntry>();

            foreach (var rel in allKeys)
            {
                var in1 = map1.TryGetValue(rel, out var f1);
                var in2 = map2.TryGetValue(rel, out var f2);

                // 片方のみ存在
                if (in1 && !in2)
                {
                    removed.Add(new DiffEntry
                    {
                        RelativePath = rel,
                        Size1 = f1!.Length,
                        Size2 = null,
                        Hash1 = computeHash ? f1.Hash ??= HashUtility.ComputeSha256(f1.FullPath) : null,
                        Hash2 = null
                    });
                    continue;
                }

                if (!in1 && in2)
                {
                    added.Add(new DiffEntry
                    {
                        RelativePath = rel,
                        Size1 = null,
                        Size2 = f2!.Length,
                        Hash1 = null,
                        Hash2 = computeHash ? f2.Hash ??= HashUtility.ComputeSha256(f2.FullPath) : null
                    });
                    continue;
                }

                // 両方に存在
                var sizeEqual = f1!.Length == f2!.Length;
                string? h1 = null, h2 = null;
                bool equal;

                if (computeHash)
                {
                    h1 = f1.Hash ??= HashUtility.ComputeSha256(f1.FullPath);
                    h2 = f2.Hash ??= HashUtility.ComputeSha256(f2.FullPath);
                    equal = string.Equals(h1, h2, StringComparison.OrdinalIgnoreCase);
                }
                else
                {
                    if (!sizeEqual)
                    {
                        equal = false;
                    }
                    else
                    {
                        // サイズが同一の場合のみハッシュ計算
                        h1 = f1.Hash ??= HashUtility.ComputeSha256(f1.FullPath);
                        h2 = f2.Hash ??= HashUtility.ComputeSha256(f2.FullPath);
                        equal = string.Equals(h1, h2, StringComparison.OrdinalIgnoreCase);
                    }
                }

                var entry = new DiffEntry
                {
                    RelativePath = rel,
                    Size1 = f1.Length,
                    Size2 = f2.Length,
                    Hash1 = h1,
                    Hash2 = h2
                };

                if (equal)
                    unchanged.Add(entry);
                else
                    modified.Add(entry);
            }

            return new FolderDiffResult
            {
                Added = added,
                Removed = removed,
                Modified = modified,
                Unchanged = unchanged
            };
        }

        /// <summary>
        /// ファイルメタ情報（パス・サイズ・ハッシュ）を保持する内部クラス。
        /// </summary>
        private sealed class FileMeta
        {
            /// <summary>絶対パス。</summary>
            public string FullPath { get; init; } = "";

            /// <summary>ファイルサイズ（バイト単位）。</summary>
            public long Length { get; init; }

            /// <summary>計算済みハッシュキャッシュ。</summary>
            public string? Hash { get; set; }
        }

        /// <summary>
        /// 指定ルート配下のすべてのファイルを列挙し、相対パスとメタ情報の辞書を構築します。
        /// </summary>
        /// <param name="root">ルートディレクトリ。</param>
        /// <returns>相対パス → メタ情報の辞書。</returns>
        private static Dictionary<string, FileMeta> BuildFileMap(string root)
        {
            var dict = new Dictionary<string, FileMeta>(StringComparer.OrdinalIgnoreCase);

            foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                var rel = ToRelativeUnixPath(root, path);
                var fi = new FileInfo(path);
                dict[rel] = new FileMeta
                {
                    FullPath = path,
                    Length = fi.Length
                };
            }

            return dict;
        }

        /// <summary>
        /// 相対パスをスラッシュ区切りの形式に正規化します。
        /// </summary>
        /// <param name="root">ルートディレクトリ。</param>
        /// <param name="fullPath">対象ファイルの絶対パス。</param>
        /// <returns>ルートからの相対パス（スラッシュ区切り）。</returns>
        private static string ToRelativeUnixPath(string root, string fullPath)
        {
            var rel = Path.GetRelativePath(root, fullPath);
            return rel.Replace('\\', '/');
        }
    }
}
