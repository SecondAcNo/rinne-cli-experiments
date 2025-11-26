using Rinne.Cli.Commands.Interfaces;
using Rinne.Core.Common;
using Rinne.Core.Features.Meta;
using Rinne.Core.Features.Notes;
using Rinne.Core.Features.Recompose;
using Rinne.Core.Features.Space;
using System.Globalization;

namespace Rinne.Cli.Commands;

public sealed class RecomposeCommand : ICliCommand
{
    public string Name => "recompose";
    public IEnumerable<string> Aliases => new[] { "rc" };
    public string Summary => $"Compose multiple snapshots (left-most wins) into a new snapshot; then write meta.json and ensure <id>/{NoteService.DefaultFileName} (write text if -m).";
    public string Usage => """
            Usage:
              rinne recompose [<target-space>] --src <spec> [--src <spec> ...] [options] [-m <text>]
              rinne recompose --space <target-space> --src <spec> [--src <spec> ...] [options] [-m <text>]

            Spec:
              <spec> := [space:]<idprefix> | [space:]@<N>
                - <idprefix> : Unique snapshot id prefix (e.g., 20251111T..., 018EF9...)
                - @<N>       : N back from newest (0=latest), e.g., @0, @1, @2
                - Optional leading "space:" selects source space; omitted => target space

            Options:
              --hydrate             If a source is missing payload, hydrate persistently
              --hydrate=ephemeral   If a source is missing payload, hydrate temporarily (alias: --hydrate=tmp)
              -m, --message <text>  Initial note to save as '<id>/note.md' (no overwrite here; use 'rinne note' later)
            """;

    private readonly RinnePaths _paths = new(Environment.CurrentDirectory);

