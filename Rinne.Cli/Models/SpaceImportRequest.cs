namespace Rinne.Cli.Models
{
    /// <summary>
    /// space 取り込みの設定。
    /// </summary>
    public sealed class SpaceImportRequest
    {
        /// <summary>
        /// 取り込み元のルートパス（.rinne を含む）。
        /// </summary>
        public string SourceRoot { get; init; } = string.Empty;

        /// <summary>
        /// 取り込み元の space 名。
        /// </summary>
        public string SourceSpace { get; init; } = string.Empty;

        /// <summary>
        /// 衝突時の動作。
        /// </summary>
        public SpaceImportConflictMode OnConflict { get; init; } = SpaceImportConflictMode.Fail;
    }
}
