namespace Rinne.Cli.Models
{
    /// <summary>
    /// space 取り込みの結果。
    /// </summary>
    public sealed class SpaceImportResult
    {
        /// <summary>
        /// 終了コード（0=成功）。
        /// </summary>
        public int ExitCode { get; init; }

        /// <summary>
        /// 実際の取り込み先 space 名。
        /// </summary>
        public string EffectiveSpace { get; init; } = string.Empty;

        /// <summary>
        /// エラーメッセージ（失敗時のみ）。
        /// </summary>
        public string? Message { get; init; }

        /// <summary>
        /// 表示用の短い文字列を返す。
        /// </summary>
        public string ToHumanReadable()
            => ExitCode == 0
                ? $"[import] ok: \"{EffectiveSpace}\""
                : $"[import] ng: \"{Message ?? EffectiveSpace}\"";
    }
}
