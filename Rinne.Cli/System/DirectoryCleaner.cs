using Rinne.Cli.Interfaces.System;

namespace Rinne.Cli.System
{
    /// <summary>
    /// Windows/Unix を問わず動作する簡易クリーナーの既定実装。
    /// </summary>
    public sealed class DirectoryCleaner : IDirectoryCleaner
    {
        /// <inheritdoc/>
        public void Empty(string dir)
        {
            if (!Directory.Exists(dir)) return;

            // 先にファイルを削除（ReadOnlyも可能なら外す）
            foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                try
                {
                    var attr = File.GetAttributes(f);
                    if ((attr & FileAttributes.ReadOnly) != 0)
                        File.SetAttributes(f, attr & ~FileAttributes.ReadOnly);
                    File.Delete(f);
                }
                catch { /* ignore */ }
            }

            // 子ディレクトリを深い順に削除
            foreach (var d in Directory.EnumerateDirectories(dir, "*", SearchOption.AllDirectories)
                                       .OrderByDescending(p => p.Length))
            {
                try { Directory.Delete(d, recursive: false); } catch { /* ignore */ }
            }
        }
    }
}
