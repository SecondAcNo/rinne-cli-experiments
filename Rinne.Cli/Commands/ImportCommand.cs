using Rinne.Cli.Commands.Interfaces;
using Rinne.Core.Common;
using Rinne.Core.Features.Import;
using Rinne.Core.Features.Meta;
using Rinne.Core.Features.Notes;
using Rinne.Core.Features.Space;

namespace Rinne.Cli.Commands;

public sealed class ImportCommand : ICliCommand
{
    public string Name => "import";
    public IEnumerable<string> Aliases => new[] { "imp" };
    public string Summary => $"Import an external directory as a single full snapshot; then write meta.json and ensure <id>/{NoteService.DefaultFileName} (write text if -m).";
    public string Usage => """
        Usage:
          rinne import <source-directory> [--space <space>] [--dry-run] [-m <text>]

        Description:
          - Imports <source-directory> as ONE full snapshot into THIS repository (payload only).
          - Destination space defaults to the current space (see `rinne space current`).
          - Any '.rinne' under <source-directory> is ignored.
          - A new snapshot ID is generated from current UTC + UUIDv7.
          - This command ALWAYS creates '<id>/note.md'.
          - If -m is given, '<id>/note.md' is written (use 'rinne note' later to overwrite).
        """;

    private readonly RinnePaths _paths = new(Environment.CurrentDirectory);

    public async Task<int> RunAsync(string[] args, CancellationToken ct)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("invalid arguments.");
            Console.WriteLine(Usage);
            return 2;
        }

        string? sourceDirArg = null;
        string? spaceArg = null;
        bool dryRun = false;
        string? messageText = null;

        for (int i = 0; i < args.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            var a = args[i];

            if (a == "--space")
            {
                if (i + 1 >= args.Length)
                {
                    Console.Error.WriteLine("option '--space' requires a value.");
                    return 2;
                }
                spaceArg = args[++i];
            }
            else if (a == "--dry-run")
            {
                dryRun = true;
            }
            else if (a == "-m" || a == "--message")
            {
                if (i + 1 >= args.Length)
                {
                    Console.Error.WriteLine("option '--message' requires a value.");
                    return 2;
                }
                messageText = args[++i];
            }
            else if (IsOption(a))
            {
                Console.Error.WriteLine($"unknown option: {a}");
                return 2;
            }
            else
            {
                if (sourceDirArg is not null)
                {
                    Console.Error.WriteLine("multiple source directories specified.");
                    return 2;
                }
                sourceDirArg = a;
            }
        }

        if (sourceDirArg is null)
        {
            Console.Error.WriteLine("missing <source-directory>.");
            Console.WriteLine(Usage);
            return 2;
        }

        if (!Directory.Exists(_paths.RinneRoot))
        {
            Console.Error.WriteLine($".rinne not found under: {_paths.SourceRoot}");
            return 2;
        }

        var spaceSvc = new SpaceService(_paths);
        var space = spaceArg ?? spaceSvc.GetCurrentSpaceFromPointer();

        if (!SpaceNameRules.NameRegex.IsMatch(space))
        {
            Console.Error.WriteLine($"invalid space name. Use {SpaceNameRules.HumanReadable}");
            return 2;
        }

        var fullSourceDir = Path.GetFullPath(sourceDirArg);

        var svc = new ImportService(_paths);
        var opt = new ImportService.Options(
            SourceDirectory: fullSourceDir,
            DestSpace: space,
            DryRun: dryRun
        );
        var res = await svc.RunAsync(opt, ct);

        if (!res.Imported || res.Error is not null)
        {
            Console.Error.WriteLine($"import failed: {res.Error}");
            return 1;
        }

        if (dryRun)
        {
            Console.WriteLine("dry-run: import would create the following snapshot:");
            Console.WriteLine($"  source   : {fullSourceDir}");
            Console.WriteLine($"  dest     : {_paths.SourceRoot}");
            Console.WriteLine($"  space    : {res.DestSpace}");
            Console.WriteLine($"  snapshot : {res.SnapshotId ?? "(none)"}");
            Console.WriteLine($"  created  : {res.CreatedUtc:O}");
            return 0;
        }

        var finalSnapDir = _paths.Snapshot(space, res.SnapshotId!);

        try
        {
            var metaService = new MetaService();
            var meta = metaService.WriteMeta(finalSnapDir, ct);
            Console.WriteLine($"Meta: v={meta.Version}, hash={meta.SnapshotHash}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"failed to write meta.json: {ex.Message}");
            return 4;
        }

        var noteService = new NoteService();

        var created = noteService.Ensure(finalSnapDir, NoteService.DefaultFileName, ensureUtf8Bom: true);

        if (!string.IsNullOrEmpty(messageText))
        {
            try
            {
                noteService.Write(finalSnapDir, new NoteService.WriteOptions(
                    Text: messageText,
                    FileName: NoteService.DefaultFileName,
                    Overwrite: true,
                    EnsureUtf8Bom: true,
                    UseCrLf: true
                ), ct);
                Console.WriteLine("Note: written to note.md");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"failed to write note.md: {ex.Message}");
                return 5;
            }
        }
        else
        {
            Console.WriteLine(created ? "Note: created empty note.md" : "Note: note.md already exists");
        }

        Console.WriteLine("import ok");
        Console.WriteLine($"  source   : {fullSourceDir}");
        Console.WriteLine($"  dest     : {_paths.SourceRoot}");
        Console.WriteLine($"  space    : {res.DestSpace}");
        Console.WriteLine($"  snapshot : {res.SnapshotId}");
        Console.WriteLine($"  created  : {res.CreatedUtc:O}");
        return 0;
    }

    private static bool IsOption(string s) => s.StartsWith("-", StringComparison.Ordinal);
}
