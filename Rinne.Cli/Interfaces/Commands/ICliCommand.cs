namespace Rinne.Cli.Interfaces.Commands
{
    /// <summary>
    /// CLI コマンドの共通インターフェイスを表します。
    /// </summary>
    public interface ICliCommand
    {
        /// <summary>
        /// このコマンドが指定された引数を処理できるかを判定します。
        /// </summary>
        /// <param name="args">コマンドライン引数配列。</param>
        /// <returns>
        /// このコマンドが処理対象の場合はtrue。それ以外の場合はfalse。
        /// </returns>
        bool CanHandle(string[] args);

        /// <summary>
        /// コマンドを非同期で実行します。
        /// </summary>
        /// <param name="args">コマンドライン引数配列。</param>
        /// <param name="cancellationToken">キャンセル トークン。</param>
        /// <returns>プロセス終了コード。0 は成功を示します。</returns>
        Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default);

        /// <summary>
        /// コマンドの使用方法を表示します。
        /// </summary>
        void PrintHelp();

        /// <summary>
        /// 引数がヘルプオプション（-h, --help）かどうかを判定します。
        /// </summary>
        /// <param name="arg">引数。</param>
        /// <returns>ヘルプオプションの場合は true。</returns>
        static bool IsHelp(string arg) => arg is "-h" or "--help";
    }
}
