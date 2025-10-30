using Rinne.Cli.Utility;

namespace Rinne.Cli.Interfaces.Services
{
    /// <summary>
    /// アーカイブ差分処理の結果と付帯情報。
    /// </summary>
    public sealed class ArchiveDiffOutcome
    {
        /// <summary>解決された space。</summary>
        public required string Space { get; init; }

        /// <summary>解決された older 側の ID（拡張子なし）。</summary>
        public required string Id1 { get; init; }

        /// <summary>解決された newer 側の ID（拡張子なし）。</summary>
        public required string Id2 { get; init; }

        /// <summary>差分結果。</summary>
        public required FolderDiffResult Result { get; init; }

        /// <summary>比較に用いた ZIP のフルパス（older）。</summary>
        public required string ZipPath1 { get; init; }

        /// <summary>比較に用いた ZIP のフルパス（newer）。</summary>
        public required string ZipPath2 { get; init; }
    }
}
