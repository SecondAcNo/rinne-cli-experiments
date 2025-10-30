using Rinne.Cli.Interfaces.Commands;
using Rinne.Cli.Interfaces.Services;
using Rinne.Cli.Models;

namespace Rinne.Cli.Commands
{
    /// <summary>
    /// Rinneのセーブ履歴（ZIP一覧）を表示するコマンド。
    /// </summary>
    public sealed class LogCommand : ICliCommand
    {
        /// <summary>コマンド名。</summary>
        public const string CommandName = "log";

        /// <summary>ログ列挙サービス。</summary>
        private readonly ILogService _logService;

        /// <summary>
        /// コンストラクタ。ログ列挙サービスを注入する。
        /// </summary>
        /// <param name="logService">履歴の取得を行うサービス。</param>
        /// <exception cref="ArgumentNullException">logServiceがnullの場合。</exception>
        public LogCommand(ILogService logService)
        {
            _logService = logService ?? throw new ArgumentNullException(nameof(logService));
        }

        /// <inheritdoc/>
        public bool CanHandle(string[] args)
            => args.Length > 0 && string.Equals(args[0], CommandName, StringComparison.OrdinalIgnoreCase);

        /// <inheritdoc/>
        public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
        {
            // help（-h / --help のみ。混在不可）
            if (args.Length == 2 && (args[1] is "-h" or "--help"))
            {
                PrintHelp();
                return 0;
            }

            // 受理形は `log` または `log <space>` のみ
            if (args.Length > 2)
            {
                Console.Error.WriteLine($"[{CommandName}] 失敗: 余分な引数があります。");
                PrintHelp();
                return 1;
            }

            // 未知オプション（-で始まるトークン）はエラー
            if (args.Length == 2 && args[1].StartsWith("-", StringComparison.Ordinal))
            {
                Console.Error.WriteLine($"[{CommandName}] 失敗: 不明なオプション '{args[1]}'");
                PrintHelp();
                return 1;
            }

            try
            {
                var layout = new RepositoryLayout(Directory.GetCurrentDirectory());
                string? space = args.Length == 2 ? args[1] : null;

                var result = await _logService.GetLogAsync(layout, space, cancellationToken).ConfigureAwait(false);

                Console.WriteLine($"[{CommandName}] space: {result.Space}");
                Console.WriteLine($"{"Time",-20} {"Size(KB)",10} {"File",-40}");
                Console.WriteLine(new string('-', 80));

                if (result.Entries.Count == 0)
                {
                    Console.WriteLine($"[info] スペース '{result.Space}' にセーブ履歴はありません。");
                    return 0;
                }

                foreach (var e in result.Entries)
                {
                    string time = e.LastWriteTimeLocal.ToString("yyyy-MM-dd HH:mm:ss");
                    string size = (e.LengthBytes / 1024.0).ToString("N1");
                    Console.WriteLine($"{time,-20} {size,10} {e.FileName,-40}");
                }

                return 0;
            }
            catch (InvalidOperationException iox)
            {
                Console.Error.WriteLine($"[error] {iox.Message}");
                return 1;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[error] {CommandName} コマンドの実行中に例外が発生しました: {ex.Message}");
                return 1;
            }
        }

        /// <inheritdoc/>
        public void PrintHelp()
        {
            Console.WriteLine($"""
                usage:
                  rinne {CommandName}
                  rinne {CommandName} <space>
                  rinne {CommandName} -h | --help

                description:
                  指定された space のセーブ履歴（ZIP 一覧）を表示します。
                  space を省略した場合は current を参照します。
                """);
        }
    }
}
