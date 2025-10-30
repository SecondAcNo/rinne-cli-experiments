namespace Rinne.Cli.Interfaces.Utility
{
    /// <summary>
    /// 差分結果をコンソールに整形出力するフォーマッターのインターフェイス。
    /// </summary>
    public interface IConsoleDiffFormatter
    {
        /// <summary>
        /// アーカイブ差分結果を標準出力に表示します。
        /// </summary>
        /// <param name="outcome">差分結果を含むオブジェクト。</param>
        void Print(Services.ArchiveDiffOutcome outcome);
    }
}
