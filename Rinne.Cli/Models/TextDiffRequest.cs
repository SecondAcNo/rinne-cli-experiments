namespace Rinne.Cli.Models
{
    /// <summary>
    /// テキスト差分サービスへの入力を表します。
    /// </summary>
    public sealed class TextDiffRequest
    {
        /// <summary>古い側 ID（拡張子省略可）。</summary>
        public string? OldId { get; init; }

        /// <summary>新しい側 ID（拡張子省略可）。</summary>
        public string? NewId { get; init; }

        /// <summary>スペース名（省略時は current を参照）。</summary>
        public string? Space { get; init; }

        /// <summary>
        /// 展開先の一時作業ディレクトリを保持するかどうか。
        /// 既定は false（比較後に削除）。
        /// </summary>
        public bool KeepWorkDirectory { get; init; } = false;

        /// <summary>
        /// 明示的に作業ディレクトリ名を指定したい場合に使用。
        /// null の場合は自動生成されます。
        /// </summary>
        public string? WorkName { get; init; }
    }
}
