using Rinne.Cli.Interfaces.Commands;
using System.Reflection;

namespace Rinne.Cli.Commands
{
    /// <summary>
    /// Rinne CLI のバージョン情報を表示するコマンド。
    /// </summary>
    public sealed class VersionCommand : ICliCommand
    {
        /// <summary>コマンド名。</summary>
        public const string CommandName = "version";

        /// <inheritdoc/>
        public bool CanHandle(string[] args)
        {
            if (args is not { Length: > 0 })
                return false;

            return string.Equals(args[0], CommandName, StringComparison.OrdinalIgnoreCase);
        }

        /// <inheritdoc/>
        public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
        {
            try
            {
                // 引数がある場合の処理
                if (args.Length > 1)
                {
                    var opt = args[1];

                    if (opt is "-h" or "--help")
                    {
                        PrintHelp();
                        return 0;
                    }

                    // 不明なオプションはエラー扱い
                    if (opt.StartsWith('-'))
                    {
                        Console.Error.WriteLine($"[{CommandName}] 失敗: 不明なオプション '{opt}'");
                        return 1;
                    }
                }

                var asm = Assembly.GetExecutingAssembly();
                var infoVer = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
                var asmVer = asm.GetName().Version?.ToString();
                var version = infoVer ?? asmVer ?? "unknown";

                Console.WriteLine($"Rinne CLI {version}");
                return await Task.FromResult(0);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[{CommandName}] 失敗: {ex.Message}");
                return 1;
            }
        }

        /// <inheritdoc/>
        public void PrintHelp()
        {
            Console.WriteLine("""
            usage:
              rinne version
              rinne version -h | --help

            description:
              Rinne CLI のバージョン情報を表示します。
            """);
        }
    }
}
