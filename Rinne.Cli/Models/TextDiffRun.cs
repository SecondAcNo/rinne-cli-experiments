using Rinne.Cli.Utility;
using System.Collections.ObjectModel;

namespace Rinne.Cli.Models
{
    /// <summary>
    /// テキスト差分の実行結果を表します。
    /// </summary>
    public sealed record TextDiffRun
    {
        /// <summary>比較に用いたスペース名。</summary>
        public string Space { get; init; } = RepositoryLayout.DefaultSpace;

        /// <summary>古い側 ZIP のフルパス。</summary>
        public string OldZipPath { get; init; } = string.Empty;

        /// <summary>新しい側 ZIP のフルパス。</summary>
        public string NewZipPath { get; init; } = string.Empty;

        /// <summary>古い側の展開ディレクトリ。</summary>
        public string OldExtractDir { get; init; } = string.Empty;

        /// <summary>新しい側の展開ディレクトリ。</summary>
        public string NewExtractDir { get; init; } = string.Empty;

        /// <summary>差分ファイル一覧（相対パス基準）。</summary>
        public ReadOnlyCollection<FileTextDiffResult> Files { get; init; } = new(new List<FileTextDiffResult>());

        /// <summary>総ファイル数。</summary>
        public int TotalCount { get; init; }

        /// <summary>追加されたファイル数。</summary>
        public int AddedCount { get; init; }

        /// <summary>削除されたファイル数。</summary>
        public int RemovedCount { get; init; }

        /// <summary>変更されたファイル数。</summary>
        public int ModifiedCount { get; init; }

        /// <summary>非テキストとしてスキップされたファイル数。</summary>
        public int SkippedCount { get; init; }

        /// <summary>変更なしのファイル数。</summary>
        public int UnchangedCount { get; init; }

        /// <summary>サービス側で作業ディレクトリを削除した場合は true。</summary>
        public bool IsWorkDirectoryCleaned { get; init; }
    }
}
