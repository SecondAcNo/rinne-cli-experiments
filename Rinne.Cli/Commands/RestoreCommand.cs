using Rinne.Cli.Commands.Interfaces;
using Rinne.Core.Common;
using Rinne.Core.Features.Restore;
using Rinne.Core.Features.Space;

namespace Rinne.Cli.Commands;

public sealed class RestoreCommand : ICliCommand
{
    public string Name => "restore";
    public IEnumerable<string> Aliases => Array.Empty<string>();
    public string Summary => "Restore working tree from a full snapshot (<id>/snapshots).";

    public string Usage => """
            Usage:
              rinne restore [<space>] [selectors...] [options...]
              rinne restore --space <space> [selectors...] [options...]

            Selectors (choose one; default = latest):
              --id <prefix>         Unique snapshot id prefix (wins over others)
              --back N              N back from latest (0=latest)
              --offset N            Alias of --back

            Options:
              --to <dir>            Destination root (default: current directory)
              --purge               Clean replace (remove everything except .rinne, then restore)
              --hydrate             If payload missing, hydrate persistently (<id>/snapshots is created)
              --hydrate=ephemeral   If payload missing, hydrate temporarily (no <id>/snapshots)
              --hydrate=tmp         Alias of --hydrate=ephemeral

            Notes:
              - If neither <space> nor --space is given, the command reads the current space from:
                  .rinne/snapshots/current  (single-line text)
              - Default restore mode is a non-destructive merge (extra files are kept). Use --purge for full clean replace.
            """;

    private readonly RinnePaths _paths = new(Environment.CurrentDirectory);

    public async Task<int> RunAsync(string[] args, CancellationToken ct)
    {
        string? spaceArg = null;

        string? idPrefix = null;
        int? back = null;

        string? dest = null;
        bool purge = false;
        bool autoHydrate = false;
        bool ephemeralHydrate = false;

        for (int i = 0; i < args.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            var a = args[i];

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
                    spaceArg = CliArgs.NeedValue(args, ref i, "--space");
                    break;

                case "--id":
                    idPrefix = CliArgs.NeedValue(args, ref i, "--id");
                    break;

                case "--back":
                    back = CliArgs.ParseNonNegativeInt(CliArgs.NeedValue(args, ref i, "--back"), "--back");
                    break;

                case "--offset":
                    back = CliArgs.ParseNonNegativeInt(CliArgs.NeedValue(args, ref i, "--offset"), "--offset");
                    break;

                case "--to":
                case "--dest":
                    dest = CliArgs.NeedValue(args, ref i, a);
                    break;

                case "--purge":
                    purge = true;
                    break;

                case "--hydrate":
                    if (a.Contains('='))
                    {
                        var mode = a[(a.IndexOf('=') + 1)..].Trim();
                        if (mode.Equals("ephemeral", StringComparison.OrdinalIgnoreCase) ||
                            mode.Equals("tmp", StringComparison.OrdinalIgnoreCase))
                        {
                            ephemeralHydrate = true;
                            autoHydrate = false;
                        }
                        else if (mode.Equals("persist", StringComparison.OrdinalIgnoreCase) ||
                                 mode.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
                                 mode.Length == 0)
                        {
                            autoHydrate = true;
                            ephemeralHydrate = false;
                        }
                        else
                        {
                            Console.Error.WriteLine("invalid --hydrate value. use: --hydrate, or --hydrate=ephemeral|tmp");
                            return 2;
                        }
                    }
                    else
                    {
                        autoHydrate = true;
                        ephemeralHydrate = false;
                    }
                    break;

                default:
                    if (a.StartsWith("--hydrate=", StringComparison.Ordinal))
                    {
                        var mode = a["--hydrate=".Length..].Trim();
                        if (mode.Equals("ephemeral", StringComparison.OrdinalIgnoreCase) ||
                            mode.Equals("tmp", StringComparison.OrdinalIgnoreCase))
                        {
                            ephemeralHydrate = true;
                            autoHydrate = false;
                            break;
                        }
                        if (mode.Equals("persist", StringComparison.OrdinalIgnoreCase) ||
                            mode.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
                            mode.Length == 0)
                        {
                            autoHydrate = true;
                            ephemeralHydrate = false;
                            break;
                        }
                        Console.Error.WriteLine("invalid --hydrate value. use: --hydrate, or --hydrate=ephemeral|tmp");
                        return 2;
                    }

                    Console.Error.WriteLine($"unknown option: {a}");
                    Console.WriteLine(Usage);
                    return 2;
            }
        }

        if (!string.IsNullOrWhiteSpace(idPrefix) && back is not null)
        {
            Console.Error.WriteLine("use either --id or --back/--offset (not both).");
            return 2;
        }

        if (string.IsNullOrWhiteSpace(idPrefix) && back is null)
            back = 0;

        var spaceSvc = new SpaceService(_paths);
        string space;
        try
        {
            space = spaceArg ?? spaceSvc.GetCurrentSpaceFromPointer();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 2;
        }

        var service = new RestoreService(_paths);
        var opt = new RestoreService.Options(
            Space: space,
            IdPrefix: idPrefix,
            NthFromNewest: back,
            Destination: dest,
            AutoHydrate: autoHydrate,
            EphemeralHydrate: ephemeralHydrate,
            PurgeAll: purge
        );

        var result = await service.RunAsync(opt, ct);

        if (result.Restored)
        {
            Console.WriteLine($"restore ok: {result.SnapshotId}");
            Console.WriteLine($"  dest : {result.Destination}");
            if (!string.IsNullOrEmpty(result.SourcePayload))
                Console.WriteLine($"  source: {result.SourcePayload}");
            return 0;
        }
        else
        {
            Console.Error.WriteLine($"restore failed: {result.Error}");
            return 1;
        }
    }
}