    public async Task<int> RunAsync(string[] args, CancellationToken ct)
    {
        string? spaceArg = null;
        var sources = new List<RecomposeService.SourceSpec>();

        bool autoHydrate = false;
        bool ephemeralHydrate = false;
        string? messageText = null;

        for (int i = 0; i < args.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            var a = args[i];

            if (a == "-m" || a == "--message")
            {
                messageText = NeedSrcValue(args, ref i, a);
                continue;
            }

            if (!a.StartsWith("--", StringComparison.Ordinal))
            {
                if (spaceArg is null) { spaceArg = a; continue; }
                Console.Error.WriteLine($"unknown argument: {a}");
                Console.WriteLine(Usage);
                return 2;
            }

            switch (a)
            {
                case "--space":
                    spaceArg = NeedSrcValue(args, ref i, "--space");
                    break;

                case "--src":
                case "--from":
                case "--source":
                    {
                        var spec = NeedSrcValue(args, ref i, a);
                        sources.Add(ParseSourceSpec(spec));
                        break;
                    }

                case "--hydrate":
                    if (a.Contains('='))
                    {
                        var mode = a[(a.IndexOf('=') + 1)..].Trim();
                        if (mode.Equals("ephemeral", StringComparison.OrdinalIgnoreCase) ||
                            mode.Equals("tmp", StringComparison.OrdinalIgnoreCase))
                        {
                            ephemeralHydrate = true; autoHydrate = false;
                        }
                        else if (mode.Equals("persist", StringComparison.OrdinalIgnoreCase) ||
                                 mode.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
                                 mode.Length == 0)
                        {
                            autoHydrate = true; ephemeralHydrate = false;
                        }
                        else
                        {
                            Console.Error.WriteLine("invalid --hydrate value. use: --hydrate, or --hydrate=ephemeral|tmp");
                            return 2;
                        }
                    }
                    else
                    {
                        autoHydrate = true; ephemeralHydrate = false;
                    }
                    break;

                default:
                    if (a.StartsWith("--hydrate=", StringComparison.Ordinal))
                    {
                        var mode = a["--hydrate=".Length..].Trim();
                        if (mode.Equals("ephemeral", StringComparison.OrdinalIgnoreCase) ||
                            mode.Equals("tmp", StringComparison.OrdinalIgnoreCase))
                        {
                            ephemeralHydrate = true; autoHydrate = false; break;
                        }
                        if (mode.Equals("persist", StringComparison.OrdinalIgnoreCase) ||
                            mode.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
                            mode.Length == 0)
                        {
                            autoHydrate = true; ephemeralHydrate = false; break;
                        }
                        Console.Error.WriteLine("invalid --hydrate value. use: --hydrate, or --hydrate=ephemeral|tmp");
                        return 2;
                    }

                    Console.Error.WriteLine($"unknown option: {a}");
                    Console.WriteLine(Usage);
                    return 2;
            }
        }

        if (sources.Count == 0)
        {
            Console.Error.WriteLine("at least one --src <spec> is required.");
            Console.WriteLine(Usage);
            return 2;
        }

        var spaceSvc = new SpaceService(_paths);
        string targetSpace;
        try
        {
            targetSpace = spaceArg ?? spaceSvc.GetCurrentSpaceFromPointer();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 2;
        }

        var normalizedSources = sources.Select(s => s with { Space = string.IsNullOrWhiteSpace(s.Space) ? targetSpace : s.Space })
                                       .ToList();

        var service = new RecomposeService(_paths);
        var opt = new RecomposeService.Options(
            TargetSpace: targetSpace,
            Sources: normalizedSources,
            NewSnapshotId: null,
            AutoHydrate: autoHydrate,
            EphemeralHydrate: ephemeralHydrate
        );

        var res = await service.RunAsync(opt, ct);

        if (!res.Created)
        {
            Console.Error.WriteLine($"recompose failed: {res.Error}");
            return 1;
        }

        var finalSnapDir = _paths.Snapshot(targetSpace, res.NewSnapshotId!);

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
                    Overwrite: created,
                    EnsureUtf8Bom: true,
                    UseCrLf: true
                ), ct);
                Console.WriteLine("Note: written to note.md");
            }
            catch (IOException)
            {
                Console.Error.WriteLine("Note: note.md already exists; not overwritten here. Use 'rinne note' to modify.");
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

        Console.WriteLine($"recompose ok: {res.NewSnapshotId}");
        Console.WriteLine($"  space   : {targetSpace}");
        Console.WriteLine($"  payload : {res.OutputPayloadDir}");
        Console.WriteLine($"  from    : {string.Join(" + ", res.ResolvedIds)}");
        Console.WriteLine($"  made    : dirs={res.DirsCreated}, files={res.FilesCopied}");
        return 0;
    }

    static string NeedSrcValue(string[] args, ref int i, string opt)
    {
        if (i + 1 >= args.Length)
            throw new ArgumentException($"missing value for {opt}");

        var v = args[i + 1];
        if (v.StartsWith("--", StringComparison.Ordinal))
            throw new ArgumentException(
                $"missing value for {opt}. If your value begins with '@' in PowerShell, quote it like: --src '@0'.");

        i++;
        return v;
    }

    private static RecomposeService.SourceSpec ParseSourceSpec(string spec)
    {
        spec = spec?.Trim() ?? throw new ArgumentException("invalid --src: empty");
        string? space = null;
        string body = spec;

        var idx = spec.IndexOf(':');
        if (idx >= 0)
        {
            space = spec[..idx];
            body = spec[(idx + 1)..];
            if (space.Length == 0)
                throw new ArgumentException("invalid --src: empty space before ':'");
        }

        if (string.IsNullOrWhiteSpace(body))
            throw new ArgumentException("invalid --src: empty selector.");

        if (body.StartsWith("--", StringComparison.Ordinal))
            throw new ArgumentException("invalid --src value. In PowerShell, quote '@N' like: --src '@0'");

        if (body[0] == '@')
        {
            if (!int.TryParse(body[1..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) || n < 0)
                throw new ArgumentException("invalid --src '@N': N must be a non-negative integer.");
            return new RecomposeService.SourceSpec(space, IdPrefix: null, NthFromNewest: n);
        }
        else
        {
            return new RecomposeService.SourceSpec(space, IdPrefix: body, NthFromNewest: null);
        }
    }
}
