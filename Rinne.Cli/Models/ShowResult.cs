namespace Rinne.Cli.Models
{
    /// <summary>
    /// showコマンド／サービスの処理結果を表す不変オブジェクト。
    /// </summary>
    public sealed class ShowResult
    {
        /// <summary>
        /// 終了コード。
        /// <para>0 = 成功、非 0 = エラー。</para>
        /// </summary>
        public int ExitCode { get; }

        /// <summary>
        /// 整形済み JSON。
        /// <para>成功時のみ非 null。</para>
        /// </summary>
        public string? FormattedJson { get; }

        /// <summary>
        /// エラーメッセージ。
        /// <para>失敗時のみ非 null。</para>
        /// </summary>
        public string? ErrorMessage { get; }

        /// <summary>
        /// 最終的に解決されたスペース名。
        /// </summary>
        public string? ResolvedSpace { get; }

        /// <summary>
        /// 最終的に解決されたセーブ ID。
        /// </summary>
        public string? ResolvedId { get; }

        /// <summary>
        /// 読み込んだ meta.json の絶対パス。
        /// </summary>
        public string? MetaPath { get; }

        /// <summary>
        /// 成功結果を生成します。
        /// </summary>
        /// <param name="formattedJson">整形済み JSON。</param>
        /// <param name="space">解決済みスペース名。</param>
        /// <param name="id">解決済みセーブ ID。</param>
        /// <param name="metaPath">meta.json の絶対パス。</param>
        /// <returns>成功結果。</returns>
        public static ShowResult Ok(string formattedJson, string space, string id, string metaPath)
            => new ShowResult(0, formattedJson, null, space, id, metaPath);

        /// <summary>
        /// 失敗結果を生成します。
        /// </summary>
        /// <param name="exitCode">終了コード。</param>
        /// <param name="message">エラーメッセージ。</param>
        /// <returns>失敗結果。</returns>
        public static ShowResult Fail(int exitCode, string message)
            => new ShowResult(exitCode, null, message, null, null, null);

        /// <summary>
        /// コンストラクタ（外部からは <see cref="Ok"/> / <see cref="Fail"/> を使用）。
        /// </summary>
        private ShowResult(
            int exitCode,
            string? formattedJson,
            string? errorMessage,
            string? resolvedSpace,
            string? resolvedId,
            string? metaPath)
        {
            ExitCode = exitCode;
            FormattedJson = formattedJson;
            ErrorMessage = errorMessage;
            ResolvedSpace = resolvedSpace;
            ResolvedId = resolvedId;
            MetaPath = metaPath;
        }
    }
}
