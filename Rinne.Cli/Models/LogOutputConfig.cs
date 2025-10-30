using System.Text.Json.Serialization;

namespace Rinne.Cli.Models
{
    /// <summary>
    /// ファイルログ出力の設定モデル（.rinne/config/log-output.json）。
    /// </summary>
    public sealed class LogOutputConfig
    {
        /// <summary>
        /// スキーマバージョン。将来互換のための番号。
        /// </summary>
        [JsonPropertyName("schema")]
        public int Schema { get; init; } = 1;

        /// <summary>
        /// ファイルへのログ出力を有効化するか。
        /// </summary>
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// ログファイルのパス（リポジトリルートからの相対または絶対）。
        /// 既定: .rinne/logs/rinne.log
        /// </summary>
        [JsonPropertyName("path")]
        public string Path { get; set; } = ".rinne/logs/rinne.log";
    }
}
