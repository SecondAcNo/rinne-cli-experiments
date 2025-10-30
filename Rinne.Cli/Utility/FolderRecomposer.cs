namespace Rinne.Cli.Utilities
{
    /// <summary>
    /// 複数のフォルダツリーを優先度順に合成するユーティリティクラス。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 指定された複数のディレクトリ（<paramref name="sourceRoots"/>）を優先度順にマージし、
    /// <paramref name="destinationRoot"/> に新たなツリーを構築します。
    /// </para>
    /// <para>
    /// 優先度は配列順で決まり、先頭が最も高い優先度（=競合時に勝つ）です。
    /// </para>
    /// <para>
    /// 出力先ディレクトリがすでに存在する場合は安全のため例外を投げます。
    /// </para>
    /// </remarks>
    public static class FolderRecomposer
    {
        /// <summary>
        /// 複数のフォルダを優先度順に合成し、新しいフォルダツリーを生成します。
        /// </summary>
        /// <param name="destinationRoot">合成結果を出力する新しいディレクトリ。</param>
        /// <param name="sourceRoots">合成元ディレクトリの一覧。先頭ほど優先度が高い。</param>
        /// <param name="cancellationToken">キャンセル用トークン。</param>
        /// <returns><see cref="RecomposeResult"/> 統計情報。</returns>
        /// <exception cref="ArgumentException">引数が不正な場合。</exception>
        /// <exception cref="DirectoryNotFoundException">ソースディレクトリが存在しない場合。</exception>
        /// <exception cref="IOException">出力先がすでに存在する場合。</exception>
        public static async Task<RecomposeResult> RecomposeAsync(
            string destinationRoot,
            IEnumerable<string> sourceRoots,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(destinationRoot))
                throw new ArgumentException("Destination path is null or empty.", nameof(destinationRoot));

            var sources = sourceRoots?.ToArray() ?? Array.Empty<string>();
            if (sources.Length == 0)
                throw new ArgumentException("At least one source directory is required.", nameof(sourceRoots));

            destinationRoot = Path.GetFullPath(destinationRoot);

            // 既存ディレクトリが存在する場合はエラー
            if (Directory.Exists(destinationRoot) || File.Exists(destinationRoot))
                throw new IOException($"Destination already exists: {destinationRoot}");

            Directory.CreateDirectory(destinationRoot);

            // ソースの存在チェック
            for (int i = 0; i < sources.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(sources[i]))
                    throw new ArgumentException($"sourceRoots[{i}] is null or empty.", nameof(sourceRoots));

                sources[i] = Path.GetFullPath(sources[i]);
                if (!Directory.Exists(sources[i]))
                    throw new DirectoryNotFoundException($"Source directory not found: {sources[i]}");
            }

            // 優先度順にエントリを選定（先頭優先）
            var chosen = new Dictionary<string, ChosenEntry>(StringComparer.OrdinalIgnoreCase);

            for (int priority = 0; priority < sources.Length; priority++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var root = sources[priority];

                foreach (var dir in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories))
                {
                    var rel = Path.GetRelativePath(root, dir).Replace(Path.DirectorySeparatorChar, '/');
                    if (string.IsNullOrEmpty(rel) || rel == ".") continue;
                    if (!chosen.ContainsKey(rel))
                        chosen[rel] = ChosenEntry.Directory(dir, priority);
                }

                foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                {
                    var rel = Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');
                    if (string.IsNullOrEmpty(rel) || rel == ".") continue;
                    if (!chosen.ContainsKey(rel))
                        chosen[rel] = ChosenEntry.File(file, priority);
                }
            }

            int dirCount = 0;
            int fileCount = 0;

            // ディレクトリ作成
            foreach (var (rel, entry) in chosen.Where(kv => kv.Value.Kind == EntryKind.Directory))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var dstDir = Path.Combine(destinationRoot, rel.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(dstDir);
                dirCount++;
            }

            // ファイルコピー
            foreach (var (rel, entry) in chosen.Where(kv => kv.Value.Kind == EntryKind.File))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var dstPath = Path.Combine(destinationRoot, rel.Replace('/', Path.DirectorySeparatorChar));
                var dstParent = Path.GetDirectoryName(dstPath);
                if (!string.IsNullOrEmpty(dstParent))
                    Directory.CreateDirectory(dstParent);

                File.Copy(entry.AbsolutePath, dstPath, overwrite: false);

                try
                {
                    var at = File.GetLastWriteTimeUtc(entry.AbsolutePath);
                    File.SetLastWriteTimeUtc(dstPath, at);
                }
                catch { /* best-effort */ }

                fileCount++;
            }

            await Task.CompletedTask;

            return new RecomposeResult(
                Sources: sources,
                Destination: destinationRoot,
                CreatedDirectories: dirCount,
                CopiedFiles: fileCount,
                TotalChosenEntries: chosen.Count
            );
        }

        private enum EntryKind { File, Directory }

        private sealed record ChosenEntry(string AbsolutePath, EntryKind Kind, int Priority)
        {
            public static ChosenEntry File(string path, int prio) => new(path, EntryKind.File, prio);
            public static ChosenEntry Directory(string path, int prio) => new(path, EntryKind.Directory, prio);
        }
    }

    /// <summary>
    /// フォルダ合成（Recompose）の結果を表します。
    /// </summary>
    public sealed record RecomposeResult(
        IReadOnlyList<string> Sources,
        string Destination,
        int CreatedDirectories,
        int CopiedFiles,
        int TotalChosenEntries
    );
}
