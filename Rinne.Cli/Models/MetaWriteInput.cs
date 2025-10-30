namespace Rinne.Cli.Models
{
    /// <summary>メタ生成の入力。</summary>
    public sealed class MetaWriteInput
    {
        /// <summary>リポジトリのルート（.rinne を含む）</summary>
        public required string RepoRoot { get; init; }
        /// <summary>スペース名</summary>
        public required string Space { get; init; }
        /// <summary>スペース単位の連番（1〜）</summary>
        public required int Seq { get; init; }
        /// <summary>対象ZIPの絶対パス</summary>
        public required string ZipAbsolutePath { get; init; }
        /// <summary>セーブメッセージ（任意）</summary>
        public string? Message { get; init; }
        /// <summary>ignore元ファイル名（既定 .rinneignore／RepoRoot直下）</summary>
        public string IgnoreSourceFileName { get; init; } = ".rinneignore";
        /// <summary> Recompose に関する一行メモ </summary>
        public string? RecomposeInfo { get; init; }
    }
}
