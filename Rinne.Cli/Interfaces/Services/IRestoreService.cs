namespace Rinne.Cli.Interfaces.Services
{
    /// <summary>
    /// スナップショットをワーキングツリーへ復元するサービスの契約を表します。
    /// </summary>
    public interface IRestoreService
    {
        /// <summary>
        /// 指定されたスナップショットを現在の作業ツリーに復元します。
        /// </summary>
        /// <param name="rootDir">
        /// 作業ツリーのルートディレクトリ（通常はカレントプロジェクトのパス）。
        /// </param>
        /// <param name="space"> スペース名。</param>
        /// <param name="id"> スナップショット ID（拡張子なし）</param>
        /// <param name="cancellationToken">中断用トークン </param>
        /// <returns>Task</returns>
        Task RestoreAsync(string rootDir, string space, string id, CancellationToken cancellationToken = default);
    }
}
