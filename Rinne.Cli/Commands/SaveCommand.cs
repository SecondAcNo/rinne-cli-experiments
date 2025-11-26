using Rinne.Cli.Commands.Interfaces;
using Rinne.Core.Common;
using Rinne.Core.Features.Meta;
using Rinne.Core.Features.Notes;
using Rinne.Core.Features.Snapshots;
using Rinne.Core.Features.Space;
using System.Text.Json;

namespace Rinne.Cli.Commands;

public sealed class SaveCommand : ICliCommand
{
    public string Name => "save";
    public IEnumerable<string> Aliases => new[] { "s" };
    public string Summary =>
        $"Create a snapshot under .rinne/snapshots/space/<space>/<id>/. " +
        $"Always creates <id>/{NoteService.DefaultFileName}; if -m is given, writes text. " +
        $"Use --compact, --compact-speed, or --compact-full to save directly to CAS (chunk+compression). " +
        $"Use --hash-none to skip SnapshotHash and write meta with HashAlgorithm=\"skip\".";

    public string Usage => $"""
        Usage:
          rinne save [<space>] [-m <text>] [--compact|-c|--compact-speed|--compact-full] [--hash-none]

        Description:
          - Creates ONE snapshot of the current directory into THIS repository.
          - If <space> is omitted, it defaults to the current space (see `rinne space current`).
          - This command ALWAYS creates '<id>/{NoteService.DefaultFileName}'.
          - If -m/--message is provided, its text is written (initial-only here; later edits via 'rinne note').
          - If --compact/-c is specified, the snapshot is stored directly to CAS (chunk+compressed) without creating payload files.
          - If `--compact-speed` is specified, an experimental high-performance compact path is used. 
            This mode is less tested, uses significantly more memory, and may have undiscovered issues.
          - If --compact-full is specified, the snapshot is stored directly to CAS with full content verification (no mtime/size-based skip).
          - If --hash-none is specified, SnapshotHash computation is skipped and meta.json is written with HashAlgorithm="skip" and SnapshotHash="SKIP".

        Examples:
          rinne save
          rinne save main -m "first snapshot"
          rinne save work --compact -m "compact save"
          rinne save work --compact-speed -m "fast compact (experimental)"
          rinne save work --compact-full -m "verify all files"
          rinne save main --hash-none -m "fast meta without content hash"
        """;

    public async Task<int> RunAsync(string[] args, CancellationToken ct)
    {
        string? spaceArg = null;
        string? messageText = null;
        bool useCompact = false;
        bool useCompactFull = false;
        bool useCompactSpeed = false;
        bool useHashNone = false;

        var positionals = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            var a = args[i];

            if (a == "-m" || a == "--message")
            {
                if (i + 1 >= args.Length)
                {
                    Console.Error.WriteLine("option '-m/--message' requires a value.");
                    Console.WriteLine("Use: " + Usage);
                    return 2;
                }
                messageText = args[++i];
            }
            else if (a == "--compact" || a == "-c")
            {
                useCompact = true;
            }
            else if (a == "--compact-full")
            {
                useCompactFull = true;
            }
            else if (a == "--compact-speed")
            {
                useCompactSpeed = true;
            }
            else if (a == "--hash-none")
            {
                useHashNone = true;
            }
            else if (IsOption(a))
            {
                Console.Error.WriteLine($"unknown option: {a}");
                Console.WriteLine("Use: " + Usage);
                return 2;
            }
            else
            {
                positionals.Add(a);
            }
        }

        // --compact 系オプションの排他チェック
        int compactFlags = 0;
        if (useCompact) compactFlags++;
        if (useCompactFull) compactFlags++;
        if (useCompactSpeed) compactFlags++;

        if (compactFlags > 1)
        {
            Console.Error.WriteLine("options --compact, --compact-speed, and --compact-full cannot be used together.");
            Console.WriteLine("Use: " + Usage);
            return 2;
        }

        if (positionals.Count > 1)
        {
            Console.Error.WriteLine("too many arguments.");
            Console.WriteLine("Use: " + Usage);
            return 2;
        }
        if (positionals.Count == 1)
        {
            spaceArg = positionals[0];
        }

