using Rinne.Cli.Interfaces.Commands;
using Rinne.Cli.Interfaces.Services;

namespace Rinne.Cli.Commands
{
    /// <summary>
    /// 現在のワーキングツリーを Space（空間）にセーブするコマンド。
    /// </summary>
    public sealed class SaveCommand : ICliCommand
    {
        /// <summary>コマンド名。</summary>
        public const string CommandName = "save";

        /// <summary>セーブ処理サービス。</summary>
        private readonly ISaveService _saveService;

        /// <summary>
        /// コンストラクタ。セーブサービスを注入する。
        /// </summary>
        /// <param name="saveService">ZIP作成・採番・メタ出力等を行うサービス。</param>
        /// <exception cref="ArgumentNullException"><paramref name="saveService"/> が null の場合。</exception>
        public SaveCommand(ISaveService saveService)
            => _saveService = saveService ?? throw new ArgumentNullException(nameof(saveService));

        /// <inheritdoc/>
        public bool CanHandle(string[] args)
            => args is { Length: > 0 } && args[0].Equals(CommandName, StringComparison.OrdinalIgnoreCase);

        /// <inheritdoc/>
        public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
        {
            // help（-h / --help のみ。混在不可）
            if (args.Length == 2 && (args[1] is "-h" or "--help"))
            {
                PrintHelp();
                return 0;
            }

            try
            {
                // ===== 厳格パース =====
                // 受理オプション:
                //   -s <name> | --space <name> | --space=<name>
                //   -m <text> | --message <text> | --message=<text>
                //   -h | --help（単独時のみ）
                string? space = null;
                string? message = null;
                bool seenSpace = false;
                bool seenMessage = false;

                for (int i = 1; i < args.Length; i++)
                {
                    var a = args[i];

                    // 位置引数は非対応（すべてオプションで指定）
                    if (!a.StartsWith("-", StringComparison.Ordinal))
                    {
                        Console.Error.WriteLine($"[{CommandName}] 失敗: 位置引数 '{a}' は受け付けません。usage: rinne {CommandName} [-s|--space <name>] [-m|--message <text>]");
                        return 1;
                    }

                    // help が他と混在していたらエラー
                    if (a is "-h" or "--help")
                    {
                        Console.Error.WriteLine($"[{CommandName}] 失敗: -h/--help は単独で使用してください。");
                        return 1;
                    }

                    // --space=NAME
                    if (a.StartsWith("--space=", StringComparison.OrdinalIgnoreCase))
                    {
                        if (seenSpace) { Console.Error.WriteLine($"[{CommandName}] 失敗: --space が重複しています。"); return 1; }
                        space = a.Substring("--space=".Length).Trim();
                        if (string.IsNullOrWhiteSpace(space))
                        {
                            Console.Error.WriteLine($"[{CommandName}] 失敗: --space に空文字は指定できません。");
                            return 1;
                        }
                        seenSpace = true;
                        continue;
                    }

                    // --space NAME  /  -s NAME
                    if (a.Equals("--space", StringComparison.OrdinalIgnoreCase) || a.Equals("-s", StringComparison.OrdinalIgnoreCase))
                    {
                        if (seenSpace) { Console.Error.WriteLine($"[{CommandName}] 失敗: --space/-s が重複しています。"); return 1; }
                        if (i + 1 >= args.Length)
                        {
                            Console.Error.WriteLine($"[{CommandName}] 失敗: {a} に値が指定されていません。");
                            return 1;
                        }
                        var val = args[++i].Trim();
                        if (string.IsNullOrWhiteSpace(val))
                        {
                            Console.Error.WriteLine($"[{CommandName}] 失敗: --space に空文字は指定できません。");
                            return 1;
                        }
                        space = val;
                        seenSpace = true;
                        continue;
                    }

                    // --message=TEXT
                    if (a.StartsWith("--message=", StringComparison.OrdinalIgnoreCase))
                    {
                        if (seenMessage) { Console.Error.WriteLine($"[{CommandName}] 失敗: --message が重複しています。"); return 1; }
                        message = a.Substring("--message=".Length).Trim().Trim('"');
                        // message は空文字でも可（仕様：省略時は空にするのと同等）
                        seenMessage = true;
                        continue;
                    }

                    // --message TEXT  /  -m TEXT
                    if (a.Equals("--message", StringComparison.OrdinalIgnoreCase) || a.Equals("-m", StringComparison.OrdinalIgnoreCase))
                    {
                        if (seenMessage) { Console.Error.WriteLine($"[{CommandName}] 失敗: --message/-m が重複しています。"); return 1; }
                        if (i + 1 >= args.Length)
                        {
                            Console.Error.WriteLine($"[{CommandName}] 失敗: {a} に値が指定されていません。");
                            return 1;
                        }
                        // メッセージは先頭 '-' でも許容（例: -m "-fix bug"）
                        message = args[++i].Trim().Trim('"');
                        seenMessage = true;
                        continue;
                    }

                    // ここまで来たら未知オプション
                    Console.Error.WriteLine($"[{CommandName}] 失敗: 不明なオプション '{a}'");
                    return 1;
                }
                // ===== パースここまで =====

                var repoRoot = Directory.GetCurrentDirectory();

                var result = await _saveService.SaveAsync(repoRoot, space, message, cancellationToken);

                Console.WriteLine($"[{CommandName}] Saved: {result.Id} (space: {result.Space})");
                Console.WriteLine($"[zip]  {result.ZipPath}");
                Console.WriteLine($"[meta] {result.MetaPath}");
                return 0;
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
                  rinne {CommandName} [-s|--space <name>] [-m|--message <text>]
                  rinne {CommandName} -h | --help

                description:
                  現在のディレクトリを .rinne/data/<space>/ に ZIP スナップショット保存し、
                  メタデータ (.rinne/data/<space>/meta/<id>.json) も出力します。
                  --space/-s を省略した場合は current のスペース、なければ 'main' を使用します。
                  --message/-m を省略した場合は空メッセージとして記録します。

                examples:
                  rinne {CommandName}
                  rinne {CommandName} -s work
                  rinne {CommandName} --space=work --message "refactor: extract SaveService"
                """);
        }
    }
}
