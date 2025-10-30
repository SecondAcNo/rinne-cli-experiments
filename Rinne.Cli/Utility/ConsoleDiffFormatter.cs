using Rinne.Cli.Interfaces.Services;
using Rinne.Cli.Interfaces.Utility;

namespace Rinne.Cli.Utility
{
    /// <summary>
    /// 差分結果をコンソールに色付きで表示する既定実装。
    /// </summary>
    public sealed class ConsoleDiffFormatter : IConsoleDiffFormatter
    {
        /// <inheritdoc/>
        public void Print(ArchiveDiffOutcome o)
        {
            var r = o.Result;
            Console.WriteLine();
            Console.WriteLine($"[diff] space={o.Space}");
            Console.WriteLine($"  {Path.GetFileName(o.ZipPath1)}  vs  {Path.GetFileName(o.ZipPath2)}");
            Console.WriteLine($"  Added   : {r.Added.Count}");
            Console.WriteLine($"  Removed : {r.Removed.Count}");
            Console.WriteLine($"  Modified: {r.Modified.Count}");
            Console.WriteLine($"  Same    : {r.Unchanged.Count}");
            Console.WriteLine();

            PrintSection("Added", r.Added, '+', ConsoleColor.Green);
            PrintSection("Removed", r.Removed, '-', ConsoleColor.Red);
            PrintSection("Modified", r.Modified, '~', ConsoleColor.Yellow);
        }

        private static void PrintSection(string title, IReadOnlyList<DiffEntry> entries, char mark, ConsoleColor color)
        {
            if (entries.Count == 0) return;

            Console.WriteLine(title + ":");
            Console.ForegroundColor = color;

            foreach (var e in entries)
            {
                string meta = title switch
                {
                    "Added" => $"size={FormatBytes(e.Size2)}, hash={e.Hash2}",
                    "Removed" => $"size={FormatBytes(e.Size1)}, hash={e.Hash1}",
                    "Modified" => $"size {FormatBytes(e.Size1)} -> {FormatBytes(e.Size2)}, hash {Trunc(e.Hash1)} -> {Trunc(e.Hash2)}",
                    _ => ""
                };
                Console.WriteLine($"  {mark} {e.RelativePath}  ({meta})");
            }

            Console.ResetColor();
            Console.WriteLine();
        }

        private static string FormatBytes(long? bytes)
        {
            if (bytes is null) return "n/a";
            double b = bytes.Value;
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            int u = 0;
            while (b >= 1024 && u < units.Length - 1) { b /= 1024; u++; }
            return $"{b:0.##} {units[u]}";
        }

        private static string Trunc(string? hash, int n = 8)
            => string.IsNullOrEmpty(hash) ? "n/a" : (hash.Length <= n ? hash : hash[..n]);
    }
}
