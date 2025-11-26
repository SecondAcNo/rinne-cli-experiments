using Rinne.Cli.Commands.Interfaces;
using Rinne.Core.Common;
using Rinne.Core.Features.Export;
using Rinne.Core.Features.Space;

namespace Rinne.Cli.Commands;

public sealed class ExportCommand : ICliCommand
{
    public string Name => "export";
    public IEnumerable<string> Aliases => Array.Empty<string>();
    public string Summary => "Export full snapshot(s) as real files to a destination folder (safe & simple).";

    public string Usage => """
        Usage:
          rinne export <space> [selectors...] --to <dest>
          rinne export --space <space> [selectors...] --to <dest>
          rinne export --to <dest>                   # current space + @0 (latest)

        Selectors:
          - Each selector := full-id | id-prefix | @N  (N back; @0 = latest)
          - If no selector is given, @0 (latest) is used.

        Important:
          - The first positional argument is ALWAYS interpreted as <space>.
            When you pass any selector, you MUST also specify <space>
            (positionally or via --space).
            e.g., OK: 'rinne export main 2025 --to out'
                  NG : 'rinne export 2025 --to out'   (treated as space "2025")

        Options:
          --to|--dest <dir>   Destination root (required)
          --overwrite         Replace existing output folder if exists

        Notes:
          - When <space> is omitted entirely (no positional args),
            the current space is read from:
              .rinne/snapshots/current   (single-line text)
            and selectors default to @0 (latest).
          - Output layout (fixed):
              <dest>/<space>/<id>/{ meta.json, note.md, snapshots/... }
        """;

    private readonly RinnePaths _paths = new(Environment.CurrentDirectory);

    public async Task<int> RunAsync(string[] args, CancellationToken ct)
    {
        string? spaceArg = null;
        var selectors = new List<string>();

        string? dest = null;
        bool overwrite = false;

        for (int i = 0; i < args.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            var a = args[i];

            if (!a.StartsWith("--", StringComparison.Ordinal))
            {
                if (spaceArg is null) spaceArg = a;
                else selectors.Add(a);
                continue;
            }

            switch (a)
            {
                case "--space":
                    spaceArg = CliArgs.NeedValue(args, ref i, "--space");
                    break;

                case "--to":
                case "--dest":
                    dest = CliArgs.NeedValue(args, ref i, a);
                    break;

                case "--overwrite":
                    overwrite = true;
                    break;

                default:
                    Console.Error.WriteLine($"unknown option: {a}");
                    Console.WriteLine(Usage);
                    return 2;
            }
        }

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

        if (string.IsNullOrWhiteSpace(dest))
        {
            Console.Error.WriteLine("option '--to <dest>' is required.");
            Console.WriteLine(Usage);
            return 2;
        }

        if (selectors.Count == 0) selectors.Add("@0");

        var service = new ExportService(_paths);
        var opt = new ExportService.Options(
            Space: space,
            IdSelectors: selectors,
            DestinationRoot: dest!,
            Flat: false,
            Overwrite: overwrite,
            Workers: 0
        );

        ExportService.Result result;
        try
        {
            result = await service.RunAsync(opt, ct);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"export failed: {ex.Message}");
            return 1;
        }

        Console.WriteLine($"Export [{result.Space}] -> {result.Destination}");
        Console.WriteLine($"Total: {result.Total}, OK: {result.Ok}, Skipped: {result.Skipped}, Error: {result.Error}");
        foreach (var d in result.Details.OrderBy(d => d.Id, StringComparer.Ordinal))
        {
            Console.WriteLine($"  {d.Id}  {d.Status}  {d.Message}");
            if (!string.IsNullOrWhiteSpace(d.OutputPath))
                Console.WriteLine($"    -> {d.OutputPath}");
        }

        return result.Error == 0 ? 0 : 1;
    }
}
