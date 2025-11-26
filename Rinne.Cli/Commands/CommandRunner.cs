using Rinne.Cli.Commands.Interfaces;

namespace Rinne.Cli.Commands;

public static class CommandRunner
{
    private static readonly Dictionary<string, ICliCommand> _map =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly List<ICliCommand> _commands = new();

    public static void Register(params ICliCommand[] commands)
    {
        foreach (var c in commands)
        {
            _commands.Add(c);
            _map[c.Name] = c;
            foreach (var a in c.Aliases) _map[a] = c;
        }
    }

    public static async Task<int> RunAsync(string[] args, CancellationToken ct = default)
    {
        try
        {
            if (args.Length == 0 || IsHelp(args[0]))
            {
                PrintGlobalHelp();
                return 1;
            }

            if (!_map.TryGetValue(args[0], out var cmd))
            {
                Console.Error.WriteLine($"unknown command: {args[0]}");
                PrintGlobalHelp();
                return 1;
            }

            var sub = args.Skip(1).ToArray();
            if (sub.Any(IsHelp))
            {
                PrintCommandHelp(cmd);
                return 0;
            }

            return await cmd.RunAsync(sub, ct);
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("cancelled.");
            return 130;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("fatal: " + ex.Message);
            return 2;
        }
    }

    private static bool IsHelp(string s) => s is "-h" or "--help" or "/?";

    private static void PrintGlobalHelp()
    {
        Console.WriteLine("Usage: rinne <command> [options]");
        Console.WriteLine("Commands:");
        foreach (var c in _commands.OrderBy(c => c.Name))
            Console.WriteLine($"  {c.Name,-12} {c.Summary}");
        Console.WriteLine("Use: rinne <command> --help");
    }

    private static void PrintCommandHelp(ICliCommand c)
    {
        Console.WriteLine($"{c.Name} - {c.Summary}");
        Console.WriteLine("Use: " + c.Usage);
    }
}
