using Rinne.Cli.Commands.Interfaces;
using Rinne.Core.Features.Init;

namespace Rinne.Cli.Commands;

public sealed class InitCommand : ICliCommand
{
    public string Name => "init";
    public IEnumerable<string> Aliases => Array.Empty<string>();
    public string Summary => "Create the .rinne standard folder layout with default 'main' space (fails if it already exists).";
    public string Usage => """
        Usage:
            rinne init

        Description:
            Initialize a new .rinne repository under the current directory.
        """;

    public Task<int> RunAsync(string[] args, CancellationToken ct)
    {
        if (args.Length > 0)
        {
            Console.Error.WriteLine("this command takes no arguments.");
            Console.WriteLine("Use: " + Usage);
            return Task.FromResult(2);
        }

        var result = InitLayout.Ensure(Environment.CurrentDirectory, new InitLayoutOptions(Space: "main"));

        Console.WriteLine($".rinne initialized at: {result.RinneRoot}");
        if (result.CreatedDirectories.Count > 0)
            Console.WriteLine("dirs:  " + string.Join(", ", result.CreatedDirectories));
        if (result.CreatedFiles.Count > 0)
            Console.WriteLine("files: " + string.Join(", ", result.CreatedFiles));
        if (result.HasWarnings)
            Console.Error.WriteLine("warn: " + string.Join(" | ", result.Warnings));

        return Task.FromResult(0);
    }
}
