namespace Rinne.Cli.Interfaces.System
{
    /// <summary>
    /// ディレクトリ配下を安全に空にするサービス。
    /// </summary>
    public interface IDirectoryCleaner
    {
        /// <summary>
        /// 指定されたディレクトリ配下を空にします。
        /// </summary>
        /// <param name="dir">対象ディレクトリ。</param>
        void Empty(string dir);
    }
}
