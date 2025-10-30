using Rinne.Cli.Interfaces.Commands;
using Rinne.Cli.Interfaces.Services;
using Rinne.Cli.Models;
using Rinne.Cli.Utility;

namespace Rinne.Cli.Commands
{
    /// <summary>
    /// テキスト差分を表示するコマンド。
    /// </summary>
    public sealed class TextDiffCommand : ICliCommand
    {
        /// <summary>コマンド名。</summary>
        public const string CommandName = "textdiff";

        /// <summary>テキスト差分サービス。</summary>
        private readonly ITextDiffService _service;

        /// <summary>
        /// コンストラクタ。サービスを注入する。
        /// </summary>
        /// <param name="service">テキスト差分を処理するサービス。</param>
        /// <exception cref="ArgumentNullException"><paramref name="service"/> が null の場合。</exception>
        public TextDiffCommand(ITextDiffService service)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
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
                // ===== 厳格パース =====
                // right-trim optional:
                //   1) textdiff
                //   2) textdiff <space>
                //   3) textdiff <old_id> <new_id>
                //   4) textdiff <old_id> <new_id> <space>

                string? oldId = null;
                string? newId = null;
                string? space = null;

                switch (args.Length)
                {
                    case 1:
                        // ok
                        break;

                    case 2:
                        // textdiff <space>
                        if (IsUnknownOption(args[1]))
                        {
                            Console.Error.WriteLine($"[{CommandName}] 失敗: 不明なオプション '{args[1]}'");
                            return 1;
                        }
                        space = args[1];
                        break;

                    case 3:
                        // textdiff <old_id> <new_id>
                        if (IsUnknownOption(args[1]) || IsUnknownOption(args[2]))
                        {
                            var bad = IsUnknownOption(args[1]) ? args[1] : args[2];
                            Console.Error.WriteLine($"[{CommandName}] 失敗: 不明なオプション '{bad}'");
                            return 1;
                        }
                        oldId = args[1];
                        newId = args[2];
                        break;

                    case 4:
                        // textdiff <old_id> <new_id> <space>
                        if (IsUnknownOption(args[1]) || IsUnknownOption(args[2]) || IsUnknownOption(args[3]))
                        {
                            var bad = IsUnknownOption(args[1]) ? args[1] : IsUnknownOption(args[2]) ? args[2] : args[3];
                            Console.Error.WriteLine($"[{CommandName}] 失敗: 不明なオプション '{bad}'");
                            return 1;
                        }
                        oldId = args[1];
                        newId = args[2];
                        space = args[3];
                        break;

                    default:
                        Console.Error.WriteLine($"[{CommandName}] 失敗: 余分な引数があります。");
                        PrintHelp();
                        return 1;
                }
                // ===== パースここまで =====

                var run = await _service.RunAsync(new TextDiffRequest
                {
                    OldId = oldId,
                    NewId = newId,
                    Space = space,
                    KeepWorkDirectory = false
                }, cancellationToken);

                // ヘッダとサマリ
                Console.WriteLine($"[{CommandName}] Space= {run.Space}");
                Console.WriteLine($"[{CommandName}] Zip(Left/Old)= {Path.GetFileName(run.OldZipPath)}");
                Console.WriteLine($"[{CommandName}] Zip(Right/New)= {Path.GetFileName(run.NewZipPath)}");
                Console.WriteLine($"[summary ] total={run.TotalCount}, +added={run.AddedCount}, -removed={run.RemovedCount}, ~modified={run.ModifiedCount}, skipped(non-text)={run.SkippedCount}, unchanged={run.UnchangedCount}");
                Console.WriteLine();

                // 本文（ファイルごと）
                foreach (var file in run.Files)
                {
                    PrintFileHeader(file);

                    if (file.Lines is { Count: > 0 })
                    {
                        foreach (var line in file.Lines)
                        {
                            var (prefix, color) = line.Kind switch
                            {
                                LineChangeKind.Inserted => ("+ ", ConsoleColor.Green),
                                LineChangeKind.Deleted => ("- ", ConsoleColor.Red),
                                LineChangeKind.Modified => ("~ ", ConsoleColor.Yellow),
                                _ => ("  ", ConsoleColor.Gray),
                            };
                            WriteColoredLine(prefix + line.Text, color);
                        }
                    }
                    else
                    {
                        WriteColoredLine("(no diff lines)", ConsoleColor.DarkGray);
                    }

                    Console.WriteLine();
                }

                Console.WriteLine("[info] 比較完了。");
                return 0;
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("[warn] キャンセルされました。");
                return 2;
            }
            catch (FileNotFoundException ex)
            {
                Console.WriteLine("[error] ZIP が見つかりません。");
                Console.WriteLine(ex.Message);
                return 1;
            }
            catch (DirectoryNotFoundException ex)
            {
                Console.WriteLine("[error] ディレクトリが見つかりません。");
                Console.WriteLine(ex.Message);
                return 1;
            }
            catch (InvalidOperationException ex)
            {
                Console.WriteLine("[error] 入力が不正です。");
                Console.WriteLine(ex.Message);
                PrintHelp();
                return 1;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[error] {CommandName} 実行中にエラーが発生しました。");
                Console.WriteLine(ex.Message);
                return 1;
            }
        }

        /// <inheritdoc/>
        public void PrintHelp()
        {
            Console.WriteLine($"""
                usage (right-trim optional):
                  rinne {CommandName}
                  rinne {CommandName} <space>
                  rinne {CommandName} <old_id> <new_id>
                  rinne {CommandName} <old_id> <new_id> <space>
                  rinne {CommandName} -h | --help

                description:
                  .rinne/data/<space>/ の ZIP スナップショットを展開してテキスト差分をすべて表示します。
                  space を省略すると current を使用します。
                  id を両方省略すると直近2つの ZIP スナップショットを比較します。
                """);
        }

        private static void PrintFileHeader(FileTextDiffResult file)
        {
            var mark = file.Change switch
            {
                FileChangeKind.Added => "[Added   ]",
                FileChangeKind.Removed => "[Removed ]",
                FileChangeKind.Modified => "[Modified]",
                FileChangeKind.Unchanged => "[Same    ]",
                FileChangeKind.SkippedNonText => "[Skipped ]",
                _ => "[Unknown ]"
            };

            var color = file.Change switch
            {
                FileChangeKind.Added => ConsoleColor.Green,
                FileChangeKind.Removed => ConsoleColor.Red,
                FileChangeKind.Modified => ConsoleColor.Yellow,
                FileChangeKind.Unchanged => ConsoleColor.DarkGray,
                FileChangeKind.SkippedNonText => ConsoleColor.DarkGray,
                _ => ConsoleColor.Gray
            };

            WriteColoredLine($"{mark} {file.RelativePath}", color);
        }

        private static void WriteColoredLine(string message, ConsoleColor color)
        {
            var prev = Console.ForegroundColor;
            try
            {
                Console.ForegroundColor = color;
                Console.WriteLine(message);
            }
            finally
            {
                Console.ForegroundColor = prev;
            }
        }

        /// <summary>未知オプション検出（- で始まり、許可済み以外）。</summary>
        private static bool IsUnknownOption(string token)
            => token.StartsWith('-') && token is not "-h" and not "--help";
    }
}
