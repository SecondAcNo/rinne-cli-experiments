using Rinne.Cli.Interfaces.Commands;
using Rinne.Cli.Interfaces.Services;
using System;

namespace Rinne.Cli.Commands
{
    /// <summary>
    /// Rinneのセーブメタ情報（meta.json）を整形表示するコマンド。
    /// </summary>
    public sealed class ShowCommand : ICliCommand
    {
        /// <summary>コマンド名。</summary>
        public const string CommandName = "show";

        /// <summary>メタ情報表示サービス。</summary>
        private readonly IShowService _show;

        /// <summary>
        /// コンストラクタ。表示サービスを注入する。
        /// </summary>
        public ShowCommand(IShowService show)
        {
            _show = show ?? throw new ArgumentNullException(nameof(show));
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

            // 厳格バリデーション：
            // 受理する形は 1) show  2) show <id>  3) show <id> <space>
            if (args.Length > 3)
            {
                Console.Error.WriteLine($"[{CommandName}] 失敗: 余分な引数があります。");
                PrintHelp();
                return 1;
            }

            // オプション（-で始まるトークン）は -h/--help 以外すべてエラー
            if (args.Length >= 2 && IsUnknownOption(args[1]))
            {
                Console.Error.WriteLine($"[{CommandName}] 失敗: 不明なオプション '{args[1]}'");
                return 1;
            }
            if (args.Length == 3 && IsUnknownOption(args[2]))
            {
                Console.Error.WriteLine($"[{CommandName}] 失敗: 不明なオプション '{args[2]}'");
                return 1;
            }

            string? id = args.Length >= 2 ? args[1] : null;
            string? space = args.Length == 3 ? args[2] : null;

            try
            {
                var repoRoot = Directory.GetCurrentDirectory();
                var result = await _show.GetFormattedMetaAsync(repoRoot, id, space, cancellationToken);

                if (!string.IsNullOrEmpty(result.FormattedJson))
                {
                    Console.WriteLine(result.FormattedJson);
                }
                else if (!string.IsNullOrEmpty(result.ErrorMessage))
                {
                    Console.Error.WriteLine(result.ErrorMessage);
                }

                return result.ExitCode;
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

        /// <inheritdoc/>
        public void PrintHelp()
        {
            Console.WriteLine($"""
                usage:
                  rinne {CommandName}
                  rinne {CommandName} <id>
                  rinne {CommandName} <id> <space>
                  rinne {CommandName} -h | --help

                description:
                  指定されたセーブの meta.json を整形表示します。
                  id      表示するセーブID。省略時は最新のものを使用。
                  space   対象スペース。省略時は current を参照。

                examples:
                  rinne {CommandName}
                  rinne {CommandName} 00000009_20251026T010732000Z
                  rinne {CommandName} 00000009_20251026T010732000Z test
                """);
        }

        /// <summary>未知オプション検出（- で始まり、許可済み以外）。</summary>
        private static bool IsUnknownOption(string token)
            => token.StartsWith("-", StringComparison.Ordinal) && token is not "-h" and not "--help";
    }
}
