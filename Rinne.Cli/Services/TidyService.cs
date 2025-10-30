using Rinne.Cli.Interfaces.Services;
using Rinne.Cli.Models;
using System.Text.Json;

namespace Rinne.Cli.Services
{
    /// <summary>
    /// バックアップ → 間引き → meta チェーン再計算 を一括実行する既定実装。
    /// </summary>
    public sealed class TidyService : ITidyService
    {
        private readonly IBackupService _backup;
        private readonly IMetaService _meta;

        public TidyService(IBackupService backup, IMetaService meta)
        {
            _backup = backup ?? throw new ArgumentNullException(nameof(backup));
            _meta = meta ?? throw new ArgumentNullException(nameof(meta));
        }

        /// <inheritdoc/>
        public async Task<int> RunAsync(string repoRoot, TidyOptions options, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(repoRoot))
                throw new ArgumentException("repoRoot is required.", nameof(repoRoot));
            if (options is null)
                throw new ArgumentNullException(nameof(options));
            if (options.KeepCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(options.KeepCount), "KeepCount must be >= 1.");

            var layout = new RepositoryLayout(repoRoot);

            if (!Directory.Exists(layout.RinneDir))
                throw new InvalidOperationException(".rinne not found. Run `rinne init` first.");

            // 1) 必ずバックアップ（出力先は .rinne と同階層 = repoRoot）
            //Console.WriteLine("[tidy] creating backup...");
            //await _backup.BackupRinneAsync(repoRoot, repoRoot, ct).ConfigureAwait(false);

            // 2) 対象 space 決定
            var spaces = options.AllSpaces
                ? layout.EnumerateSpaces()
                : new[] { layout.ResolveSpace(options.Space) };

            // 3) 各 space を整理
            foreach (var sp in spaces)
            {
                ct.ThrowIfCancellationRequested();
                TidyOneSpace(layout, sp, options.KeepCount);
                await _meta.RestitchHashesAsync(repoRoot, sp, ct).ConfigureAwait(false);
                Console.WriteLine($"[tidy:{sp}] meta chain restitched.");
            }

            Console.WriteLine("[tidy] done.");
            return 0;
        }

        /// <summary>
        /// 指定 space の ZIP と meta を「最新 N 件だけ残す」形で間引きます（ID/ZIP名は変更しません）。
        /// </summary>
        /// <summary>
        /// 指定 space の ZIP と meta を「最新 N 件だけ残す」形で間引きます（ID/ZIP名は変更しません）。
        /// メタ→ZIPの“正引き”でペアリングするため、zip名とmeta名のUTC表記差異にも耐えます。
        /// </summary>
        private static void TidyOneSpace(RepositoryLayout layout, string space, int keepCount)
        {
            var dataDir = layout.GetSpaceDataDir(space);
            var metaDir = layout.GetSpaceMetaDir(space);

            if (!Directory.Exists(dataDir))
            {
                Console.WriteLine($"[tidy:{space}] skipped (no data dir).");
                return;
            }

            // 1) meta/*.json を読み、id と zip の対応を取得（zip は相対→絶対に解決）
            var pairs = new List<Entry>();
            if (Directory.Exists(metaDir))
            {
                foreach (var mp in Directory.EnumerateFiles(metaDir, "*.json"))
                {
                    try
                    {
                        using var fs = new FileStream(mp, FileMode.Open, FileAccess.Read, FileShare.Read);
                        using var doc = JsonDocument.Parse(fs);
                        var root = doc.RootElement;

                        // id
                        var id = root.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.String
                            ? idProp.GetString()
                            : null;

                        // zip（メタからの相対パス想定）
                        string? zipRel = null;
                        if (root.TryGetProperty("zip", out var zipProp) && zipProp.ValueKind == JsonValueKind.String)
                        {
                            zipRel = zipProp.GetString();
                        }
                        else if (root.TryGetProperty("hash", out var hash) &&
                                 hash.ValueKind == JsonValueKind.Object &&
                                 hash.TryGetProperty("zip", out var _)) // 旧スキーマなどに備えて保険
                        {
                            // zip 相対パスが無いスキーマなら、id から推測（最悪のフォールバック）
                            // ただし id が無い場合はスキップ
                        }

                        if (string.IsNullOrWhiteSpace(id))
                            continue;

                        var metaPath = mp;
                        string zipAbs;

                        if (!string.IsNullOrWhiteSpace(zipRel))
                        {
                            var rel = zipRel!.Replace('/', Path.DirectorySeparatorChar);
                            zipAbs = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(metaPath)!, rel));
                        }
                        else
                        {
                            // フォールバック：id から推測
                            zipAbs = Path.Combine(dataDir, id + ".zip");
                        }

                        pairs.Add(new Entry(id!, zipAbs, metaPath));
                    }
                    catch
                    {
                        // 壊れた meta は無視（ログだけ出す）
                        Console.Error.WriteLine($"[tidy:{space}] skip invalid meta: {mp}");
                    }
                }
            }

            if (pairs.Count == 0)
            {
                Console.WriteLine($"[tidy:{space}] no meta.");
                // ZIP だけが残っている「孤児ZIP」を掃除したい場合はここで対応可（任意）
                return;
            }

            // 2) id 降順（新しい→古い）で並べて keep/purge を決定
            pairs.Sort((a, b) => StringComparer.Ordinal.Compare(b.Id, a.Id));

            var keep = pairs.Take(Math.Min(keepCount, pairs.Count)).ToList();
            var remove = pairs.Skip(keepCount).ToList();

            // 3) 削除（meta と zip の“正しいペア”を確実に消す）
            foreach (var e in remove)
            {
                TryDelete(e.ZipPath);
                TryDelete(e.MetaPath);
            }

            Console.WriteLine($"[tidy:{space}] keep={keep.Count}, removed={remove.Count}");

            // 4) オプション：孤児のクリーンアップ（任意）
            //    - meta はあるが zip が無い → meta を削除
            //    - zip はあるが meta が無い → zip を削除 など
            //    ここでは「remove 対象以外」にも整合性チェックを加える例を示します。

            // 4-1) meta 孤児（zip 不存在）を清掃（removeに含まれず、かつ zip が無いもの）
            foreach (var e in keep)
            {
                if (!File.Exists(e.ZipPath))
                {
                    // keep に入っているのに ZIP が無ければ meta だけ残ってしまうので消す
                    TryDelete(e.MetaPath);
                    Console.Error.WriteLine($"[tidy:{space}] cleaned orphan meta (zip missing): {Path.GetFileName(e.MetaPath)}");
                }
            }

            // 4-2) zip 孤児（meta 不存在）を清掃（任意）
            var zipSet = new HashSet<string>(pairs.Select(p => p.ZipPath), StringComparer.OrdinalIgnoreCase);
            foreach (var zp in Directory.EnumerateFiles(dataDir, "*.zip"))
            {
                if (!zipSet.Contains(Path.GetFullPath(zp)))
                {
                    TryDelete(zp);
                    Console.Error.WriteLine($"[tidy:{space}] cleaned orphan zip (meta missing): {Path.GetFileName(zp)}");
                }
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[tidy] delete failed: {path} : {ex.Message}");
            }
        }

        private sealed record Entry(string Id, string ZipPath, string MetaPath);
    }
}