        var paths = new RinnePaths(Environment.CurrentDirectory);
        var spaceSvc = new SpaceService(paths);
        var space = spaceArg ?? spaceSvc.GetCurrentSpaceFromPointer();

        if (!IsValidSpaceName(space))
        {
            Console.Error.WriteLine($"invalid space name. Use {SpaceNameRules.HumanReadable}");
            return 2;
        }

        var opt = new SnapshotOptions(
            SourceRoot: Environment.CurrentDirectory,
            Space: space
        );

        SnapshotResult res;
        try
        {
            if (useCompact || useCompactFull || useCompactSpeed)
            {
                if (useCompactSpeed)
                {
                    // Experimental faster path (used only for --compact-speed).
                    // The amount of testing is not sufficient.
                    var hashMode = useHashNone ? CompactSaverParallelMemory.HashMode.None : CompactSaverParallelMemory.HashMode.Full;
                    res = await Task.Run(() => CompactSaverParallelMemory.SaveCompact(
                        opt,
                        cp: null,
                        fullHashCheck: useCompactFull, // should always be false here due to mutual exclusion
                        hashMode: hashMode
                    ), ct);
                }
                else
                {
                    var hashMode = useHashNone ? CompactSaverParallel.HashMode.None : CompactSaverParallel.HashMode.Full;
                    res = await Task.Run(() => CompactSaverParallel.SaveCompact(
                        opt,
                        cp: null,
                        fullHashCheck: useCompactFull,
                        hashMode: hashMode
                    ), ct);
                }
            }
            else
            {
                var hashMode = useHashNone ? SnapshotSaver.HashMode.None : SnapshotSaver.HashMode.Full;
                res = await Task.Run(() => SnapshotSaver.Save(opt, hashMode), ct);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"save failed: {ex.Message}");
            return 3;
        }

        Console.WriteLine($"Saved to: {res.TargetDir}");
        Console.WriteLine($"Files: {res.CopiedFiles}, Bytes: {res.CopiedBytes}");

        if (res.HasErrors)
        {
            foreach (var e in res.Errors.Take(5))
                Console.Error.WriteLine(e);
            return 3;
        }

        try
        {
            if (useCompact || useCompactFull || useCompactSpeed)
            {
                var metaPath = Path.Combine(res.TargetDir, "meta.json");
                await using var fs = File.OpenRead(metaPath);
                var meta = await JsonSerializer.DeserializeAsync<SnapshotMeta>(fs, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                }, ct);
                if (meta is null) throw new InvalidDataException("invalid meta.json");
                Console.WriteLine($"Meta: v={meta.Version}, hash={meta.SnapshotHash}");
            }
            else
            {
                var metaService = new MetaService();
                var meta = metaService.WriteMeta(res.TargetDir, ct);
                Console.WriteLine($"Meta: v={meta.Version}, hash={meta.SnapshotHash}");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"failed to finalize meta.json: {ex.Message}");
            return 4;
        }

        var noteService = new NoteService();

        if (!string.IsNullOrEmpty(messageText))
        {
            try
            {
                noteService.Write(
                    snapshotRoot: res.TargetDir,
                    opt: new NoteService.WriteOptions(
                        Text: messageText,
                        FileName: NoteService.DefaultFileName,
                        Overwrite: false,
                        EnsureUtf8Bom: true,
                        UseCrLf: true
                    ),
                    ct: ct
                );
                Console.WriteLine("Note: written to note.md");
            }
            catch (IOException ioEx)
            {
                Console.Error.WriteLine($"note.md not written: {ioEx.Message}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"failed to write note.md: {ex.Message}");
                return 5;
            }
        }
        else
        {
            var created = noteService.Ensure(
                res.TargetDir,
                NoteService.DefaultFileName,
                ensureUtf8Bom: true
            );

            Console.WriteLine(created
                ? "Note: created empty note.md"
                : "Note: note.md already exists");
        }

        return 0;
    }

    private static bool IsOption(string s) => s.StartsWith("-", StringComparison.Ordinal);

    private static bool IsValidSpaceName(string name) => SpaceNameRules.NameRegex.IsMatch(name);
}
