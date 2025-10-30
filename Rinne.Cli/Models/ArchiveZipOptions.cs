using System.IO.Compression;

namespace Rinne.Cli.Models
{
    /// <summary>
    /// ZIP アーカイブ作成時の動作オプションを表します。
    /// </summary>
    public sealed class ArchiveZipOptions
    {
        /// <summary>
        /// ZIP 圧縮時に使用する圧縮レベルを取得または設定します。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 既定値は <see cref="CompressionLevel.NoCompression"/> です。
        /// </para>
        /// </remarks>
        public CompressionLevel CompressionLevel { get; init; } = CompressionLevel.NoCompression;

        /// <summary>
        /// 出力先の ZIP ファイルが既に存在する場合に上書きするかどうかを示します。
        /// </summary>
        /// <remarks>既定値は false（上書き禁止）です。</remarks>
        public bool Overwrite { get; init; } = false;

        /// <summary>
        /// 除外するパスのパターン群を取得または設定します。
        /// </summary>
        /// <remarks>
        /// <para>
        /// ワイルドカード (*, ?, **) に対応しています。
        /// 例:
        /// <list type="bullet">
        /// <item><description>".git/**" – .git 以下を除外</description></item>
        /// <item><description>"bin/**", "obj/**" – ビルド生成物を除外</description></item>
        /// <item><description>"**/*.tmp" – 拡張子 .tmp のファイルを除外</description></item>
        /// </list>
        /// </para>
        /// </remarks>
        public IReadOnlyList<string> ExcludeGlobs { get; init; } = Array.Empty<string>();

        /// <summary>
        /// 隠しファイルやシステムファイルを含めるかどうかを示します。
        /// </summary>
        /// <remarks>既定値は true（含める）です。</remarks>
        public bool IncludeHidden { get; init; } = true;

        /// <summary>
        /// ファイルを追加するかどうかを決定する追加フィルタ関数を取得または設定します。
        /// </summary>
        /// <remarks>
        /// <para>
        /// この関数が指定されている場合、返値が true のファイルのみを ZIP に含めます。
        /// </para>
        /// </remarks>
        public Func<string, bool>? IncludePredicate { get; init; } = null;

        /// <summary>
        /// ZIP 作成時の進捗情報を報告する <see cref="IProgress{T}"/> を取得または設定します。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 報告される情報は、追加されたファイル数および累計バイト数です。
        /// </para>
        /// </remarks>
        public IProgress<ArchiveZipProgress>? Progress { get; init; } = null;
    }

    /// <summary>
    /// ZIP 作成の進捗状況を表します。
    /// </summary>
    /// <param name="Files">これまでに追加されたファイル数。</param>
    /// <param name="Bytes">これまでに追加された総バイト数。</param>
    public readonly record struct ArchiveZipProgress(long Files, long Bytes);
}
