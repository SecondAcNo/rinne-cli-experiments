using Rinne.Cli.Interfaces.Commands;
using Rinne.Cli.Models;
using System.Text.Json;

namespace Rinne.Cli.Commands
{
    /// <summary>
    /// ファイルログ出力を on / off / clean で制御する CLI コマンド。
    /// clean は現在 off のときのみ実行可能。
    /// </summary>
    public sealed class LogOutputCommand : ICliCommand
    {
        private const string CommandName = "log-output";
        private const string LogFileName = "rinne.log";

        public bool CanHandle(string[] args)
            => args.Length > 0 && string.Equals(args[0], CommandName, StringComparison.OrdinalIgnoreCase);

        public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
        {
            // help（-h / --help のみ。混在不可）
            if (args.Length == 2 && (args[1] is "-h" or "--help"))
            {
                PrintHelp();
                return 0;
            }

            // 受理するのはサブコマンド1個のみ（on|off|clean）
            if (args.Length != 2)
            {
                Console.Error.WriteLine($"[{CommandName}] 失敗: 構文が不正です。usage: rinne {CommandName} <on|off|clean>");
                PrintHelp();
                return 1;
            }

            var sub = args[1].ToLowerInvariant();
            if (sub is not ("on" or "off" or "clean"))
            {
                Console.Error.WriteLine($"[{CommandName}] 失敗: 不明なサブコマンド '{args[1]}'");
                PrintHelp();
                return 1;
            }

            try
            {
                var layout = new RepositoryLayout(Directory.GetCurrentDirectory());
                Directory.CreateDirectory(layout.ConfigDir);

                var cfgPath = layout.LogOutputPath;

                // 設定ロード
                var config = await LoadConfigAsync(cfgPath, cancellationToken);

                switch (sub)
                {
                    case "on":
                        if (config.Enabled)
                        {
                            Console.WriteLine("[ok] log-output already enabled");
                            return 0;
                        }
                        config.Enabled = true;
                        await SaveConfigAsync(cfgPath, config, cancellationToken);
                        Console.WriteLine("[ok] log-output enabled");
                        return 0;

                    case "off":
                        if (!config.Enabled)
                        {
                            Console.WriteLine("[ok] log-output already disabled");
                            return 0;
                        }
                        config.Enabled = false;
                        await SaveConfigAsync(cfgPath, config, cancellationToken);
                        Console.WriteLine("[ok] log-output disabled");
                        return 0;

                    case "clean":
                        if (config.Enabled)
                        {
                            Console.Error.WriteLine("[warn] cannot clean while log-output is enabled. Run 'rinne log-output off' first.");
                            return 1;
                        }
                        try
                        {
                            // 実ログパス解決（空なら既定 .rinne/logs/rinne.log）
                            var resolved = string.IsNullOrWhiteSpace(config.Path)
                                ? Path.Combine(layout.LogsDir, LogFileName)
                                : (Path.IsPathRooted(config.Path) ? config.Path
                                   : Path.GetFullPath(Path.Combine(layout.RepoRoot, config.Path)));

                            var dir = Path.GetDirectoryName(resolved);
                            if (!string.IsNullOrEmpty(dir))
                                Directory.CreateDirectory(dir);

                            // 共有可で開いてトランケート（DualWriter 側も FileShare.ReadWrite を使用）
                            using (var fs = new FileStream(
                                resolved,
                                FileMode.OpenOrCreate,
                                FileAccess.Write,
                                FileShare.ReadWrite))
                            {
                                fs.SetLength(0);
                                fs.Flush(true);
                            }

                            Console.WriteLine($"[ok] log file cleared: {resolved}");
                            return 0;
                        }
                        catch (Exception ex)
                        {
                            Console.Error.WriteLine($"[warn] failed to clear log: {ex.Message}");
                            return 1;
                        }
                }

                // ここには来ない
                return 1;
            }
            catch (OperationCanceledException)
            {
                Console.Error.WriteLine($"[{CommandName}] canceled.");
                return 130;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[{CommandName}] 失敗: {ex.Message}");
                return 1;
            }
        }

        public void PrintHelp()
        {
            Console.WriteLine($"""
                usage:
                  rinne {CommandName} <on|off|clean>
                  rinne {CommandName} -h | --help

                description:
                  on     - Enable file log output (console + file).
                  off    - Disable file log output (console only).
                  clean  - Clear the log file content (only allowed when off).
                """);
        }

        private static async Task<LogOutputConfig> LoadConfigAsync(string path, CancellationToken token)
        {
            if (!File.Exists(path)) return new LogOutputConfig();
            try
            {
                var json = await File.ReadAllTextAsync(path, token);
                return JsonSerializer.Deserialize<LogOutputConfig>(json) ?? new LogOutputConfig();
            }
            catch
            {
                return new LogOutputConfig();
            }
        }

        private static Task SaveConfigAsync(string path, LogOutputConfig cfg, CancellationToken token)
        {
            var json = JsonSerializer.Serialize(cfg, new JsonSerializerOptions { WriteIndented = true });
            return File.WriteAllTextAsync(path, json, token);
        }
    }
}
