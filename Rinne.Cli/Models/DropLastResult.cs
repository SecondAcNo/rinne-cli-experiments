namespace Rinne.Cli.Models
{
    /// <summary>drop-last の結果。</summary>
    public sealed class DropLastResult
    {
        /// <summary>終了コード（0=成功）。</summary>
        public int ExitCode { get; init; }
        /// <summary>対象スペース。</summary>
        public string? Space { get; init; }
        /// <summary>削除した最新ID。</summary>
        public string? DeletedId { get; init; }
        /// <summary>エラー時のメッセージ。</summary>
        public string? ErrorMessage { get; init; }

        public static DropLastResult Ok(string space, string? deletedId)
            => new() { ExitCode = 0, Space = space, DeletedId = deletedId };

        public static DropLastResult Fail(int code, string message)
            => new() { ExitCode = code, ErrorMessage = message };
    }
}
