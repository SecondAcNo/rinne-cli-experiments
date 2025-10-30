namespace Rinne.Cli.Models
{
    /// <summary>
    /// バックアップ処理の成果物を表します。
    /// </summary>
    public sealed class BackupResult
    {
        /// <summary>生成された ZIP ファイルの絶対パス。</summary>
        public string ZipPath { get; init; } = string.Empty;

        /// <summary>生成された SHA-256 テキストの絶対パス。</summary>
        public string HashPath { get; init; } = string.Empty;

        /// <summary>ZIP の SHA-256（小文字 16 進）。</summary>
        public string Sha256 { get; init; } = string.Empty;

        /// <summary>バックアップのベース名（拡張子なし）。</summary>
        public string BaseName { get; init; } = string.Empty;
    }
}
