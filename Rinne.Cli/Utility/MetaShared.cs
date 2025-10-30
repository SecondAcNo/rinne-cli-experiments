using Rinne.Cli.Models;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Rinne.Cli.Utility
{
    /// <summary>
    /// メタデータ関連の共通ユーティリティ群。
    /// </summary>
    /// <remarks>
    /// <para>
    /// スペース名のサニタイズ、SHA256 計算、チェーンハッシュ生成、
    /// 最新メタ参照、.rinneignore のルール抽出と指紋計算など、
    /// MetaService / MetaVerifyService の両方で使用される共通処理を集約します。
    /// </para>
    /// </remarks>
    internal static class MetaShared
    {
        /// <summary>
        /// スペース名のサニタイズ（無効文字やスラッシュをハイフンに置換）。
        /// </summary>
        /// <param name="space">入力スペース名。</param>
        /// <returns>安全にファイル名として使えるスペース名。</returns>
        public static string SanitizeSpace(string space)
        {
            if (string.IsNullOrWhiteSpace(space)) return "main";
            foreach (var c in Path.GetInvalidFileNameChars())
                space = space.Replace(c, '-');
            space = space.Replace('/', '-').Replace('\\', '-').Trim();
            return string.IsNullOrWhiteSpace(space) ? "main" : space;
        }

        /// <summary>
        /// 指定ファイルの SHA256 を計算して 16 進小文字文字列を返します。
        /// </summary>
        /// <param name="path">対象ファイルの絶対パス。</param>
        /// <param name="ct">キャンセルトークン。</param>
        /// <returns>SHA256 ハッシュ値（16進小文字）。</returns>
        public static async Task<string> Sha256FileAsync(string path, CancellationToken ct)
        {
            using var sha = SHA256.Create();
            await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var hash = await sha.ComputeHashAsync(fs, ct).ConfigureAwait(false);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        /// <summary>
        /// 文字列データの SHA256 を計算して 16 進小文字文字列を返します。
        /// </summary>
        /// <param name="text">UTF-8 テキスト。</param>
        /// <returns>SHA256 ハッシュ値（16進小文字）。</returns>
        public static string Sha256Text(string text)
        {
            using var sha = SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
        }

        /// <summary>
        /// チェーンハッシュ（RINNE1|id|zipHash[|prev] の SHA256）を計算します。
        /// </summary>
        /// <param name="id">セーブ ID。</param>
        /// <param name="zipHash">ZIP の SHA256。</param>
        /// <param name="prev">前セーブのチェーンハッシュ（null なら genesis）。</param>
        /// <returns>チェーンハッシュ（16進小文字）。</returns>
        public static string ComputeChainThis(string id, string zipHash, string? prev)
        {
            var s = string.IsNullOrEmpty(prev)
                ? $"RINNE1|{id}|{zipHash}"
                : $"RINNE1|{id}|{zipHash}|{prev}";
            using var sha = SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(s))).ToLowerInvariant();
        }

        /// <summary>
        /// メタディレクトリ中の最新メタ（辞書順最大＝最大 seq）から prevId/prevThis を取得します。
        /// 壊れている場合はチェーンを切ります（null, null）。
        /// </summary>
        /// <param name="metaDir">meta ディレクトリパス。</param>
        /// <param name="readOptions">JSON デシリアライズ設定。</param>
        /// <returns>(prevId, prevThis) のタプル。</returns>
        public static (string? prevId, string? prevThis) TryReadLatestChain(string metaDir, JsonSerializerOptions readOptions)
        {
            if (!Directory.Exists(metaDir)) return (null, null);

            var latest = Directory.EnumerateFiles(metaDir, "*.json", SearchOption.TopDirectoryOnly)
                                  .OrderBy(f => f, StringComparer.Ordinal)
                                  .LastOrDefault();
            if (latest is null) return (null, null);

            try
            {
                var json = File.ReadAllText(latest, Encoding.UTF8);
                var doc = JsonSerializer.Deserialize<MetaDocument>(json, readOptions);
                return doc is null ? (null, null) : (doc.Id, doc.Hash?.Chain?.This);
            }
            catch
            {
                return (null, null);
            }
        }

        /// <summary>
        /// メタ JSON を読み込みます（失敗時は null を返します）。
        /// </summary>
        /// <param name="metaPath">メタファイルの絶対パス。</param>
        /// <param name="readOptions">JSON デシリアライズ設定。</param>
        /// <returns><see cref="MetaDocument"/> インスタンス、または null。</returns>
        public static MetaDocument? ReadMeta(string metaPath, JsonSerializerOptions readOptions)
        {
            try
            {
                var json = File.ReadAllText(metaPath, Encoding.UTF8);
                return JsonSerializer.Deserialize<MetaDocument>(json, readOptions);
            }
            catch { return null; }
        }

        /// <summary>
        /// .rinneignore を読み込み、有効なルール（コメント・空行除去、トリム済み）を返します。
        /// </summary>
        /// <param name="path">ignore ファイルの絶対パス。</param>
        /// <returns>有効ルールの配列。ファイルが存在しない場合は空配列。</returns>
        public static string[] ReadIgnoreRules(string path)
        {
            if (!File.Exists(path)) return Array.Empty<string>();

            var lines = File.ReadAllLines(path);
            return lines
                .Select(l => l.Trim())
                .Where(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith("#"))
                .ToArray();
        }

        /// <summary>
        /// ignore ルール配列を改行区切りテキストに連結し、その SHA256 を小文字16進で返します。
        /// </summary>
        /// <param name="rules">ルール配列。</param>
        /// <returns>指紋（小文字16進文字列）。ルールが空の場合は空文字。</returns>
        public static string FingerprintRules(string[] rules)
        {
            if (rules is null || rules.Length == 0) return string.Empty;
            var text = string.Join("\n", rules);
            using var sha = SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
        }

        /// <summary>
        /// 16進ハッシュ文字列の先頭を簡略表示（ログ向け）。
        /// </summary>
        /// <param name="hex">ハッシュ文字列。</param>
        /// <returns>先頭12文字（短い場合は全体）。</returns>
        public static string ShortHex(string hex)
            => string.IsNullOrEmpty(hex) ? string.Empty : hex.Length <= 12 ? hex : hex[..12];

        /// <summary>
        /// 大小文字を無視してハッシュ文字列を比較します（null は空文字と等価）。
        /// </summary>
        /// <param name="a">ハッシュA。</param>
        /// <param name="b">ハッシュB。</param>
        /// <returns>一致すれば true。</returns>
        public static bool HexEquals(string? a, string? b)
            => string.Equals(a ?? string.Empty, b ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }
}
