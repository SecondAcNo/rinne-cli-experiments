namespace Rinne.Cli.System
{
    /// <summary>
    /// 一時ファイルに書き切ってから最終パスへ入れ替えるアトミック書き込みユーティリティ。
    /// 任意のストリーム/ファイル生成に利用可能。
    /// </summary>
    public sealed class AtomicFileWriter
    {
        /// <summary>
        /// 一時ファイルのストリームに書き込む方式で原子的にファイルを生成します。
        /// </summary>
        public async Task<string> WriteStreamAsync(
            string finalPath,
            bool overwrite,
            Func<Stream, CancellationToken, Task> writeToStream,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(finalPath))
                throw new ArgumentException("finalPath is null or empty.", nameof(finalPath));

            var fullFinal = Path.GetFullPath(finalPath);
            var dir = Path.GetDirectoryName(fullFinal) ?? throw new ArgumentException("Invalid path.", nameof(finalPath));
            Directory.CreateDirectory(dir);

            if (File.Exists(fullFinal) && !overwrite)
                throw new IOException($"Output already exists: {fullFinal}");

            var tempPath = Path.Combine(dir, Path.GetFileName(fullFinal) + ".tmp");
            TryDeleteQuiet(tempPath);

            try
            {
                await using (var fs = new FileStream(tempPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 1 << 16, useAsync: true))
                {
                    await writeToStream(fs, cancellationToken).ConfigureAwait(false);
                }

                FinalizeTemp(tempPath, fullFinal, overwrite);
                return fullFinal;
            }
            catch
            {
                TryDeleteQuiet(tempPath);
                throw;
            }
        }

        /// <summary>
        /// 一時ファイルのパスを渡す方式で原子的にファイルを生成します。
        /// 外部プロセスや一時ディレクトリを使う場合に便利です。
        /// </summary>
        public async Task<string> WriteAsync(
            string finalPath,
            bool overwrite,
            Func<string, CancellationToken, Task> writeToTempPath,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(finalPath))
                throw new ArgumentException("finalPath is null or empty.", nameof(finalPath));

            var fullFinal = Path.GetFullPath(finalPath);
            var dir = Path.GetDirectoryName(fullFinal) ?? throw new ArgumentException("Invalid path.", nameof(finalPath));
            Directory.CreateDirectory(dir);

            if (File.Exists(fullFinal) && !overwrite)
                throw new IOException($"Output already exists: {fullFinal}");

            var tempPath = Path.Combine(dir, Path.GetFileName(fullFinal) + ".tmp");
            TryDeleteQuiet(tempPath);

            try
            {
                await writeToTempPath(tempPath, cancellationToken).ConfigureAwait(false);
                FinalizeTemp(tempPath, fullFinal, overwrite);
                return fullFinal;
            }
            catch
            {
                TryDeleteQuiet(tempPath);
                throw;
            }
        }

        private static void FinalizeTemp(string tempPath, string finalPath, bool overwrite)
        {
            if (File.Exists(finalPath))
            {
                // 同一ボリュームでアトミックに置換。ACL 等を維持しやすい。
                File.Replace(tempPath, finalPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(tempPath, finalPath);
            }
        }

        private static void TryDeleteQuiet(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { /* ignore */ }
        }
    }
}
