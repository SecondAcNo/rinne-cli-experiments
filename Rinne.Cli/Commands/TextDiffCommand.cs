using Rinne.Cli.Commands.Interfaces;
using Rinne.Core.Features.TextDiff;
using Rinne.Core.Common;
using Rinne.Core.Features.Space;

namespace Rinne.Cli.Commands;

public sealed class TextDiffCommand : ICliCommand
{
    public string Name => "textdiff";
    public IEnumerable<string> Aliases => new[] { "tdiff", "td" };
    public string Summary => "Show unified diffs for text files between two snapshots. Uses payload; temp-hydrate if needed. (@N and id-prefix resolved by command)";

    public string Usage => """
        Usage:
          rinne textdiff [<space>] <A> <B>

        Snapshot selectors:
          <A>, <B> can be:
            - full id
            - id prefix (unique)
            - @N (N back; @1 = previous)

        Notes:
          - If <space> is omitted, it defaults to the current space (see `rinne space current`).
          - DEPRECATED: this command will be removed in a future version.
          - This command accepts no options. Only positional args are allowed.
        """;

    public async Task<int> RunAsync(string[] args, CancellationToken ct)
    {
        Console.Error.WriteLine("warning: 'rinne textdiff' is DEPRECATED and will be removed in the next version.");

        string? spaceArg = null;
        string? selA = null, selB = null;

        for (int i = 0; i < args.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            if (IsOption(args[i]))
            {
                Console.Error.WriteLine($"unknown option: {args[i]}");
                Console.WriteLine("Use: " + Usage);
                return 2;
            }
        }

        var positionals = args.Select(TrimQuotes).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();

        if (positionals.Count == 3)
        {
            spaceArg = positionals[0];
            selA = positionals[1];
            selB = positionals[2];
        }
        else if (positionals.Count == 2)
        {
            selA = positionals[0];
            selB = positionals[1];
        }
        else
        {
            Console.Error.WriteLine("missing or too many arguments.");
            Console.WriteLine("Use: " + Usage);
            return 2;
        }

        if (string.IsNullOrEmpty(selA) || string.IsNullOrEmpty(selB))
        {
            Console.Error.WriteLine("missing <A> and <B>.");
            Console.WriteLine("Use: " + Usage);
            return 2;
        }

        try
        {
            var paths = new RinnePaths(Environment.CurrentDirectory);

            var spaceSvc = new SpaceService(paths);
            var space = spaceArg ?? spaceSvc.GetCurrentSpaceFromPointer();

            if (!IsValidSpaceName(space))
            {
                Console.Error.WriteLine($"invalid space name. Use {SpaceNameRules.HumanReadable}");
                return 2;
            }

            var idA = ResolveSelectorToId(paths, space, selA, ct);
            var idB = ResolveSelectorToId(paths, space, selB, ct);

            var svc = new TextDiffService(paths);
            var opt = new TextDiffService.Options(
                IncludeGlobs: null,
                ExcludeGlobs: null,
                MaxBytesPerFile: 2L * 1024 * 1024,
                TempHydrateWhenNeeded: true,
                HydrateWorkers: 0,
                HydrateMaxTotalBytes: long.MaxValue,
                ContextLines: 3,
                IgnoreTrim: false,
                NormalizeNewlines: true
            );

            var result = await svc.DiffTextAsync(space, idA, idB, opt, ct);

            PrintHeader(result, space);

            if (result.Diffs.Count == 0)
            {
                Console.WriteLine("No text changes.");
                Console.WriteLine("Hint: target is only text under <id>/snapshots (note.md/binary are excluded).");
                return 0;
            }

            foreach (var d in result.Diffs)
            {
                var tag = d.Status switch
                {
                    TextDiffService.FileStatus.Added => "+",
                    TextDiffService.FileStatus.Removed => "-",
                    TextDiffService.FileStatus.Modified => "M",
                    TextDiffService.FileStatus.Renamed => "R",
                    _ => "?"
                };

                var name = d.Status == TextDiffService.FileStatus.Added ? d.PathB
                         : d.Status == TextDiffService.FileStatus.Removed ? d.PathA
                         : $"{d.PathA} -> {d.PathB}";

                Console.WriteLine($"{tag} {name}");

                if (d.IsBinary)
                {
                    Console.WriteLine("(binary file skipped)");
                    continue;
                }

                if (!string.IsNullOrEmpty(d.UnifiedDiffText))
                    PrintCaretStyle(d.UnifiedDiffText);
            }

            Console.WriteLine($"Files: {result.Diffs.Count}");
            return 0;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"textdiff failed: {ex.Message}");
            return 3;
        }
    }

    private static void PrintHeader(TextDiffService.Result r, string space)
    {
        Console.WriteLine($"TextDiff [{space}]: {r.IdA} .. {r.IdB}");
    }

    private static bool IsOption(string s) => s.StartsWith("-", StringComparison.Ordinal);

    private static string TrimQuotes(string? s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        if ((s.Length >= 2) &&
            ((s[0] == '"' && s[^1] == '"') || (s[0] == '\'' && s[^1] == '\'')))
            return s.Substring(1, s.Length - 2);
        return s;
    }

    private static bool IsValidSpaceName(string name) => SpaceNameRules.NameRegex.IsMatch(name);

    private static string ResolveSelectorToId(RinnePaths paths, string space, string selector, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var spaceDir = paths.SnapshotsSpace(space);
        if (!Directory.Exists(spaceDir))
            throw new DirectoryNotFoundException($"space not found: {space}");

        if (selector.Length >= 2 && selector[0] == '@')
        {
            if (!int.TryParse(selector.AsSpan(1), out var n) || n < 0)
                throw new ArgumentException($"invalid @N selector: {selector}");

            var ids = Directory.EnumerateDirectories(spaceDir)
                               .Select(Path.GetFileName)
                               .Where(id => !string.IsNullOrWhiteSpace(id)
                                            && id!.Length >= 8
                                            && !id.StartsWith("."))
                               .OrderByDescending(s => s, StringComparer.Ordinal)
                               .ToList();

            if (ids.Count == 0)
                throw new InvalidOperationException("no snapshots in space.");

            var index = 1 + n;
            if (index < 0 || index >= ids.Count)
                throw new ArgumentOutOfRangeException(nameof(selector), $"@{n} out of range.");

            return ids[index]!;
        }

        var matches = Directory.EnumerateDirectories(spaceDir)
                               .Select(Path.GetFileName)
                               .Where(id => !string.IsNullOrWhiteSpace(id)
                                            && id!.StartsWith(selector, StringComparison.Ordinal))
                               .ToList();

        if (matches.Count == 1)
            return matches[0]!;
        if (matches.Count > 1)
            throw new InvalidOperationException($"ambiguous id prefix '{selector}': {string.Join(", ", matches.Take(5))}{(matches.Count > 5 ? ", ..." : "")}");
        throw new FileNotFoundException($"snapshot not found for selector: {selector}");
    }

    private static void PrintCaretStyle(string unified)
    {
        var lines = unified.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
        int aLine = 0, bLine = 0;
        var minus = new List<(int num, string text)>();
        var plus = new List<(int num, string text)>();

        foreach (var raw in lines)
        {
            var line = raw;
            if (line.StartsWith("@@ "))
            {
                Flush(minus, plus);
                ParseHunkHeader(line, out aLine, out bLine);
                minus.Clear(); plus.Clear();
                continue;
            }
            if (line.StartsWith("--- ") || line.StartsWith("+++ "))
                continue;

            if (line.Length == 0)
                continue;

            var kind = line[0];
            var payload = line.Length > 1 ? line.Substring(1) : string.Empty;

            if (kind == ' ')
            {
                Flush(minus, plus);
                aLine++; bLine++;
            }
            else if (kind == '-')
            {
                minus.Add((aLine + 1, payload));
                aLine++;
            }
            else if (kind == '+')
            {
                plus.Add((bLine + 1, payload));
                bLine++;
            }
        }

        Flush(minus, plus);

        static void ParseHunkHeader(string s, out int aStart, out int bStart)
        {
            aStart = 0; bStart = 0;
            var i1 = s.IndexOf('-'); var i2 = s.IndexOf(' ', i1 + 1);
            var j1 = s.IndexOf('+', i2 + 1); var j2 = s.IndexOf(' ', j1 + 1);
            var aTok = s.Substring(i1 + 1, i2 - (i1 + 1));
            var bTok = s.Substring(j1 + 1, j2 - (j1 + 1));
            var aComma = aTok.IndexOf(',');
            var bComma = bTok.IndexOf(',');
            aStart = int.Parse(aComma >= 0 ? aTok.Substring(0, aComma) : aTok);
            bStart = int.Parse(bComma >= 0 ? bTok.Substring(0, bComma) : bTok);
            aStart--; bStart--;
        }

        static void Flush(List<(int num, string text)> minus, List<(int num, string text)> plus)
        {
            if (minus.Count == 0 && plus.Count == 0) return;
            var n = Math.Max(minus.Count, plus.Count);
            for (int i = 0; i < n; i++)
            {
                var hasOld = i < minus.Count;
                var hasNew = i < plus.Count;
                var oldNum = hasOld ? minus[i].num : (hasNew ? plus[i].num : 0);
                var newNum = hasNew ? plus[i].num : (hasOld ? minus[i].num : 0);
                var oldText = hasOld ? minus[i].text : string.Empty;
                var newText = hasNew ? plus[i].text : string.Empty;

                Console.WriteLine($"L{oldNum}-");
                Console.WriteLine(oldText);
                Console.WriteLine($"L{newNum}+");
                Console.WriteLine(newText);

                var target = newText.Length > 0 ? newText : oldText;
                var pref = CommonPrefixLen(oldText, newText);
                var suff = CommonSuffixLen(oldText, newText, pref);
                var len = Math.Max(1, target.Length - pref - suff);
                Console.WriteLine(new string(' ', pref) + new string('^', len));
            }
            minus.Clear(); plus.Clear();
        }

        static int CommonPrefixLen(string a, string b)
        {
            var n = Math.Min(a.Length, b.Length);
            int i = 0;
            for (; i < n; i++) if (a[i] != b[i]) break;
            return i;
        }

        static int CommonSuffixLen(string a, string b, int skipPrefix)
        {
            int ai = a.Length - 1, bi = b.Length - 1, c = 0, minStop = skipPrefix;
            while (ai >= minStop && bi >= minStop && a[ai] == b[bi]) { c++; ai--; bi--; }
            return c;
        }
    }
}
