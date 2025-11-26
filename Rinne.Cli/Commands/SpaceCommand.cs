using Rinne.Cli.Commands.Interfaces;
using Rinne.Core.Features.Space;
using Rinne.Core.Common;

namespace Rinne.Cli.Commands;

public sealed class SpaceCommand : ICliCommand
{
    public string Name => "space";
    public IEnumerable<string> Aliases => Array.Empty<string>();
    public string Summary => "List/Create/Use/Rename/Current/Delete spaces under .rinne/snapshots/space/<name>/";
    public string Usage => """
        Usage:
            rinne space list
            rinne space create <name>
            rinne space use <name>
            rinne space rename <old> <new>
            rinne space current
            rinne space delete <name>

        Description:
            Manage snapshot spaces in the current Rinne repository.

            A "space" is an independent timeline of snapshots.
            Each space has its own history and does not affect other spaces.
            This is similar to Git branches, but spaces always store full snapshots.

        Commands:
            list
                Show all existing spaces.

            create <name>
                Create a new empty space.
                Fails if the name already exists.

            use <name>
                Switch the active space.
                Updates `.rinne/snapshots/current` to point to <name>.

            rename <old> <new>
                Rename an existing space.
                Fails if <new> already exists.

            current
                Show the name of the active space.

            delete <name>
                Delete the specified space entirely (all snapshots under that space).
                Fails if the space does not exist.
                Fails if the space is current (active).
                Safe by default: does not touch other spaces.
        """;

    public async Task<int> RunAsync(string[] args, CancellationToken ct)
    {
        try
        {
            if (args.Length == 0 || IsHelp(args[0]))
            {
                Console.WriteLine(Summary);
                Console.WriteLine("Use:\n" + Usage);
                return 0;
            }

            var sub = args[0];
            var svc = new SpaceService(new RinnePaths(Environment.CurrentDirectory));

            switch (sub)
            {
                case "list":
                    {
                        if (args.Length != 1)
                        {
                            Console.Error.WriteLine("too many arguments.");
                            Console.WriteLine("Use:\n" + Usage);
                            return 2;
                        }

                        var current = svc.GetCurrent();
                        var spaces = svc.List().OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToArray();

                        if (spaces.Length == 0)
                        {
                            Console.WriteLine("(no spaces)");
                            return 0;
                        }

                        foreach (var s in spaces)
                        {
                            var mark = IsWindows()
                                ? (string.Equals(s, current, StringComparison.OrdinalIgnoreCase) ? "*" : " ")
                                : (s == current ? "*" : " ");
                            Console.WriteLine($"{mark} {s}");
                        }
                        return 0;
                    }

                case "create":
                    {
                        if (args.Length != 2)
                        {
                            Console.Error.WriteLine(args.Length < 2 ? "space name is required." : "too many arguments.");
                            Console.WriteLine("Use:\nrinne space create <name>");
                            return 2;
                        }
                        if (IsOption(args[1])) { UnknownOption(args[1]); return 2; }

                        svc.Create(args[1], ct);
                        Console.WriteLine($"created space '{args[1]}'.");
                        return 0;
                    }

                case "use":
                    {
                        if (args.Length != 2)
                        {
                            Console.Error.WriteLine(args.Length < 2 ? "space name is required." : "too many arguments.");
                            Console.WriteLine("Use:\nrinne space use <name>");
                            return 2;
                        }
                        if (IsOption(args[1])) { UnknownOption(args[1]); return 2; }

                        svc.Use(args[1], ct);
                        Console.WriteLine($"current space = '{svc.GetCurrent()}'.");
                        return 0;
                    }

                case "rename":
                    {
                        if (args.Length != 3)
                        {
                            Console.Error.WriteLine(args.Length < 3 ? "old and new names are required." : "too many arguments.");
                            Console.WriteLine("Use:\nrinne space rename <old> <new>");
                            return 2;
                        }
                        if (IsOption(args[1]) || IsOption(args[2]))
                        {
                            UnknownOption(IsOption(args[1]) ? args[1] : args[2]);
                            return 2;
                        }

                        var oldName = args[1];
                        var newName = args[2];
                        svc.Rename(oldName, newName, ct);
                        Console.WriteLine($"renamed '{oldName}' -> '{newName}'. current space = '{svc.GetCurrent()}'.");
                        return 0;
                    }

                case "current":
                    {
                        if (args.Length != 1)
                        {
                            Console.Error.WriteLine("too many arguments.");
                            Console.WriteLine("Use:\nrinne space current");
                            return 2;
                        }

                        var name = svc.GetCurrentSpaceFromPointer();
                        Console.WriteLine(name);
                        return 0;
                    }

                case "delete":
                    {
                        if (args.Length != 2)
                        {
                            Console.Error.WriteLine(args.Length < 2 ? "space name is required." : "too many arguments.");
                            Console.WriteLine("Use:\nrinne space delete <name>");
                            return 2;
                        }
                        if (IsOption(args[1])) { UnknownOption(args[1]); return 2; }

                        svc.Delete(args[1], ct);
                        Console.WriteLine($"deleted space '{args[1]}'.");
                        return 0;
                    }

                default:
                    Console.Error.WriteLine($"unknown subcommand: {sub}");
                    Console.WriteLine("Use:\n" + Usage);
                    return 2;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 3;
        }
        finally
        {
            await Task.CompletedTask;
        }
    }

    private static bool IsHelp(string s) => s is "-h" or "--help" or "help";
    private static bool IsOption(string s) => s.StartsWith("-", StringComparison.Ordinal);
    private static void UnknownOption(string s)
        => Console.Error.WriteLine($"unknown option: {s}");
    private static bool IsWindows() => OperatingSystem.IsWindows();
}
