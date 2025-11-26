using Rinne.Core.Config;

namespace Rinne.Core.Features.Snapshots
{
    internal static class CopyPlanner
    {
        internal sealed record CopyItem(string FullPath, string RelativePath, long Length);
        internal sealed record CopyPlan(IReadOnlyList<string> Dirs, IReadOnlyList<CopyItem> Files);

        public static CopyPlan PlanAll(string sourceRoot, Ignorer ignorer)
        {
            if (string.IsNullOrWhiteSpace(sourceRoot))
                throw new ArgumentException("sourceRoot is required.", nameof(sourceRoot));
            if (!Directory.Exists(sourceRoot))
                throw new DirectoryNotFoundException(sourceRoot);

            var root = sourceRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var rootLen = root.Length;
            var dirs = new List<string>(256);
            var files = new List<CopyItem>(4096);

            var stack = new Stack<string>();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            stack.Push(root);

            while (stack.Count > 0)
            {
                var dir = stack.Pop();
                string fullDir;
                try
                {
                    fullDir = Path.GetFullPath(dir);
                }
                catch
                {
                    fullDir = dir;
                }

                if (!visited.Add(fullDir))
                    continue;

                foreach (var sub in SafeEnumDirs(dir))
                {
                    if (IsReparsePoint(sub))
                        continue;

                    if (string.Equals(Path.GetFileName(sub), ".rinne", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var rel = ToRel(sub, rootLen);
                    if (ignorer.IsDirExcluded(rel)) continue;

                    dirs.Add(rel);
                    stack.Push(sub);
                }

                foreach (var file in SafeEnumFiles(dir))
                {
                    var rel = ToRel(file, rootLen);
                    if (ignorer.IsFileExcluded(rel)) continue;

                    long len = 0;
                    try { len = new FileInfo(file).Length; } catch { }
                    files.Add(new CopyItem(file, rel, len));
                }
            }

            dirs.Sort(StringComparer.Ordinal);
            files.Sort(static (a, b) => StringComparer.Ordinal.Compare(a.RelativePath, b.RelativePath));

            return new CopyPlan(dirs, files);

            static IEnumerable<string> SafeEnumDirs(string d)
            {
                try { return Directory.EnumerateDirectories(d); }
                catch { return Array.Empty<string>(); }
            }

            static IEnumerable<string> SafeEnumFiles(string d)
            {
                try { return Directory.EnumerateFiles(d); }
                catch { return Array.Empty<string>(); }
            }

            static string ToRel(string path, int rootLength)
            {
                var rel = path.Substring(rootLength)
                              .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                              .Replace(Path.DirectorySeparatorChar, '/')
                              .Replace(Path.AltDirectorySeparatorChar, '/');
                return rel.Length == 0 ? "." : rel;
            }

            static bool IsReparsePoint(string path)
            {
                try
                {
                    var attr = File.GetAttributes(path);
                    return (attr & FileAttributes.ReparsePoint) != 0;
                }
                catch
                {
                    return false;
                }
            }
        }

        public static IReadOnlyList<CopyItem> Plan(string sourceRoot, Ignorer ignorer)
            => PlanAll(sourceRoot, ignorer).Files;

        public static long SumBytes(IEnumerable<CopyItem> items) => items.Sum(i => i.Length);
    }
}
