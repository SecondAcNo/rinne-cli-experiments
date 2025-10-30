namespace Rinne.Cli.Models
{
    /// <summary>メタ生成の結果。</summary>
    public sealed class MetaWriteResult
    {
        /// <summary>書き出したメタファイルの絶対パス</summary>
        public required string MetaPath { get; init; }
        /// <summary>確定メタ</summary>
        public required MetaDocument Meta { get; init; }
    }
}
