namespace Rinne.Cli.Models
{
    /// <summary>
    /// tidy の実行オプション。
    /// </summary>
    /// <param name="AllSpaces">全Space対象フラグ trueで全Space対象</param>
    /// <param name="Space">Space名</param>
    /// <param name="KeepCount">履歴として残す数</param>
    public sealed record TidyOptions(
        bool AllSpaces,
        string? Space,
        int KeepCount
    );
}
