using Rinne.Cli.Commands;

var cts = new CancellationTokenSource();
Console.CancelKeyPress += (s, e) => { e.Cancel = true; cts.Cancel(); };

CommandRunner.Register(
    new SaveCommand(),
    new InitCommand(),
    new SpaceCommand(),
    new CompactCommand(),
    new HydrateCommand(),
    new RestoreCommand(),
    new TidyCommand(),
    new HistoryCommand(),
    new RecomposeCommand(),
    new ImportCommand(),
    new VerifyCommand(),
    new NoteCommand(),
    new DiffCommand(),
    new TextDiffCommand(),
    new ExportCommand(),
    new PickCommand(),
    new CacheMetaGcCommand()
);

return await CommandRunner.RunAsync(args, cts.Token);
