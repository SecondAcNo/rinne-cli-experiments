namespace Rinne.Cli.Commands.Interfaces;

public interface ICliCommand
{
    string Name { get; }
    IEnumerable<string> Aliases { get; }
    string Summary { get; }

    string Usage => $"rinne {Name}";

    Task<int> RunAsync(string[] args, CancellationToken ct);
}
