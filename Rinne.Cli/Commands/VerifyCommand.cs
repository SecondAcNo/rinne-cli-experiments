using Rinne.Cli.Commands.Interfaces;
using Rinne.Core.Common;
using Rinne.Core.Features.Meta;
using Rinne.Core.Features.Space;
using Rinne.Core.Features.Verify;
using System.Globalization;

namespace Rinne.Cli.Commands;

public sealed class VerifyCommand : ICliCommand
{
    public string Name => "verify";
    public IEnumerable<string> Aliases => Array.Empty<string>();
    public string Summary => "Verify snapshots by comparing computed state hash against meta.json.";

    public string Usage => """
        Usage:
          rinne verify [<space>] [ids...] [options...]
          rinne verify --space <space> [ids...] [options...]

        If no ids are provided, all snapshots in the space are verified.
        ids accepts snapshot ID prefixes or @N selectors (both are resolved to unique snapshot IDs).

        Options:
          --space <space>                 Explicit space; if omitted, the current space is used.

          --policy <error|skip|hydrate|temp>
                                          How to handle missing payload (chunk+compressed).
                                            error   : treat as error (default)
                                            skip    : ignore and continue
                                            hydrate : permanently restore payload before verify
                                            temp    : restore payload to a temporary location only for verify

          --show-details                  Show all results including OK (by default only non-OK entries are shown).
          --only-bad                      When combined with --show-details, suppress OK entries.
        """;


    private readonly RinnePaths _paths = new(Environment.CurrentDirectory);

    public async Task<int> RunAsync(string[] args, CancellationToken ct)
    {
        string? spaceArg = null;
        var ids = new List<string>();
        var policy = MissingPayloadPolicy.Error;
        bool showDetails = false;
        bool onlyBad = false;

        for (int i = 0; i < args.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            var a = args[i];

            if (!a.StartsWith("--", StringComparison.Ordinal))
            {
                if (spaceArg is null) { spaceArg = a; continue; }
                ids.Add(a);
                continue;
            }

            switch (a)
            {
                case "--space":
                    spaceArg = CliArgs.NeedValue(args, ref i, "--space");
                    break;

                case "--policy":
                    {
                        var s = CliArgs.NeedValue(args, ref i, "--policy");
                        if (!TryParsePolicy(s, out policy))
                            throw new ArgumentException($"invalid --policy: {s}");
                        break;
                    }

                case "--show-details":
                    showDetails = true;
                    break;

                case "--only-bad":
                    onlyBad = true;
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

        var metaSvc = new MetaService();
        var verifySvc = new VerifyService(_paths, metaSvc);

        try
        {
            List<string>? resolvedIds = null;
            if (ids.Count > 0)
            {
                resolvedIds = ResolveSelectorsToIds(space, ids);
            }

            var summary = await verifySvc.RunAsync(
                space: space,
                ids: resolvedIds,
                policy: policy,
                workers: Environment.ProcessorCount,
                ct: ct);

            Console.WriteLine("Verify done.");
            Console.WriteLine($"  space              : {space}");
            Console.WriteLine($"  targets            : {summary.Total}");
            Console.WriteLine($"  OK                 : {summary.Ok}");
            Console.WriteLine($"  MISMATCH           : {summary.Mismatch}");
            Console.WriteLine($"  META-NOT-FOUND     : {summary.MetaMissing}");
            Console.WriteLine($"  PAYLOAD-MISSING    : {summary.PayloadMissingError}");
            Console.WriteLine($"  HYDRATED-OK        : {summary.Hydrated}");
            Console.WriteLine($"  TEMP-HYDRATED-OK   : {summary.TempHydrated}");
            Console.WriteLine($"  HYDRATE-FAIL       : {summary.HydrateFail}");
            Console.WriteLine($"  TEMP-HYDRATE-FAIL  : {summary.TempHydrateFail}");
            Console.WriteLine($"  OTHER ERRORS       : {summary.OtherErrors}");

            if (showDetails || summary.Mismatch > 0 || summary.MetaMissing > 0 ||
                summary.PayloadMissingError > 0 || summary.HydrateFail > 0 ||
                summary.TempHydrateFail > 0 || summary.OtherErrors > 0)
            {
                Console.WriteLine();
                Console.WriteLine("Details:");
                int printed = 0;
                const int cap = 200;
                foreach (var d in summary.Details)
                {
                    if (onlyBad && string.Equals(d.Status, "OK", StringComparison.OrdinalIgnoreCase))
                        continue;

                    Console.WriteLine($"  [{d.Id}] {d.Status} - {d.Message}");
                    printed++;
                    if (!showDetails && printed >= cap)
                    {
                        Console.WriteLine($"  ... (+{summary.Details.Count - printed} more)");
                        break;
                    }
                }
            }

            bool success =
                summary.Mismatch == 0 &&
                summary.MetaMissing == 0 &&
                summary.PayloadMissingError == 0 &&
                summary.HydrateFail == 0 &&
                summary.TempHydrateFail == 0 &&
                summary.OtherErrors == 0;

            return success ? 0 : 1;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("verify cancelled.");
            return 130;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"verify failed: {ex.Message}");
            return 1;
        }
    }

    private static bool TryParsePolicy(string s, out MissingPayloadPolicy policy)
    {
        switch (s.Trim().ToLowerInvariant())
        {
            case "error": policy = MissingPayloadPolicy.Error; return true;
            case "skip": policy = MissingPayloadPolicy.Skip; return true;
            case "hydrate": policy = MissingPayloadPolicy.Hydrate; return true;
            case "temp": policy = MissingPayloadPolicy.TempHydrate; return true;
            default: policy = default; return false;
        }
    }

    private static List<string> ResolveSelectorsToIds(string space, List<string> selectors)
    {
        var baseDir = Path.Combine(Environment.CurrentDirectory, ".rinne", "snapshots", "space", space);
        if (!Directory.Exists(baseDir))
            throw new DirectoryNotFoundException($"space not found: {space}");

        var allIds = Directory
            .GetDirectories(baseDir)
            .Select(d => Path.GetFileName(d))
            .Where(id => !string.IsNullOrEmpty(id))
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

        if (allIds.Count == 0)
            throw new InvalidOperationException($"no snapshots found in space: {space}");

        var result = new List<string>();

        foreach (var sel in selectors)
        {
            if (string.IsNullOrWhiteSpace(sel))
                continue;

            if (sel.StartsWith("@", StringComparison.Ordinal))
            {
                var nStr = sel.Substring(1);
                if (!int.TryParse(nStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) || n <= 0)
                    throw new ArgumentException($"invalid selector '{sel}': @N must be positive integer.");

                if (n > allIds.Count)
                    throw new ArgumentException($"selector '{sel}' is out of range. space '{space}' has only {allIds.Count} snapshots.");

                // 最新 = allIds の最後
                var index = allIds.Count - n;
                result.Add(allIds[index]);
            }
            else
            {
                var matches = allIds
                    .Where(id => id.StartsWith(sel, StringComparison.Ordinal))
                    .ToList();

                if (matches.Count == 0)
                    throw new ArgumentException($"no snapshot matches selector: {sel} (space: {space})");

                if (matches.Count > 1)
                    throw new ArgumentException($"ambiguous snapshot selector: {sel} (space: {space}, matches: {matches.Count})");

                result.Add(matches[0]);
            }
        }

        return result;
    }
}
