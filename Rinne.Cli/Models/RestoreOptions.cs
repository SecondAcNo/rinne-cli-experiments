namespace Rinne.Cli.Models
{
    /// <summary>
    /// スナップショット復元処理に必要な入力オプションを表します。
    /// </summary>
    public sealed record RestoreOptions
    {
        /// <summary>
        /// 対象リポジトリのルートディレクトリ（絶対パス）。
        /// </summary>
        public required string RepoRoot { get; init; }

        /// <summary>
        /// 対象スペース名。
        /// </summary>
        public required string Space { get; init; }

        /// <summary>
        /// 復元対象スナップショット ID（ZIP ファイル名のベース）。
        /// </summary>
        public required string SnapshotId { get; init; }

        /// <summary>
        /// 展開先ディレクトリ（絶対パス）。
        /// </summary>
        public required string Destination { get; init; }

        /// <summary>
        /// 復元対象に含めるパスパターン群（グロブ形式）。空の場合は全ファイル対象。
        /// </summary>
        public IReadOnlyList<string> Includes { get; init; } = new[] { "**/*" };

        /// <summary>
        /// 復元対象から除外するパスパターン群（グロブ形式）。
        /// </summary>
        public IReadOnlyList<string> Excludes { get; init; } = [];

        /// <summary>
        /// 展開先を事前に空にしてから復元するかどうか。
        /// </summary>
        public bool CleanBeforeRestore { get; init; }

        /// <summary>
        /// ZIP 内ファイルで既存ファイルを上書きするかどうか。既定は true。
        /// </summary>
        public bool OverwriteAlways { get; init; } = true;

        /// <summary>
        /// 復元対象ファイルを実際に展開せず、件数などのシミュレーションのみ行うかどうか。
        /// </summary>
        public bool DryRun { get; init; } = false;
    }

    /// <summary>
    /// スナップショット復元処理の結果サマリを表します。
    /// </summary>
    public sealed record RestoreResult
    {
        /// <summary>
        /// 対象となったスナップショット ZIP の絶対パス。
        /// </summary>
        public required string SnapshotPath { get; init; }

        /// <summary>
        /// 実際に展開されたディレクトリの絶対パス。
        /// </summary>
        public required string Destination { get; init; }

        /// <summary>
        /// 実際に復元されたファイル数。
        /// </summary>
        public int RestoredCount { get; init; }

        /// <summary>
        /// 除外またはフィルタによりスキップされたファイル数。
        /// </summary>
        public int SkippedCount { get; init; }
    }
}
