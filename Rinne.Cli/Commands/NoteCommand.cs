using Rinne.Cli.Commands.Interfaces;
using Rinne.Core.Common;
using Rinne.Core.Features.Notes;
using Rinne.Core.Features.Space;
using System.Text;

namespace Rinne.Cli.Commands;

public sealed class NoteCommand : ICliCommand
{
    public string Name => "note";
    public IEnumerable<string> Aliases => Array.Empty<string>();
    public string Summary => "List notes, or view/append/overwrite/clear a snapshot note.";
    public string Usage => $"""
        Usage:
          rinne note [<space>]
              List snapshots that have {NoteService.DefaultFileName}.

          rinne note [<space>] <id|@N> --view
              Print {NoteService.DefaultFileName} to stdout.

          rinne note [<space>] <id|@N> --append <text>
              Append <text> (non-interactive). Use shell here-strings for multi-line.

          rinne note [<space>] <id|@N> --overwrite <text>
              Overwrite the entire note with <text>. 

          rinne note [<space>] <id|@N> --clear
              Clear all content (keep the file).

        Notes:
          - If <space> is omitted, the CURRENT space pointer is used.
          - <id> can be a full id or a unique prefix, or @N (N-th from newest; @0 is latest).
          - PowerShell: quote @N. Example →  rinne note '@0' --view
          - PowerShell multi-line:
              rinne note '@0' --append @"
              line1
              line2
              "@
        """;

    public async Task<int> RunAsync(string[] args, CancellationToken ct)
    {
        string? explicitSpace = null;
        string? id = null;
        bool viewMode = false;
        string? appendText = null;
        string? overwriteText = null;
        bool clearMode = false;

        var positionals = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            var a = args[i];

            if (a == "--view")
            {
                viewMode = true;
            }
            else if (a == "--append")
            {
                if (i + 1 >= args.Length) { Console.Error.WriteLine("--append requires <text>."); return 2; }
                appendText = args[++i];
            }
            else if (a == "--overwrite")
            {
                if (i + 1 >= args.Length) { Console.Error.WriteLine("--overwrite requires <text>."); return 2; }
                overwriteText = args[++i];
            }
            else if (a == "--clear")
            {
                clearMode = true;
            }
            else if (a.StartsWith('-'))
            {
                Console.Error.WriteLine($"unknown option: {a}");
                return 2;
            }
            else
            {
                positionals.Add(a);
            }
        }

        bool spaceExplicitlyGiven = false;
        if (positionals.Count >= 1)
        {
            if (LooksLikeId(positionals[0]))
            {
                id = positionals[0];
            }
            else
            {
                explicitSpace = positionals[0];
                if (!SpaceNameRules.NameRegex.IsMatch(explicitSpace)) { Console.Error.WriteLine("invalid space name."); return 2; }
                spaceExplicitlyGiven = true;
                if (positionals.Count >= 2) id = positionals[1];
            }
        }
        if (positionals.Count > 2) { Console.Error.WriteLine("too many arguments."); return 2; }

        int modeCount = (viewMode ? 1 : 0)
                      + (appendText is not null ? 1 : 0)
                      + (overwriteText is not null ? 1 : 0)
                      + (clearMode ? 1 : 0);
        if (modeCount > 1) { Console.Error.WriteLine("use only one of --view / --append / --overwrite / --clear."); return 2; }

        var paths = new RinnePaths(Environment.CurrentDirectory);
        var spaceSvc = new SpaceService(paths);

        string effectiveSpace;
        bool usedCurrentPointer = false;
        if (spaceExplicitlyGiven)
        {
            effectiveSpace = explicitSpace!;
        }
        else
        {
            try
            {
                effectiveSpace = spaceSvc.GetCurrentSpaceFromPointer();
                usedCurrentPointer = true;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 2;
            }
        }

        var spaceDir = Path.Combine(paths.SnapshotsRoot, "space", effectiveSpace);
        if (!Directory.Exists(spaceDir))
        {
            Console.Error.WriteLine($"space not found: {effectiveSpace}");
            return 1;
        }

        if (id == null && (viewMode || appendText is not null || overwriteText is not null || clearMode))
        {
            Console.Error.WriteLine("missing <id|@N> argument.");
            Console.Error.WriteLine("Hint: On PowerShell, quote @N. Example:  rinne note '@0' --view");
            return 2;
        }

