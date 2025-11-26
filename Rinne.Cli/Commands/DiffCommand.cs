using System.Globalization;
using Rinne.Cli.Commands.Interfaces;
using Rinne.Core.Common;
using Rinne.Core.Features.Diff;
using Rinne.Core.Features.Space;

namespace Rinne.Cli.Commands;

public sealed class DiffCommand : ICliCommand
{
    public string Name => "diff";
    public IEnumerable<string> Aliases => Array.Empty<string>();
    public string Summary => "Compare two snapshots by @N selectors in the current or specified space.";
    public string Usage => """
        Usage:
          rinne diff [--space <space>] <@A> <@B>

        Options:
          --space <space>   Explicit space; if omitted, use the current space.

        Notes:
          - DEPRECATED: this command will be removed in a future version.
          - @0 is the latest snapshot, @1 is one before, and so on.
        """;

    private readonly RinnePaths _paths = new(Environment.CurrentDirectory);

    public async Task<int> RunAsync(string[] args, CancellationToken ct)
    {
        Console.Error.WriteLine("warning: 'rinne diff' is DEPRECATED and will be removed in the next version.");

        string? spaceArg = null;
        var pos = new List<string>();

        for (int i = 0; i < args.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            var a = args[i].Trim();

            if (!a.StartsWith("--", StringComparison.Ordinal))
            {
                pos.Add(a);
                continue;
            }

            switch (a)
            {
                case "--space":
                    if (spaceArg is not null)
                    {
                        Console.Error.WriteLine("--space specified more than once.");
                        Console.WriteLine(Usage);
                        return 2;
                    }
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("missing value for --space");
                        Console.WriteLine(Usage);
                        return 2;
                    }
                    spaceArg = args[++i].Trim();
                    break;

                default:
                    Console.Error.WriteLine($"unknown option: {a}");
                    Console.WriteLine(Usage);
                    return 2;
            }
        }

        if (pos.Count != 2)
        {
            Console.Error.WriteLine("two @N selectors are required.");
            Console.WriteLine(Usage);
            return 2;
        }

        var selA = pos[0];
        var selB = pos[1];
        if (!selA.StartsWith("@") || !selB.StartsWith("@"))
        {
            Console.Error.WriteLine("both arguments must be @N selectors (e.g., @1 @0).");
            Console.WriteLine(Usage);
            return 2;
        }

        string space;
        try
        {
            var spaceSvc = new SpaceService(_paths);
            space = spaceArg ?? spaceSvc.GetCurrentSpaceFromPointer();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 2;
        }

        string idA, idB;
        try
        {
            idA = ResolveAtN(space, selA);
            idB = ResolveAtN(space, selB);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 2;
        }

        var svc = new DiffService(_paths);
        try
        {
            var res = await svc.DiffAsync(space, idA, idB, new DiffService.Options(UseContentHash: false), ct);

            Console.WriteLine($"Diff [{space}]: {res.IdA} .. {res.IdB}");
            foreach (var c in res.Changes)
            {
                ct.ThrowIfCancellationRequested();
                switch (c.Kind)
                {
                    case DiffService.ChangeKind.Added:
                        Console.WriteLine($"+ {c.PathB}");
                        break;
                    case DiffService.ChangeKind.Removed:
                        Console.WriteLine($"- {c.PathA}");
                        break;
                    case DiffService.ChangeKind.Modified:
                        Console.WriteLine($"M {c.PathA}");
                        break;
                    case DiffService.ChangeKind.Unchanged:
                        Console.WriteLine($"= {c.PathA}");
                        break;
                }
            }
            Console.WriteLine($"Added: {res.Added}, Removed: {res.Removed}, Modified: {res.Modified}, Unchanged: {res.Unchanged}");
            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("diff cancelled.");
            return 130;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"diff failed: {ex.Message}");
            return 1;
        }
    }

    private static string ResolveAtN(string space, string selector)
    {
        if (!selector.StartsWith("@"))
            throw new ArgumentException($"invalid selector: {selector}");

        var s = selector.AsSpan(1);
        if (!int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) || n < 0)
            throw new ArgumentException($"invalid selector: {selector}");

        var dir = Path.Combine(Environment.CurrentDirectory, ".rinne", "snapshots", "space", space);
        if (!Directory.Exists(dir))
            throw new DirectoryNotFoundException($"space not found: {space}");

        var ids = Directory.GetDirectories(dir)
            .Select(Path.GetFileName)
            .Where(v => !string.IsNullOrEmpty(v))
            .OrderBy(v => v, StringComparer.Ordinal)
            .ToList();

        if (ids.Count == 0)
            throw new InvalidOperationException($"no snapshots under space: {space}");
        if (n >= ids.Count)
            throw new ArgumentOutOfRangeException(nameof(selector), $"@{n} is out of range (space has {ids.Count} snapshots).");

        var index = ids.Count - 1 - n;
        return ids[index]!;
    }
}
