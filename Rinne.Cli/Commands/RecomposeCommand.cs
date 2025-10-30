using Rinne.Cli.Interfaces.Commands;
using Rinne.Cli.Interfaces.Services;
using Rinne.Cli.Models;
using Rinne.Cli.Utilities;
using System.IO.Compression;
using System.Security.Cryptography;

namespace Rinne.Cli.Commands
{
    /// <summary>
    /// 複数スナップショット（ZIP）を優先度順に合成し、指定の出力 space へ保存するコマンド。
    /// </summary>
    public sealed class RecomposeCommand : ICliCommand
    {
        /// <summary>コマンド名。</summary>
        public const string CommandName = "recompose";

        /// <summary>合成結果を保存するサービス。</summary>
        private readonly ISaveService _save;

        public RecomposeCommand(ISaveService save)
        {
            _save = save ?? throw new ArgumentNullException(nameof(save));
        }

        /// <inheritdoc/>
        public bool CanHandle(string[] args)
            => args is { Length: > 0 } && string.Equals(args[0], CommandName, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// 合成および保存処理を実行する。
        /// </summary>
        /// <param name="args">構文: recompose &lt;outspace&gt; &lt;space1&gt; &lt;id1&gt; , &lt;space2&gt; &lt;id2&gt; , ...</param>
        /// <param name="cancellationToken">キャンセルトークン。</param>
        /// <returns>0=成功, 1=一般エラー, 2=入力エラー, 3=ファイル未検出, 130=中断。</returns>
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
                var layout = new RepositoryLayout(Directory.GetCurrentDirectory());
                if (!Directory.Exists(layout.RinneDir))
                {
                    Console.Error.WriteLine($"[{CommandName}] 入力エラー: .rinne が見つかりません。先に 'rinne init' を実行してください。");
                    return 2;
                }

                // 厳格パース： outspace と <space,id>ペア群のみを受理。未知オプションはエラー
                if (args.Length < 2)
                {
                    Console.Error.WriteLine($"[{CommandName}] 入力エラー: outspace と <space> <id> のペアを指定してください。");
                    PrintHelp();
                    return 2;
                }

                var outSpace = args[1].Trim();
                if (string.IsNullOrWhiteSpace(outSpace) || IsOptionLike(outSpace))
                {
                    Console.Error.WriteLine($"[{CommandName}] 入力エラー: outspace が不正です。");
                    return 2;
                }

                // outspace 以降のトークンから <space,id> ペアを抽出（',' はセパレータとして無視）
                var pairs = ParseSpaceIdPairsStrict(args.Skip(2));
                if (pairs is null || pairs.Count == 0)
                {
                    Console.Error.WriteLine($"[{CommandName}] 入力エラー: <space> <id> のペアを 1 つ以上、正しく指定してください。");
                    PrintHelp();
                    return 2;
                }

                // 一時出力先
                Directory.CreateDirectory(layout.TempDir);
                var outName = $"{CommandName}{DateTime.UtcNow:yyyyMMddTHHmmss}_{RandomSuffix(6)}".ToLowerInvariant();
                var outDir = Path.Combine(layout.TempDir, outName);

                var extractedRoots = new List<string>(pairs.Count);

                try
                {
                    // 1) ZIP 展開（優先度=指定順）
                    for (int i = 0; i < pairs.Count; i++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var (space, id) = pairs[i];
                        var dataDir = layout.GetSpaceDataDir(space);

                        // id は .zip 省略可
                        var candidate = Path.Combine(dataDir, id);
                        var zipPath = candidate.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                            ? candidate
                            : candidate + ".zip";

                        if (!File.Exists(zipPath))
                        {
                            // 念のため、ユーザーが拡張子付きで入れていた場合も再確認
                            if (!File.Exists(candidate))
                            {
                                Console.Error.WriteLine($"[{CommandName}] 未検出: ZIP が見つかりません: {zipPath}");
                                return 3;
                            }
                            zipPath = candidate;
                        }

                        var extractDir = Path.Combine(layout.TempDir, $"{Path.GetFileNameWithoutExtension(Path.GetFileName(zipPath))}_src{i + 1}");
                        if (Directory.Exists(extractDir))
                            Directory.Delete(extractDir, recursive: true);

                        ZipFile.ExtractToDirectory(zipPath, extractDir);
                        extractedRoots.Add(extractDir);

                        Console.WriteLine($"[info] extracted: {zipPath} -> {extractDir}");
                    }

                    // 2) 合成
                    var result = await FolderRecomposer.RecomposeAsync(
                        destinationRoot: outDir,
                        sourceRoots: extractedRoots,
                        cancellationToken: cancellationToken
                    );

                    Console.WriteLine($"[ok] recompose done: {result.Destination}");
                    Console.WriteLine($"      sources: {string.Join(", ", result.Sources.Select(Path.GetFileName))}");
                    Console.WriteLine($"      dirs: +{result.CreatedDirectories}, files: +{result.CopiedFiles}, chosen: {result.TotalChosenEntries}");

                    // 3) outSpace へセーブ
                    var message = BuildRecomposeMessage(outSpace, pairs);
                    var save = await _save.SaveAsync(
                        repoRoot: layout.RepoRoot,
                        targetRoot: outDir,
                        space: outSpace,
                        message: message,
                        cancellationToken: cancellationToken
                    );

                    Console.WriteLine($"[ok] saved: space={save.Space}, id={save.Id}");
                    Console.WriteLine($"     zip:  {save.ZipPath}");
                    if (!string.IsNullOrEmpty(save.MetaPath))
                        Console.WriteLine($"     meta: {save.MetaPath}");

                    return 0;
                }
                catch (OperationCanceledException)
                {
                    Console.Error.WriteLine($"[{CommandName}] 中断されました。");
                    return 130;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[{CommandName}] 失敗: {ex.Message}");
                    return 1;
                }
                finally
                {
                    // 4) クリーンアップ
                    foreach (var dir in extractedRoots)
                    {
                        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
                        catch { /* noop */ }
                    }
                    try { if (Directory.Exists(outDir)) Directory.Delete(outDir, recursive: true); }
                    catch { /* noop */ }
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
                  rinne {CommandName} <outspace> <space1> <id1> , <space2> <id2> , ...
                  rinne {CommandName} -h | --help

                description:
                  - 先に指定したペアほど優先度が高く、同一路径の競合は先勝ちです。
                  - 各 <id> は .rinne/data/<space>/<id>.zip を参照（.zip 省略可）。
                  - 合成結果は一時ディレクトリで作成後、<outspace> に対して新規セーブとして保存します。
                """);
        }

        /// <summary>
        /// トークン列から &lt;space&gt; &lt;id&gt; のペアを厳格抽出（未知オプションや不正個数は null を返す）。
        /// </summary>
        private static List<(string space, string id)>? ParseSpaceIdPairsStrict(IEnumerable<string> tokens)
        {
            // ',' はセパレータとして無視。'-'で始まるトークンは未知オプションとしてエラー。
            var cleaned = new List<string>();
            foreach (var t in tokens)
            {
                var tok = t?.Trim();
                if (string.IsNullOrEmpty(tok)) continue;
                if (tok == ",") continue;
                if (IsOptionLike(tok)) return null; // 未知オプションはエラー
                cleaned.Add(tok);
            }

            if (cleaned.Count == 0 || cleaned.Count % 2 != 0) return null;

            var list = new List<(string space, string id)>(cleaned.Count / 2);
            for (int i = 0; i < cleaned.Count; i += 2)
            {
                var space = cleaned[i];
                var id = cleaned[i + 1];
                if (string.IsNullOrWhiteSpace(space) || string.IsNullOrWhiteSpace(id)) return null;
                list.Add((space, id));
            }
            return list;
        }

        /// <summary>セーブメッセージを合成元一覧から組み立てる。</summary>
        private static string BuildRecomposeMessage(string outSpace, IReadOnlyList<(string space, string id)> pairs)
        {
            var sources = string.Join(" | ", pairs.Select(p => $"{p.space}/{p.id}"));
            return $"{CommandName} to {outSpace}: {sources}";
        }

        /// <summary>ランダムな16進サフィックスを生成する。</summary>
        private static string RandomSuffix(int bytes)
        {
            Span<byte> buf = stackalloc byte[bytes];
            RandomNumberGenerator.Fill(buf);
            return Convert.ToHexString(buf).ToLowerInvariant();
        }

        /// <summary>トークンがオプション風（- で始まる）かどうか。</summary>
        private static bool IsOptionLike(string token)
            => token.StartsWith("-", StringComparison.Ordinal);
    }
}
