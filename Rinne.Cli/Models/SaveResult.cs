namespace Rinne.Cli.Models
{
    /// <summary>
    /// セーブ処理の結果を表します。
    /// </summary>
    public sealed class SaveResult
    {
        /// <summary>採番済みのセーブ ID（例：00000042_20251027T073012345）。</summary>
        public string Id { get; init; } = string.Empty;

        /// <summary>セーブが保存されたスペース名。</summary>
        public string Space { get; init; } = string.Empty;

        /// <summary>作成された ZIP ファイルの絶対パス。</summary>
        public string ZipPath { get; init; } = string.Empty;

        /// <summary>出力されたメタデータ JSON の絶対パス。</summary>
        public string MetaPath { get; init; } = string.Empty;

        /// <summary>スペース内での連番（1 始まり）。</summary>
        public int Sequence { get; init; }
    }
}
