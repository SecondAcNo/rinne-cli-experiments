using Rinne.Cli.Interfaces.Commands;
using Rinne.Cli.Interfaces.Services;

namespace Rinne.Cli.Commands
{
    /// <summary>
    /// 最新履歴（1件）を削除するコマンド。
    /// </summary>
    /// <remarks>
    /// zip と meta をペアで削除します。中抜きは行いません。
    /// 通常の使用は想定されていないサブコマンドのような位置付けです。
    /// </remarks>
    public sealed class DropLastCommand : ICliCommand
    {
        /// <summary>コマンド名。</summary>
        public const string CommandName = "drop-last";

        private readonly IDropLastService _service;

        public DropLastCommand(IDropLastService service)
            => _service = service ?? throw new ArgumentNullException(nameof(service));

        /// <inheritdoc/>
        public bool CanHandle(string[] args)
            => args.Length > 0 && string.Equals(args[0], CommandName, StringComparison.OrdinalIgnoreCase);

        /// <inheritdoc/>
        /// <param name="args">構文: drop-last [&lt;space&gt;] [--yes]</param>
        /// <param name="cancellationToken">キャンセルトークン。</param>
        public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
        {
            // help（-h / --help のみ。混在不可）
            if (args.Length == 2 && (args[1] is "-h" or "--help"))
            {
                PrintHelp();
                return 0;
            }

            // 厳格パース：許可は <space>(任意) と --yes のみ
            string? space = null;
            bool yes = false;

            for (int i = 1; i < args.Length; i++)
            {
                var tok = args[i];

                if (tok.StartsWith("-", StringComparison.Ordinal))
                {
                    if (tok.Equals("--yes", StringComparison.OrdinalIgnoreCase))
                    {
                        if (yes)
                        {
                            Console.Error.WriteLine($"[{CommandName}] 失敗: '--yes' が重複しています。");
                            return 2;
                        }
                        yes = true;
                        continue;
                    }

                    // -h/--help が他引数と混在している場合もエラー扱い
                    if (tok is "-h" or "--help")
                    {
                        Console.Error.WriteLine($"[{CommandName}] 失敗: -h/--help は単独で使用してください。");
                        return 2;
                    }

                    Console.Error.WriteLine($"[{CommandName}] 失敗: 不明なオプション '{tok}'");
                    return 2;
                }
                else
                {
                    if (space is not null)
                    {
                        Console.Error.WriteLine($"[{CommandName}] 失敗: 余分な引数 '{tok}'");
                        return 2;
                    }
                    space = tok;
                }
            }

            // 対話確認
            if (!yes)
            {
                Console.Write("本当に最新履歴を削除しますか？（y/N）: ");
                var ans = Console.ReadLine()?.Trim().ToLowerInvariant();
                if (ans is not ("y" or "yes"))
                {
                    Console.WriteLine("[drop-last] 中止しました。");
                    return 1;
                }
            }

            try
            {
                var cwd = Environment.CurrentDirectory;
                var result = await _service.DropLastAsync(cwd, space, confirmed: true, cancellationToken).ConfigureAwait(false);

                if (result.ExitCode != 0)
                {
                    if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
                        Console.Error.WriteLine($"[drop-last] 失敗: {result.ErrorMessage}");
                    return result.ExitCode;
                }

                Console.WriteLine($"[drop-last] space      : {result.Space}");
                Console.WriteLine($"[drop-last] deleted id : {result.DeletedId ?? "(none)"}");
                Console.WriteLine("[drop-last] 完了しました。");
                return 0;
            }
            catch (OperationCanceledException)
            {
                Console.Error.WriteLine("[drop-last] キャンセルされました。");
                return 130; // Ctrl+C 相当
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[drop-last] 例外: {ex.Message}");
                return 1;
            }
        }

        /// <summary>使い方表示。</summary>
        public void PrintHelp()
        {
            Console.WriteLine($"""
                usage:
                  rinne {CommandName}
                  rinne {CommandName} <space>
                  rinne {CommandName} --yes
                  rinne {CommandName} <space> --yes
                  rinne {CommandName} -h | --help

                description:
                  指定スペースの“最新1件のみ”を zip/meta のペアで削除します。
                  <space> を省略した場合は .rinne/current（なければ 'main'）を参照します。

                options:
                  --yes   確認プロンプトをスキップ
                """);
        }
    }
}
