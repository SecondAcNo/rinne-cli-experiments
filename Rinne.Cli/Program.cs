using Microsoft.Extensions.DependencyInjection;
using Rinne.Cli.DI;
using Rinne.Cli.Interfaces.Commands;
using Rinne.Cli.Models;
using Rinne.Cli.Utility;
using System.Text;
using System.Text.Json;

namespace Rinne.Cli
{
    internal static class Program
    {
        private static async Task<int> Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            try
            {
                var layout = new RepositoryLayout(Directory.GetCurrentDirectory());
                var cfgPath = layout.LogOutputPath;

                if (File.Exists(cfgPath))
                {
                    var cfg = JsonSerializer.Deserialize<LogOutputConfig>(await File.ReadAllTextAsync(cfgPath));

                    if (cfg?.Enabled == true)
                    {
                        if (string.IsNullOrWhiteSpace(cfg.Path))
                        {
                            Console.Error.WriteLine("[warn] log-output enabled but path is empty. Please set a valid path in log-output.json.");
                        }
                        else
                        {
                            var logPath = Path.IsPathRooted(cfg.Path)
                                ? cfg.Path
                                : Path.GetFullPath(Path.Combine(layout.RepoRoot, cfg.Path));

                            var dualOut = new DualWriter(logPath);
                            Console.SetOut(dualOut);
                            Console.SetError(new DualWriter(logPath));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[warn] failed to initialize log-output: {ex.Message}");
            }

            var services = new ServiceCollection()
                .AddCliServices()
                .BuildServiceProvider();

            var commands = services.GetServices<ICliCommand>().ToList();

            if (args.Length == 0)
            {
                Console.WriteLine("使用可能なコマンド:");
                foreach (var c in commands)
                {
                    var name = c.GetType().Name.Replace("Command", "").ToLowerInvariant();
                    Console.WriteLine($"  rinne {name}");
                }
                return 0;
            }

            var handler = commands.FirstOrDefault(c => c.CanHandle(args));

            if (handler is null)
            {
                Console.Error.WriteLine($"不明なコマンドです: {args[0]}");
                return 1;
            }

            try
            {
                return await handler.RunAsync(args, CancellationToken.None);
            }
            catch (OperationCanceledException)
            {
                Console.Error.WriteLine("キャンセルされました。");
                return 130;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("[致命的エラー] " + ex.Message);
                return 99;
            }
        }
    }
}
