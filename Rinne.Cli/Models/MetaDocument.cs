using System.Text.Json.Serialization;

namespace Rinne.Cli.Models
{
    /// <summary>
    /// Rinne のメタファイルを表します。
    /// </summary>
    public sealed class MetaDocument
    {
        /// <summary>メタスキーマのバージョン。</summary>
        [JsonPropertyName("schema")]
        public int Schema { get; set; }

        /// <summary>セーブID。例: 00000001_20251026T010732000Z。</summary>
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        /// <summary>スペース内の連番。</summary>
        [JsonPropertyName("seq")]
        public int Seq { get; set; }

        /// <summary>セーブ時刻（UTC）。</summary>
        [JsonPropertyName("utc")]
        public string Utc { get; set; } = string.Empty;

        /// <summary>対象スペース名。</summary>
        [JsonPropertyName("space")]
        public string Space { get; set; } = string.Empty;

        /// <summary>ZIP ファイルへの相対パス。</summary>
        [JsonPropertyName("zip")]
        public string Zip { get; set; } = string.Empty;

        /// <summary>セーブメッセージ。</summary>
        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;

        /// <summary>ignore 設定情報。</summary>
        [JsonPropertyName("ignore")]
        public MetaIgnore Ignore { get; set; } = new();

        /// <summary>ZIP とチェーンのハッシュ情報。</summary>
        [JsonPropertyName("hash")]
        public MetaHash Hash { get; set; } = new();
    }

    /// <summary>
    /// ignore 設定情報。
    /// </summary>
    public sealed class MetaIgnore
    {
        /// <summary>元ファイル名（通常は .rinneignore）。</summary>
        [JsonPropertyName("source")]
        public string Source { get; set; } = ".rinneignore";

        /// <summary>有効なルール一覧。</summary>
        [JsonPropertyName("rules")]
        public string[] Rules { get; set; } = Array.Empty<string>();
    }

    /// <summary>
    /// ZIP とチェーンハッシュ情報。
    /// </summary>
    public sealed class MetaHash
    {
        /// <summary>ハッシュアルゴリズム名。</summary>
        [JsonPropertyName("algo")]
        public string Algo { get; set; } = "SHA256";

        /// <summary>ZIP のハッシュ値。</summary>
        [JsonPropertyName("zip")]
        public string Zip { get; set; } = string.Empty;

        /// <summary>チェーンハッシュ情報。</summary>
        [JsonPropertyName("chain")]
        public MetaHashChain Chain { get; set; } = new();
    }

    /// <summary>
    /// チェーンハッシュ情報。
    /// </summary>
    public sealed class MetaHashChain
    {
        /// <summary>前のセーブID。</summary>
        [JsonPropertyName("prevId")]
        public string? PrevId { get; set; }

        /// <summary>前のチェーンハッシュ。</summary>
        [JsonPropertyName("prev")]
        public string? Prev { get; set; }

        /// <summary>現在のチェーンハッシュ。</summary>
        [JsonPropertyName("this")]
        public string This { get; set; } = string.Empty;
    }
}
