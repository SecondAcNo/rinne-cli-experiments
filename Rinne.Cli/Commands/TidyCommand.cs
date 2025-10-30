using Rinne.Cli.Interfaces.Commands;
using Rinne.Cli.Interfaces.Services;
using Rinne.Cli.Models;

namespace Rinne.Cli.Commands
{
    /// <summary>
    /// 古いスナップショットを整理（最新 N 件だけ残す）し、meta のチェーンを整えるコマンド。
    /// </summary>
    public sealed class TidyCommand : ICliCommand
    {
        /// <summary>コマンド名。</summary>
        public const string CommandName = "tidy";

        private readonly ITidyService _service;

        public TidyCommand(ITidyService service)
            => _service = service ?? throw new ArgumentNullException(nameof(service));

        /// <inheritdoc/>
        public bool CanHandle(string[] args)
            => args is { Length: > 0 } && string.Equals(args[0], CommandName, StringComparison.OrdinalIgnoreCase);

        /// <inheritdoc/>
        public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
        {
            // help（-h / --help のみ）
            if (args.Length >= 2 && (args[1] is "-h" or "--help"))
            {
                PrintHelp();
                return 0;
            }

            if (args.Length <= 1)
            {
                Console.Error.WriteLine($"[{CommandName}] 失敗: 引数が不足しています。");
                PrintHelp();
                return 1;
            }

            try
            {
                var after = args.Skip(1).ToArray();

                bool all = false;
                string? space = null;
                int keep;

                // 受理する構文:
                //   tidy [space] <keepCount>
                //   tidy --all <keepCount>
                //   tidy -h | --help
                if (after[0].Equals("--all", StringComparison.OrdinalIgnoreCase))
                {
                    all = true;

                    // 形式: --all <keepCount>（ちょうど2引数）
                    if (after.Length != 2)
                    {
                        Console.Error.WriteLine($"[{CommandName}] 失敗: 構文が不正です。usage: {CommandName} --all <keepCount>");
                        return 1;
                    }
                    if (!TryParseKeep(after[1], out keep))
                    {
                        Console.Error.WriteLine($"[{CommandName}] 失敗: <keepCount> は 1 以上の整数で指定してください。");
                        return 1;
                    }
                }
                else
                {
                    // 形式は 1) <keepCount> または 2) <space> <keepCount>
                    if (after.Length == 1)
                    {
                        // tidy N
                        if (IsUnknownOption(after[0]))
                        {
                            Console.Error.WriteLine($"[{CommandName}] 失敗: 不明なオプション '{after[0]}'");
                            return 1;
                        }
                        if (!TryParseKeep(after[0], out keep))
                        {
                            Console.Error.WriteLine($"[{CommandName}] 失敗: <keepCount> は 1 以上の整数で指定してください。");
                            return 1;
                        }
                    }
                    else if (after.Length == 2)
                    {
                        // tidy <space> N
                        if (IsUnknownOption(after[0]))
                        {
                            Console.Error.WriteLine($"[{CommandName}] 失敗: 不明なオプション '{after[0]}'");
                            return 1;
                        }
                        space = after[0];
                        if (!TryParseKeep(after[1], out keep))
                        {
                            Console.Error.WriteLine($"[{CommandName}] 失敗: <keepCount> は 1 以上の整数で指定してください。");
                            return 1;
                        }
                    }
                    else
                    {
                        // 余分な引数はエラー
                        Console.Error.WriteLine($"[{CommandName}] 失敗: 余分な引数があります: '{string.Join(' ', after.Skip(2))}'");
                        return 1;
                    }
                }

                var options = new TidyOptions(
                    AllSpaces: all,
                    Space: space,
                    KeepCount: keep
                );

                var root = Environment.CurrentDirectory;
                return await _service.RunAsync(root, options, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Console.Error.WriteLine($"[{CommandName}] canceled.");
                return 130; // Ctrl+C 相当
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[{CommandName}] error: {ex.Message}");
                return 2;
            }
        }

        /// <inheritdoc/>
        public void PrintHelp()
        {
            Console.WriteLine($"""
                usage:
                  rinne {CommandName} [space] <keepCount>
                  rinne {CommandName} --all <keepCount>
                  rinne {CommandName} -h | --help

                description:
                  - .rinne をリポジトリ直下へバックアップした後、
                    指定 space（省略時は current）または全 space の最新 <keepCount> 件を残して古い履歴を削除し、
                    残存 meta のチェーンハッシュ（prev/this）を再計算して整えます。
                  - ID / ZIP 名は変更しません（外部参照を壊しません）。
                  - 実行前に log-output は一旦 off にしてください。

                examples:
                  rinne {CommandName} 20
                  rinne {CommandName} work 30
                  rinne {CommandName} --all 50
                """);
        }

        /// <summary>keepカウントの解析（1以上の整数）。</summary>
        private static bool TryParseKeep(string s, out int keep)
            => int.TryParse(s, out keep) && keep > 0;

        /// <summary>未知オプション検出（- で始まり、許可済み以外）。</summary>
        private static bool IsUnknownOption(string token)
        {
            if (!token.StartsWith('-')) return false;
            // 許可済み: --all, -h, --help
            return !(token.Equals("--all", StringComparison.OrdinalIgnoreCase)
                     || token.Equals("-h", StringComparison.OrdinalIgnoreCase)
                     || token.Equals("--help", StringComparison.OrdinalIgnoreCase));
        }
    }
}
