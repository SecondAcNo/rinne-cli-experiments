namespace Rinne.Cli.Models
{
    /// <summary>
    /// space 取り込み時の衝突動作。
    /// </summary>
    public enum SpaceImportConflictMode
    {
        /// <summary>既存があれば別名にリネームして取り込む。</summary>
        Rename,

        /// <summary>既存を削除して上書き取り込み。</summary>
        Clean,

        /// <summary>既存があれば失敗とする。</summary>
        Fail
    }
}
