using Rinne.Cli.Interfaces.Commands;
using Rinne.Cli.Interfaces.Services;

namespace Rinne.Cli.Commands
{
    /// <summary>
    /// Rinneリポジトリの標準フォルダ構造を作成するコマンド。
    /// </summary>
    public sealed class InitCommand : ICliCommand
    {
        /// <summary>コマンド名。</summary>
        public const string CommandName = "init";

        /// <summary>初期化処理サービス。</summary>
        private readonly IInitService _initService;

        public InitCommand(IInitService initService)
            => _initService = initService ?? throw new ArgumentNullException(nameof(initService));

        /// <inheritdoc/>
        public bool CanHandle(string[] args)
            => args is { Length: > 0 } && string.Equals(args[0], CommandName, StringComparison.OrdinalIgnoreCase);

        /// <inheritdoc/>
        public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
        {
            // help（-h / --help のみ。混在不可）
            if (args.Length == 2 && (args[1] is "-h" or "--help"))
            {
                PrintHelp();
                return 0;
            }

            // 受理形は `init` のみ（追加引数はエラー）
            if (args.Length > 1)
            {
                // 未知オプションかどうかをメッセージで示す
                if (args[1].StartsWith("-", StringComparison.Ordinal))
                    Console.Error.WriteLine($"[{CommandName}] 失敗: 不明なオプション '{args[1]}'");
                else
                    Console.Error.WriteLine($"[{CommandName}] 失敗: 余分な引数があります。");

                PrintHelp();
                return 1;
            }

            try
            {
                var root = Directory.GetCurrentDirectory();
                var created = await _initService.InitializeAsync(root, cancellationToken);

                Console.WriteLine(created
                    ? $"[ok] Rinneリポジトリを初期化しました: {Path.Combine(root, ".rinne")}"
                    : "[info] すでに初期化済みです。");

                return 0;
            }
            catch (OperationCanceledException)
            {
                Console.Error.WriteLine("[warn] Canceled.");
                return 130;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[error] {CommandName} 失敗: {ex.Message}");
                return 1;
            }
        }

        /// <inheritdoc/>
        public void PrintHelp()
        {
            Console.WriteLine($"""
                usage:
                  rinne {CommandName}
                  rinne {CommandName} -h | --help

                description:
                  現在のディレクトリに .rinne/ 標準フォルダ構造を作成します。
                  既に存在する場合は何も行いません。
                """);
        }
    }
}
