using Rinne.Cli.Interfaces.Commands;
using Rinne.Cli.Interfaces.Services;

namespace Rinne.Cli.Commands
{
    /// <summary>
    /// RinneリポジトリをZIP形式でバックアップするコマンド。
    /// </summary>
    public sealed class BackupCommand : ICliCommand
    {
        /// <summary>コマンド名。</summary>
        public const string CommandName = "backup";

        /// <summary>バックアップ処理サービス。</summary>
        private readonly IBackupService _backup;

        public BackupCommand(IBackupService backup)
            => _backup = backup ?? throw new ArgumentNullException(nameof(backup));

        /// <inheritdoc/>
        public bool CanHandle(string[] args)
            => args.Length > 0 && string.Equals(args[0], CommandName, StringComparison.OrdinalIgnoreCase);

        /// <inheritdoc/>
        /// <returns>0=成功、2=入力エラー、130=キャンセル、9=処理エラー</returns>
        public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
        {
            // help（-h / --help のみ。混在不可）
            if (args.Length == 2 && (args[1] is "-h" or "--help"))
            {
                PrintHelp();
                return 0;
            }

            // 受理形は `backup <outputdir>` のみ
            if (args.Length != 2)
            {
                Console.Error.WriteLine($"[{CommandName}] 失敗: 構文が不正です。");
                PrintHelp();
                return 2;
            }

            var outputDir = args[1].Trim();

            // 未知オプション風（-で始まる）の拒否
            if (outputDir.StartsWith("-", StringComparison.Ordinal))
            {
                Console.Error.WriteLine($"[{CommandName}] 失敗: 不明なオプション '{outputDir}'");
                PrintHelp();
                return 2;
            }

            if (string.IsNullOrWhiteSpace(outputDir))
            {
                Console.Error.WriteLine($"[{CommandName}] 失敗: 出力ディレクトリを指定してください。");
                PrintHelp();
                return 2;
            }

            try
            {
                var rootdir = Directory.GetCurrentDirectory();
                var result = await _backup.BackupRinneAsync(rootdir, outputDir, cancellationToken).ConfigureAwait(false);

                Console.WriteLine($"[ok] ZIP : {result.ZipPath}");
                Console.WriteLine($"[ok] HASH: {result.HashPath}");
                Console.WriteLine($"[ok] SHA256 = {result.Sha256}");
                return 0;
            }
            catch (OperationCanceledException)
            {
                Console.Error.WriteLine("[cancelled] バックアップを中断しました。");
                return 130;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[error] {ex.Message}");
                return 9;
            }
        }

        /// <inheritdoc/>
        public void PrintHelp()
        {
            Console.WriteLine($"""
                usage:
                  rinne {CommandName} <outputdir>
                  rinne {CommandName} -h | --help

                description:
                  カレントディレクトリ直下の .rinne/ を ZIP 化しバックアップとして出力します。
                  出力ZIP のハッシュは '<filename>.sha256.txt' に出力します。

                examples:
                  rinne {CommandName} backups
                  rinne {CommandName} D:\RinneBackups
                """);
        }
    }
}
