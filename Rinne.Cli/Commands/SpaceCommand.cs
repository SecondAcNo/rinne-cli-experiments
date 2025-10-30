using Rinne.Cli.Interfaces.Commands;
using Rinne.Cli.Interfaces.Services;

namespace Rinne.Cli.Commands
{
    /// <summary>
    /// 作業空間（space）を操作するコマンド。
    /// </summary>
    /// <remarks>実処理は <see cref="ISpaceService"/> に委譲する。</remarks>
    public sealed class SpaceCommand : ICliCommand
    {
        /// <summary>コマンド名。</summary>
        public const string CommandName = "space";

        /// <summary>作業空間操作サービス。</summary>
        private readonly ISpaceService _space;

        public SpaceCommand(ISpaceService space)
            => _space = space ?? throw new ArgumentNullException(nameof(space));

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

            if (args.Length < 2)
            {
                Console.Error.WriteLine($"[{CommandName}] 失敗: サブコマンドが必要です。");
                PrintHelp();
                return 1;
            }

            var sub = args[1].ToLowerInvariant();
            var repoRoot = Directory.GetCurrentDirectory();

            try
            {
                // tail はサブコマンド以降
                var tail = args.Skip(2).ToArray();

                switch (sub)
                {
                    case "current":
                        {
                            if (tail.Length > 0)
                            {
                                Console.Error.WriteLine($"[{CommandName}] 失敗: 'current' は引数を受け付けません。");
                                return 1;
                            }
                            var cur = await _space.GetCurrentAsync(repoRoot, cancellationToken);
                            Console.WriteLine(string.IsNullOrWhiteSpace(cur) ? "(none)" : cur);
                            return 0;
                        }

                    case "list":
                        {
                            if (tail.Length > 0)
                            {
                                Console.Error.WriteLine($"[{CommandName}] 失敗: 'list' は引数を受け付けません。");
                                return 1;
                            }
                            var list = await _space.ListAsync(repoRoot, cancellationToken);
                            var cur = await _space.GetCurrentAsync(repoRoot, cancellationToken) ?? string.Empty;

                            if (list.Length == 0)
                            {
                                Console.WriteLine("(no spaces)");
                                return 0;
                            }

                            foreach (var p in list)
                            {
                                var mark = string.Equals(p, cur, StringComparison.Ordinal) ? "*" : " ";
                                Console.WriteLine($"{mark} {p}");
                            }
                            return 0;
                        }

                    case "select":
                        {
                            // 許可オプション: --create
                            // 構文: select <name> [--create]
                            if (tail.Length is < 1 or > 2)
                            {
                                Console.Error.WriteLine($"[{CommandName}] 失敗: 構文が不正です。usage: rinne {CommandName} select <name> [--create]");
                                return 1;
                            }

                            string? name = null;
                            bool create = false;

                            foreach (var t in tail)
                            {
                                if (t.StartsWith("-", StringComparison.Ordinal))
                                {
                                    if (t.Equals("--create", StringComparison.OrdinalIgnoreCase))
                                    {
                                        if (create) { Console.Error.WriteLine($"[{CommandName}] 失敗: '--create' が重複しています。"); return 1; }
                                        create = true;
                                    }
                                    else
                                    {
                                        Console.Error.WriteLine($"[{CommandName}] 失敗: 不明なオプション '{t}'");
                                        return 1;
                                    }
                                }
                                else
                                {
                                    if (name is not null)
                                    {
                                        Console.Error.WriteLine($"[{CommandName}] 失敗: 余分な引数 '{t}'");
                                        return 1;
                                    }
                                    name = t;
                                }
                            }

                            if (string.IsNullOrWhiteSpace(name))
                            {
                                Console.Error.WriteLine($"[{CommandName}] 失敗: Space 名が必要です。usage: rinne {CommandName} select <name> [--create]");
                                return 1;
                            }

                            await _space.SelectAsync(repoRoot, name, create, cancellationToken);
                            Console.WriteLine($"Selected space: {name}");
                            return 0;
                        }

                    case "create":
                        {
                            // 構文: create <name>（オプションなし）
                            if (tail.Length != 1 || tail[0].StartsWith("-", StringComparison.Ordinal))
                            {
                                Console.Error.WriteLine($"[{CommandName}] 失敗: 構文が不正です。usage: rinne {CommandName} create <name>");
                                return 1;
                            }
                            var name = tail[0];
                            await _space.CreateAsync(repoRoot, name, cancellationToken);
                            Console.WriteLine($"Created space: {name}");
                            return 0;
                        }

                    case "rename":
                        {
                            // 構文: rename <old> <new>（オプションなし）
                            if (tail.Length != 2 || tail.Any(t => t.StartsWith("-", StringComparison.Ordinal)))
                            {
                                Console.Error.WriteLine($"[{CommandName}] 失敗: 構文が不正です。usage: rinne {CommandName} rename <old> <new>");
                                return 1;
                            }
                            var oldRaw = tail[0];
                            var newRaw = tail[1];

                            await _space.RenameAsync(repoRoot, oldRaw, newRaw, cancellationToken);
                            Console.WriteLine($"Renamed space: {oldRaw} -> {newRaw}");
                            return 0;
                        }

                    case "delete":
                        {
                            // 許可オプション: --force
                            // 構文: delete <name> [--force]
                            if (tail.Length is < 1 or > 2)
                            {
                                Console.Error.WriteLine($"[{CommandName}] 失敗: 構文が不正です。usage: rinne {CommandName} delete <name> [--force]");
                                return 1;
                            }

                            string? name = null;
                            bool force = false;

                            foreach (var t in tail)
                            {
                                if (t.StartsWith("-", StringComparison.Ordinal))
                                {
                                    if (t.Equals("--force", StringComparison.OrdinalIgnoreCase))
                                    {
                                        if (force) { Console.Error.WriteLine($"[{CommandName}] 失敗: '--force' が重複しています。"); return 1; }
                                        force = true;
                                    }
                                    else
                                    {
                                        Console.Error.WriteLine($"[{CommandName}] 失敗: 不明なオプション '{t}'");
                                        return 1;
                                    }
                                }
                                else
                                {
                                    if (name is not null)
                                    {
                                        Console.Error.WriteLine($"[{CommandName}] 失敗: 余分な引数 '{t}'");
                                        return 1;
                                    }
                                    name = t;
                                }
                            }

                            if (string.IsNullOrWhiteSpace(name))
                            {
                                Console.Error.WriteLine($"[{CommandName}] 失敗: Space 名が必要です。usage: rinne {CommandName} delete <name> [--force]");
                                return 1;
                            }

                            await _space.DeleteAsync(repoRoot, name, force, cancellationToken);
                            Console.WriteLine($"Deleted space: {name}");
                            return 0;
                        }

                    default:
                        Console.Error.WriteLine($"[{CommandName}] 失敗: 不明なサブコマンド '{sub}'");
                        PrintHelp();
                        return 1;
                }
            }
            catch (OperationCanceledException)
            {
                Console.Error.WriteLine("Operation cancelled.");
                return 2;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
                return 3;
            }
        }

        /// <inheritdoc/>
        public void PrintHelp()
        {
            Console.WriteLine($"""
                usage:
                  rinne {CommandName} current
                  rinne {CommandName} list
                  rinne {CommandName} select <name> [--create]
                  rinne {CommandName} create <name>
                  rinne {CommandName} rename <old> <new>
                  rinne {CommandName} delete <name> [--force]
                  rinne {CommandName} -h | --help

                description:
                  作業空間（space）を一覧・作成・選択・改名・削除します。

                options:
                  --create   select 時、存在しない作業空間を作成して選択
                  --force    delete 時、非空でも削除
                  -h, --help このヘルプを表示
                """);
        }
    }
}
