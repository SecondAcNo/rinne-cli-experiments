namespace Rinne.Cli.Models
{
    /// <summary>検証結果の集計。</summary>
    public sealed class MetaVerifyReport
    {
        /// <summary>
        /// 対象
        /// </summary>
        public string Target { get; init; } = string.Empty;

        /// <summary>
        /// 検証結果
        /// </summary>
        public bool IsOk { get; init; }

        /// <summary>
        /// 検証の要約
        /// </summary>
        public string Summary { get; init; } = string.Empty;

        /// <summary>
        /// 検証結果の詳細
        /// </summary>
        public string[] Details { get; init; } = [];
    }
}
