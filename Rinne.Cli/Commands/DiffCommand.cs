using Rinne.Cli.Interfaces.Commands;
using Rinne.Cli.Interfaces.Services;
using Rinne.Cli.Interfaces.Utility;

namespace Rinne.Cli.Commands
{
    /// <summary>
    /// 2つのRinneアーカイブ（ZIP）の内容差分を表示するコマンド。
    /// </summary>
    public sealed class DiffCommand : ICliCommand
    {
        /// <summary>コマンド名。</summary>
        public const string CommandName = "diff";

        private readonly IArchiveDiffService _service;
        private readonly IConsoleDiffFormatter _formatter;

        public DiffCommand(IArchiveDiffService service, IConsoleDiffFormatter formatter)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _formatter = formatter ?? throw new ArgumentNullException(nameof(formatter));
        }

        /// <inheritdoc/>
        public bool CanHandle(string[] args)
            => args is { Length: >= 1 } &&
               string.Equals(args[0], CommandName, StringComparison.OrdinalIgnoreCase);

        /// <inheritdoc/>
        /// <returns>0=差分なし、1=差分あり、2=入力/処理エラー、130=キャンセル</returns>
        public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
        {
            // help（-h / --help のみ。混在不可）
            if (args.Length == 2 && (args[1] is "-h" or "--help"))
            {
                PrintHelp();
                return 0;
            }

            // 受理形は: 1) diff  2) diff <id1> <id2> [space]
            if (args.Length is 2 or > 4)
            {
                // args.Length == 2 は「位置引数が1個だけ」の中途半端ケース
                Console.Error.WriteLine($"[{CommandName}] 失敗: 構文が不正です。");
                PrintHelp();
                return 2;
            }

            string? id1 = null, id2 = null, space = null;

            if (args.Length >= 3)
            {
                // 未知オプション防止（id/space に '-' 始まりは禁止）
                if (args[1].StartsWith("-", StringComparison.Ordinal) ||
                    args[2].StartsWith("-", StringComparison.Ordinal) ||
                    (args.Length == 4 && args[3].StartsWith("-", StringComparison.Ordinal)))
                {
                    var bad = args.Skip(1).First(a => a.StartsWith("-", StringComparison.Ordinal));
                    Console.Error.WriteLine($"[{CommandName}] 失敗: 不明なオプション '{bad}'");
                    return 2;
                }

                id1 = args[1];
                id2 = args[2];
                if (args.Length == 4) space = args[3];
            }
            // else: 引数なし → 自動選択

            try
            {
                var outcome = await _service.DiffAsync(
                    repoRoot: Directory.GetCurrentDirectory(),
                    id1: id1,
                    id2: id2,
                    space: space,
                    cancellationToken).ConfigureAwait(false);

                // 自動選択時の案内
                if (id1 is null && id2 is null)
                {
                    Console.WriteLine($"[info] Auto-selected latest pair in space='{outcome.Space}':");
                    Console.WriteLine($"       id1={outcome.Id1}.zip  (older)");
                    Console.WriteLine($"       id2={outcome.Id2}.zip  (newer)");
                }

                _formatter.Print(outcome);

                var hasDiff = outcome.Result.Added.Count
                            + outcome.Result.Removed.Count
                            + outcome.Result.Modified.Count > 0;

                return hasDiff ? 1 : 0;
            }
            catch (OperationCanceledException)
            {
                Console.Error.WriteLine("[warn] Canceled.");
                return 130;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[error] {ex.Message}");
                return 2;
            }
        }

        /// <inheritdoc/>
        public void PrintHelp()
        {
            Console.WriteLine($"""
                usage:
                  rinne {CommandName}
                  rinne {CommandName} <id1> <id2> [space]
                  rinne {CommandName} -h | --help

                description:
                  指定された space（または current）の ZIP スナップショット差分を表示します。
                  引数なしの場合は、最新と直前のペアを自動選択して比較します。

                examples:
                  rinne {CommandName}
                  rinne {CommandName} 00000001_20251026T090000 00000002_20251026T093000 main
                  rinne {CommandName} 00000003_20251026T100000 00000004_20251026T101000
                """);
        }
    }
}
