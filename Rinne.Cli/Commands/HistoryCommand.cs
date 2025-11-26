using Rinne.Cli.Commands.Interfaces;
using Rinne.Core.Common;
using Rinne.Core.Features.History;
using Rinne.Core.Features.Space;
using System.Globalization;

namespace Rinne.Cli.Commands;

public sealed class HistoryCommand : ICliCommand
{
    public string Name => "history";
    public IEnumerable<string> Aliases => new[] { "hist", "log" };
    public string Summary => "List snapshots in a space (newest-first).";

    public string Usage => """
            Usage:
              rinne history [<space>] [--take N] [--since YYYY-MM-DD] [--before YYYY-MM-DD] [--match GLOB] [--size]
              rinne history --space <space> [--take N] [--since YYYY-MM-DD] [--before YYYY-MM-DD] [--match GLOB] [--size]

            Filters:
              --take N              Limit number of entries (newest-first)
              --since YYYY-MM-DD    Include snapshots >= date (local midnight → UTC)
              --before YYYY-MM-DD   Include snapshots <  date (local midnight → UTC)
              --match GLOB          Filter by snapshot ID using a glob pattern (* and ? supported).

            Options:
              --size / --bytes      Compute and show payload byte size (may be slow)
            """;

    private readonly RinnePaths _paths = new(Environment.CurrentDirectory);

    public async Task<int> RunAsync(string[] args, CancellationToken ct)
    {
        string? spaceArg = null;
        int? take = null;
        DateTimeOffset? since = null;
        DateTimeOffset? before = null;
        string? matchGlob = null;
        bool includeSize = false;

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

                case "--take":
                    take = CliArgs.ParseNonNegativeInt(CliArgs.NeedValue(args, ref i, "--take"), "--take");
                    break;

                case "--since":
                    {
                        var s = CliArgs.NeedValue(args, ref i, "--since");
                        if (DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dto))
                            since = dto.ToUniversalTime();
                        else
                            since = DateUtil.ParseLocalDateAsUtcMidnight(s);
                        break;
                    }

                case "--before":
                    {
                        var s = CliArgs.NeedValue(args, ref i, "--before");
                        if (DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dto))
                            before = dto.ToUniversalTime();
                        else
                            before = DateUtil.ParseLocalDateAsUtcMidnight(s);
                        break;
                    }

                case "--match":
                    {
                        if (matchGlob is not null)
                        {
                            Console.Error.WriteLine("--match cannot be specified more than once.");
                            Console.WriteLine(Usage);
                            return 2;
                        }
                        matchGlob = CliArgs.NeedValue(args, ref i, "--match");
                        break;
                    }

                case "--size":
                case "--bytes":
                    includeSize = true;
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

        var svc = new HistoryService(_paths);
        var opt = new HistoryService.Options(
            Space: space,
            Take: take,
            Since: since,
            Before: before,
            MatchGlobs: matchGlob is not null ? new[] { matchGlob } : null,
            IncludeSize: includeSize
        );

        var res = await Task.Run(() => svc.RunAsync(opt, ct), ct);

        if (res.Entries.Count == 0)
        {
            Console.WriteLine("(no snapshots)");
            return 0;
        }

        if (includeSize)
            Console.WriteLine("ID                                   CREATED (UTC)         PAYLOAD  MANIFEST  SIZE");
        else
            Console.WriteLine("ID                                   CREATED (UTC)         PAYLOAD  MANIFEST");

        foreach (var e in res.Entries)
        {
            ct.ThrowIfCancellationRequested();
            var id = e.Id.PadRight(35);
            var when = e.CreatedUtc.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture).PadRight(21);
            var payload = e.HasPayload ? "yes" : "no ";
            var manifest = e.HasManifest ? "yes" : "no ";

            if (includeSize)
            {
                var sizeText = e.PayloadBytes is long b ? FormatBytes(b).PadLeft(8) : "-".PadLeft(8);
                Console.WriteLine($"{id}  {when}  {payload,-7} {manifest,-8} {sizeText}");
            }
            else
            {
                Console.WriteLine($"{id}  {when}  {payload,-7} {manifest,-8}");
            }
        }

        return 0;
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB", "PB"];
        double val = bytes;
        int u = 0;
        while (val >= 1024 && u < units.Length - 1) { val /= 1024; u++; }
        return $"{val:0.##} {units[u]}";
    }
}
