using Rinne.Cli.Interfaces.Commands;
using Rinne.Cli.Interfaces.Services;
using Rinne.Cli.Models;

namespace Rinne.Cli.Commands
{
    /// <summary>
    /// メタ情報およびハッシュチェーンの整合性を検証するコマンド。
    /// </summary>
    public sealed class VerifyCommand : ICliCommand
    {
        /// <summary>コマンド名。</summary>
        public const string CommandName = "verify";

        /// <summary>メタ検証サービス。</summary>
        private readonly IMetaVerifyService _verifier;

        /// <summary>
        /// コンストラクタ。検証サービスを注入する。
        /// </summary>
        public VerifyCommand(IMetaVerifyService verifier)
        {
            _verifier = verifier ?? throw new ArgumentNullException(nameof(verifier));
        }

        /// <inheritdoc/>
        public bool CanHandle(string[] args)
            => args is { Length: > 0 } && string.Equals(args[0], CommandName, StringComparison.OrdinalIgnoreCase);

        /// <inheritdoc/>
        public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
        {
            // help
            if (args.Length >= 2 && (args[1] is "-h" or "--help"))
            {
                PrintHelp();
                return 0;
            }

            try
            {
                var layout = new RepositoryLayout(Directory.GetCurrentDirectory());

                if (!Directory.Exists(layout.RinneDir))
                {
                    Console.WriteLine("[info] 先に init コマンドで初期化してください。");
                    return 0;
                }

                string? metaPath = null;
                string? spaceOpt = null;

                for (int i = 1; i < args.Length; i++)
                {
                    var a = args[i];

                    if (a.StartsWith("--"))
                    {
                        if (a.StartsWith("--meta=", StringComparison.OrdinalIgnoreCase))
                        {
                            metaPath = a.Substring("--meta=".Length).Trim().Trim('"');
                        }
                        else if (a.Equals("--meta", StringComparison.OrdinalIgnoreCase))
                        {
                            if (i + 1 >= args.Length || args[i + 1].StartsWith("-"))
                            {
                                Console.Error.WriteLine($"[{CommandName}] 失敗: --meta にパスが指定されていません。");
                                return 1;
                            }
                            metaPath = args[++i].Trim().Trim('"');
                        }
                        else if (a.StartsWith("--space=", StringComparison.OrdinalIgnoreCase))
                        {
                            spaceOpt = a.Substring("--space=".Length).Trim();
                        }
                        else if (a.Equals("--space", StringComparison.OrdinalIgnoreCase))
                        {
                            if (i + 1 >= args.Length || args[i + 1].StartsWith("-"))
                            {
                                Console.Error.WriteLine($"[{CommandName}] 失敗: --space に名前が指定されていません。");
                                return 1;
                            }
                            spaceOpt = args[++i].Trim();
                        }
                        else if (a is "--help")
                        {
                            PrintHelp();
                            return 0;
                        }
                        else
                        {
                            Console.Error.WriteLine($"[{CommandName}] 失敗: 不明なオプション '{a}'");
                            return 1;
                        }
                    }
                    else if (a == "-s")
                    {
                        if (i + 1 >= args.Length || args[i + 1].StartsWith("-"))
                        {
                            Console.Error.WriteLine($"[{CommandName}] 失敗: -s に名前が指定されていません。");
                            return 1;
                        }
                        spaceOpt = args[++i].Trim();
                    }
                    else if (a == "-h")
                    {
                        PrintHelp();
                        return 0;
                    }
                    else if (a == "--")
                    {
                        // 本コマンドは位置引数を受け付けないため、"--" 以降はエラー扱い
                        if (i + 1 < args.Length)
                        {
                            Console.Error.WriteLine($"[{CommandName}] 失敗: 余分な引数 '{args[i + 1]}'");
                            return 1;
                        }
                    }
                    else
                    {
                        // 位置引数はサポートしない
                        Console.Error.WriteLine($"[{CommandName}] 失敗: 余分な引数 '{a}'");
                        return 1;
                    }
                }
                // ===== 解析ここまで =====

                // 実行
                if (!string.IsNullOrWhiteSpace(metaPath))
                {
                    var report = await _verifier.VerifyMetaFileAsync(metaPath!, cancellationToken);
                    PrintReport(report);
                    return report.IsOk ? 0 : 2;
                }
                else
                {
                    // スペース全体検証（--space → current → main）
                    var space = !string.IsNullOrWhiteSpace(spaceOpt)
                        ? spaceOpt!
                        : TryReadCurrentSpace(layout) ?? RepositoryLayout.DefaultSpace;

                    var report = await _verifier.VerifySpaceAsync(layout.RepoRoot, space, cancellationToken);
                    PrintReport(report);
                    return report.IsOk ? 0 : 2;
                }
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
                  rinne {CommandName} [--space <name>] [--meta <path>]
                  rinne {CommandName} -h | --help

                description:
                  メタ情報とハッシュチェーンの整合性を検証します。
                  指定がなければ current の space（なければ 'main'）を対象に、
                  .rinne/data/<space>/meta 配下の全メタを古い順に検証します。

                options:
                  --meta <path>     単一メタファイル（.json）を検証
                  -s, --space <name> 対象 space 名（--meta を省略した場合に有効）
                  -h, --help        このヘルプを表示
                """);
        }

        private static void PrintReport(MetaVerifyReport report)
        {
            Console.WriteLine($"{report.Target}: {(report.IsOk ? "OK" : "NG")} - {report.Summary}");
            foreach (var line in report.Details)
                Console.WriteLine("  " + line);
        }

        private static string? TryReadCurrentSpace(RepositoryLayout layout)
        {
            try
            {
                if (File.Exists(layout.CurrentSpacePath))
                {
                    var text = File.ReadAllText(layout.CurrentSpacePath).Trim();
                    if (!string.IsNullOrWhiteSpace(text)) return text;
                }
            }
            catch
            {
                // ignore
            }
            return null;
        }
    }
}
