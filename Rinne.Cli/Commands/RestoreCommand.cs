using Rinne.Cli.Interfaces.Commands;
using Rinne.Cli.Interfaces.Services;
using Rinne.Cli.Models;

namespace Rinne.Cli.Commands
{
    /// <summary>
    /// スナップショット（ZIP）をワーキングツリーへ復元する最小コマンド。
    /// </summary>
    public sealed class RestoreCommand : ICliCommand
    {
        /// <summary>コマンド名。</summary>
        public const string CommandName = "restore";

        /// <summary>復元処理サービス。</summary>
        private readonly IRestoreService _restoreService;

        public RestoreCommand(IRestoreService restoreService)
        {
            _restoreService = restoreService ?? throw new ArgumentNullException(nameof(restoreService));
        }

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

            try
            {
                // 厳格パース：restore <space> <id> 以外はエラー
                if (args.Length != 3)
                {
                    Console.Error.WriteLine($"[{CommandName}] 入力エラー: 構文は 'rinne {CommandName} <space> <id>' です。");
                    PrintHelp();
                    return 2;
                }

                var spaceTok = args[1].Trim();
                var idTok = args[2].Trim();

                // 未知オプション（-で始まるトークン）はエラー
                if (IsOptionLike(spaceTok) || IsOptionLike(idTok))
                {
                    var bad = IsOptionLike(spaceTok) ? spaceTok : idTok;
                    Console.Error.WriteLine($"[{CommandName}] 入力エラー: 不明なオプション '{bad}'");
                    return 2;
                }

                if (string.IsNullOrWhiteSpace(spaceTok) || string.IsNullOrWhiteSpace(idTok))
                {
                    Console.Error.WriteLine($"[{CommandName}] 入力エラー: space と id は必須です。");
                    return 2;
                }

                // 実行
                var root = Directory.GetCurrentDirectory();
                var layout = new RepositoryLayout(root);

                if (!Directory.Exists(layout.RinneDir))
                {
                    Console.Error.WriteLine($"[{CommandName}] 入力エラー: .rinne が見つかりません。先に 'rinne init' を実行してください。");
                    return 2;
                }

                await _restoreService.RestoreAsync(root, spaceTok, idTok, cancellationToken).ConfigureAwait(false);

                Console.WriteLine($"[ok] restored {spaceTok}:{idTok} → {root}");
                return 0;
            }
            catch (OperationCanceledException)
            {
                Console.Error.WriteLine($"[{CommandName}] 中断されました。");
                return 130; // SIGINT 相当
            }
            catch (FileNotFoundException ex)
            {
                Console.Error.WriteLine($"[{CommandName}] スナップショットが見つかりません: {ex.FileName ?? "(unknown)"}");
                return 3;
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
                  rinne {CommandName} <space> <id>
                  rinne {CommandName} -h | --help

                description:
                  指定スナップショット (.rinne/data/<space>/<id>.zip) を現在のプロジェクトに復元します。
                  復元前のクリーン処理や .rinneignore の適用はサービス側で行われます。

                examples:
                  rinne {CommandName} main 00000042_20251024T091530123
                  rinne {CommandName} work 00000011_20251027T154200987
                """);
        }

        private static bool IsOptionLike(string token)
            => token.StartsWith("-", StringComparison.Ordinal); // -h, --foo などを弾く
    }
}