        if (id == null)
            return await ListNotes(spaceDir, usedCurrentPointer ? "current" : effectiveSpace, ct);

        var snapshotDir = ResolveSnapshotDir(spaceDir, id);
        if (snapshotDir == null)
        {
            Console.Error.WriteLine($"snapshot not found for id: {id}");
            return 1;
        }

        var noteService = new NoteService();
        var fileName = NoteService.DefaultFileName;
        var notePath = Path.Combine(snapshotDir, fileName);

        if (viewMode) return ViewNote(noteService, snapshotDir, notePath, fileName);
        if (appendText is not null) return AppendNote(noteService, snapshotDir, fileName, appendText, ct);
        if (overwriteText is not null) return OverwriteNote(noteService, snapshotDir, fileName, overwriteText, ct);
        if (clearMode) return ClearNote(noteService, snapshotDir, fileName, ct);

        Console.Error.WriteLine("no action. Use --view, --append, --overwrite or --clear.");
        return 2;
    }

    private static bool LooksLikeId(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        if (s[0] == '@') return true;
        if (char.IsDigit(s[0])) return true;
        if (s.Contains('_')) return true;
        return false;
    }

    private async Task<int> ListNotes(string spaceDir, string headerLabel, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Console.WriteLine($"Notes in space: {headerLabel}");

        int count = 0;
        foreach (var snapDir in Directory.EnumerateDirectories(spaceDir))
        {
            ct.ThrowIfCancellationRequested();
            var notePath = Path.Combine(snapDir, NoteService.DefaultFileName);
            if (File.Exists(notePath))
            {
                count++;
                Console.WriteLine($"- {Path.GetFileName(snapDir)}  ({NoteService.DefaultFileName})");
            }
        }
        if (count == 0) Console.WriteLine("(no notes found)");
        return 0;
    }

    private int ViewNote(NoteService noteService, string snapshotRoot, string notePath, string fileName)
    {
        if (!File.Exists(notePath))
        {
            Console.Error.WriteLine($"{fileName} not found.");
            return 1;
        }
        var content = noteService.Read(snapshotRoot, fileName) ?? "";
        Console.WriteLine($"---- {fileName} ----");
        Console.WriteLine(content);
        Console.WriteLine(new string('-', fileName.Length + 10));
        return 0;
    }

    private int AppendNote(
        NoteService noteService,
        string snapshotRoot,
        string fileName,
        string appendText,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var old = noteService.Read(snapshotRoot, fileName) ?? string.Empty;

        var sb = new StringBuilder(old);
        if (sb.Length > 0 && !sb.ToString().EndsWith("\n") && !sb.ToString().EndsWith("\r"))
            sb.AppendLine();
        sb.Append(appendText);

        noteService.Write(
            snapshotRoot,
            new NoteService.WriteOptions(
                Text: sb.ToString(),
                FileName: fileName,
                Overwrite: true,
                EnsureUtf8Bom: true,
                UseCrLf: true
            ),
            ct
        );

        Console.WriteLine("Note appended.");
        return 0;
    }

    private int OverwriteNote(
        NoteService noteService,
        string snapshotRoot,
        string fileName,
        string text,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        noteService.Write(
            snapshotRoot,
            new NoteService.WriteOptions(
                Text: text,
                FileName: fileName,
                Overwrite: true,
                EnsureUtf8Bom: true,
                UseCrLf: true
            ),
            ct
        );

        Console.WriteLine(text.Length == 0 ? "Note cleared." : "Note overwritten.");
        return 0;
    }

    private int ClearNote(
        NoteService noteService,
        string snapshotRoot,
        string fileName,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        noteService.Clear(snapshotRoot, fileName, ensureUtf8Bom: true);
        Console.WriteLine("Note cleared.");
        return 0;
    }

    private string? ResolveSnapshotDir(string spaceDir, string idOrAt)
    {
        if (idOrAt.StartsWith("@") && int.TryParse(idOrAt.AsSpan(1), out int n) && n >= 0)
        {
            var snaps = Directory.GetDirectories(spaceDir)
                                 .OrderByDescending(x => x)
                                 .ToList();
            if (n >= snaps.Count) return null;
            return snaps[n];
        }

        foreach (var dir in Directory.EnumerateDirectories(spaceDir))
        {
            var name = Path.GetFileName(dir);
            if (name.StartsWith(idOrAt, StringComparison.OrdinalIgnoreCase))
                return dir;
        }
        return null;
    }
}
