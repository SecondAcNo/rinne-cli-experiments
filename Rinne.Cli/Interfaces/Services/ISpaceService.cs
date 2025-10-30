namespace Rinne.Cli.Interfaces.Services
{
    /// <summary>
    /// 「作業空間（space）」に関する操作を提供するサービスの契約。
    /// </summary>
    public interface ISpaceService
    {
        /// <summary>
        /// 現在選択中の作業空間名を取得します。
        /// </summary>
        /// <param name="repoRoot">リポジトリルート。</param>
        /// <param name="cancellationToken">キャンセルトークン。</param>
        /// <returns>現在の作業空間名。未設定なら null。</returns>
        Task<string?> GetCurrentAsync(string repoRoot, CancellationToken cancellationToken = default);

        /// <summary>
        /// 既存の作業空間名を列挙します。
        /// </summary>
        /// <param name="repoRoot">リポジトリルート。</param>
        /// <param name="cancellationToken">キャンセルトークン。</param>
        Task<string[]> ListAsync(string repoRoot, CancellationToken cancellationToken = default);

        /// <summary>
        /// 作業空間を選択（切替）します。
        /// </summary>
        /// <param name="repoRoot">リポジトリルート。</param>
        /// <param name="name">選択する作業空間名。</param>
        /// <param name="createIfMissing">存在しない場合に作成してから選択するか。</param>
        /// <param name="cancellationToken">キャンセルトークン。</param>
        Task SelectAsync(string repoRoot, string name, bool createIfMissing, CancellationToken cancellationToken = default);

        /// <summary>
        /// 新しい作業空間を作成します（current は変更しません）。
        /// </summary>
        /// <param name="repoRoot">リポジトリルート。</param>
        /// <param name="name">選択する作業空間名。</param>
        /// <param name="cancellationToken">キャンセルトークン。</param>
        Task CreateAsync(string repoRoot, string name, CancellationToken cancellationToken = default);

        /// <summary>
        /// 既存の作業空間名を変更します（物理フォルダ名の変更を含む）。
        /// </summary>
        /// <param name="repoRoot">リポジトリルート。</param>
        /// <param name="oldName">対象の作業空間名</param>
        /// <param name="newName">新しい作業空間名</param>
        /// <param name="cancellationToken">キャンセルトークン。</param>
        Task RenameAsync(string repoRoot, string oldName, string newName, CancellationToken cancellationToken = default);

        /// <summary>
        /// 作業空間を削除します（current は削除不可）。
        /// </summary>
        /// <param name="repoRoot">リポジトリルート。</param>
        /// <param name="name">選択する作業空間名</param>
        /// <param name="force">非空でも削除する場合はtrue</param>
        /// <param name="cancellationToken">キャンセルトークン。</param>
        Task DeleteAsync(string repoRoot, string name, bool force, CancellationToken cancellationToken = default);
    }
}
