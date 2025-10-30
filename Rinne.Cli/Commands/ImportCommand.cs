using Rinne.Cli.Interfaces.Commands;
using Rinne.Cli.Interfaces.Services;
using Rinne.Cli.Models;

namespace Rinne.Cli.Commands
{
    /// <summary>
    /// 他リポジトリの space を取り込む CLI コマンド。
    /// </summary>
    public sealed class ImportCommand : ICliCommand
    {
        /// <summary>コマンド名。</summary>
        private const string CommandName = "import";

        private readonly ISpaceImportService _importService;

        public ImportCommand(ISpaceImportService importService)
        {
            _importService = importService ?? throw new ArgumentNullException(nameof(importService));
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

            // 受理形: import <sourceRinneRoot> <space> [--mode fail|rename|clean]
            if (args.Length < 3)
            {
                Console.Error.WriteLine($"[{CommandName}] 失敗: 引数が不足しています。");
                PrintHelp();
                return 2; // 入力エラー
            }

            // 未知オプションの早期検出（位置引数2つの後ろは --mode のみ許可）
            if (args.Length > 3 && !args[3].StartsWith("--", StringComparison.Ordinal))
            {
                Console.Error.WriteLine($"[{CommandName}] 失敗: 余分な引数 '{args[3]}'");
                PrintHelp();
                return 2;
            }

            var sourceRoot = args[1].Trim();
            var sourceSpace = args[2].Trim();

            // 位置引数にオプション風トークンが来たらエラー
            if (IsOptionLike(sourceRoot) || IsOptionLike(sourceSpace))
            {
                var bad = IsOptionLike(sourceRoot) ? sourceRoot : sourceSpace;
                Console.Error.WriteLine($"[{CommandName}] 失敗: 不明なオプション '{bad}'");
                return 2;
            }

            var mode = SpaceImportConflictMode.Fail; // 既定: fail
            bool seenMode = false;

            // オプション解析
            for (int i = 3; i < args.Length; i++)
            {
                var a = args[i];

                if (a.Equals("-h", StringComparison.OrdinalIgnoreCase) || a.Equals("--help", StringComparison.OrdinalIgnoreCase))
                {
                    Console.Error.WriteLine($"[{CommandName}] 失敗: -h/--help は単独で使用してください。");
                    return 2;
                }

                if (a.StartsWith("--mode=", StringComparison.OrdinalIgnoreCase))
                {
                    if (seenMode) { Console.Error.WriteLine($"[{CommandName}] 失敗: --mode が重複しています。"); return 2; }
                    var val = a.Substring("--mode=".Length).Trim().ToLowerInvariant();
                    if (!TryParseMode(val, out mode))
                    {
                        Console.Error.WriteLine($"[{CommandName}] 失敗: --mode の値が不正です: '{val}'（fail|rename|clean）");
                        return 2;
                    }
                    seenMode = true;
                    continue;
                }

                if (a.Equals("--mode", StringComparison.OrdinalIgnoreCase))
                {
                    if (seenMode) { Console.Error.WriteLine($"[{CommandName}] 失敗: --mode が重複しています。"); return 2; }
                    if (i + 1 >= args.Length || args[i + 1].StartsWith("-", StringComparison.Ordinal))
                    {
                        Console.Error.WriteLine($"[{CommandName}] 失敗: --mode に値が指定されていません。");
                        return 2;
                    }
                    var val = args[++i].Trim().ToLowerInvariant();
                    if (!TryParseMode(val, out mode))
                    {
                        Console.Error.WriteLine($"[{CommandName}] 失敗: --mode の値が不正です: '{val}'（fail|rename|clean）");
                        return 2;
                    }
                    seenMode = true;
                    continue;
                }

                // ここまでに該当しなければ未知オプション
                Console.Error.WriteLine($"[{CommandName}] 失敗: 不明なオプション '{a}'");
                return 2;
            }

            try
            {
                var layout = new RepositoryLayout(Directory.GetCurrentDirectory());

                if (!Directory.Exists(layout.RinneDir))
                    throw new InvalidOperationException("先に init コマンドで初期化してください。(.rinne が見つかりません)");

                var request = new SpaceImportRequest
                {
                    SourceRoot = sourceRoot,
                    SourceSpace = sourceSpace,
                    OnConflict = mode
                };

                var result = await _importService.ImportAsync(layout, request, cancellationToken).ConfigureAwait(false);

                Console.WriteLine(result.ToHumanReadable());
                return result.ExitCode; // サービス側の終了コードポリシーに委譲
            }
            catch (OperationCanceledException)
            {
                Console.Error.WriteLine($"[{CommandName}] canceled.");
                return 130;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[{CommandName}] 失敗: {ex.Message}");
                return 2; // 入力/処理エラー
            }
        }

        /// <inheritdoc/>
        public void PrintHelp()
        {
            Console.WriteLine($"""
                usage:
                  rinne {CommandName} <source_root> <space> [--mode fail|rename|clean]
                  rinne {CommandName} -h | --help

                description:
                  他の .rinne から指定 space を取り込み、現在のリポジトリにコピーします。
                  <source_root>   　　取り込み元のルートディレクトリ。ルートディレクトリ直下に.rinneが配置されます。
                  <space>             取り込み対象の space 名。
                  --mode              衝突時の動作（既定: fail）
                                      - fail   既存があれば中止
                                      - rename 別名でコピー
                                      - clean  既存を削除して上書き
                """);
        }

        private static bool TryParseMode(string val, out SpaceImportConflictMode mode)
        {
            switch (val)
            {
                case "fail": mode = SpaceImportConflictMode.Fail; return true;
                case "rename": mode = SpaceImportConflictMode.Rename; return true;
                case "clean": mode = SpaceImportConflictMode.Clean; return true;
                default: mode = SpaceImportConflictMode.Fail; return false;
            }
        }

        private static bool IsOptionLike(string token)
            => token.StartsWith("-", StringComparison.Ordinal); // - で始まるならオプション風
    }
}
