namespace Rinne.Cli.Models
{
    /// <summary>
    /// 1 つのセーブ ZIP を表すログエントリ。
    /// </summary>
    /// <param name="FileName">ZIP ファイル名</param>
    /// <param name="FullPath">ZIP ファイルの絶対パス。</param>
    /// <param name="LastWriteTimeLocal">最終更新日時（ローカル時刻）。</param>
    /// <param name="LengthBytes">ファイルサイズ（バイト単位）。</param>
    public sealed record SaveLogEntry(
        string FileName,
        string FullPath,
        DateTime LastWriteTimeLocal,
        long LengthBytes);

    /// <summary>
    /// セーブログの一覧結果。
    /// </summary>
    /// <param name="Space">解決済みスペース名。</param>
    /// <param name="Entries">ZIP セーブ一覧（通常は新しい順）。</param>
    public sealed record SaveLogResult(
        string Space,
        IReadOnlyList<SaveLogEntry> Entries);
}
