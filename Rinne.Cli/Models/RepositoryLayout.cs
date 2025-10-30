using System.Text;

namespace Rinne.Cli.Models
{
    /// <summary>
    /// Rinne リポジトリの物理レイアウトを表す不変オブジェクト。
    /// </summary>
    public sealed class RepositoryLayout
    {
        /// <summary>既定スペース名。</summary>
        public const string DefaultSpace = "main";

        /// <summary>リポジトリのルート（絶対パス）。</summary>
        public string RepoRoot { get; }

        /// <summary>.rinne/ ディレクトリの絶対パス。</summary>
        public string RinneDir { get; }

        /// <summary>.rinne/config/ ディレクトリの絶対パス。</summary>
        public string ConfigDir { get; }

        /// <summary>.rinne/data/（全スペースの親）ディレクトリの絶対パス。</summary>
        public string DataRootDir { get; }

        /// <summary>.rinne/logs/ ディレクトリの絶対パス。</summary>
        public string LogsDir { get; }

        /// <summary>.rinne/state/ ディレクトリの絶対パス。</summary>
        public string StateDir { get; }

        /// <summary>.rinne/temp/ ディレクトリの絶対パス。</summary>
        public string TempDir { get; }

        /// <summary>currentファイル の絶対パス（現在のスペース名を一行で保存）。</summary>
        public string CurrentSpacePath { get; }

        /// <summary>.rinneignore ファイルの絶対パス。</summary>
        public string IgnorePath { get; }

        /// <summary>log-output.jsonファイルの絶対パス</summary>
        public string LogOutputPath { get; }

        /// <summary>
        /// 指定されたルートを基点に <see cref="RepositoryLayout"/> を初期化します。
        /// </summary>
        /// <param name="repoRoot">リポジトリのルートディレクトリ。</param>
        /// <exception cref="ArgumentException">引数が null または空文字列です。</exception>
        public RepositoryLayout(string repoRoot)
        {
            if (string.IsNullOrWhiteSpace(repoRoot))
                throw new ArgumentException("Repository root path is null or empty.", nameof(repoRoot));

            RepoRoot = Path.GetFullPath(repoRoot);
            RinneDir = Path.Combine(RepoRoot, ".rinne");
            ConfigDir = Path.Combine(RinneDir, "config");
            DataRootDir = Path.Combine(RinneDir, "data");
            LogsDir = Path.Combine(RinneDir, "logs");
            StateDir = Path.Combine(RinneDir, "state");
            TempDir = Path.Combine(RinneDir, "temp");
            CurrentSpacePath = Path.Combine(StateDir, "current");
            IgnorePath = Path.Combine(RepoRoot, ".rinneignore");
            LogOutputPath = Path.Combine(ConfigDir, "log-output.json");
        }

        /// <summary>
        /// 指定スペースの data ディレクトリの絶対パスを取得します
        /// </summary>
        public string GetSpaceDataDir(string space) =>
            Path.Combine(DataRootDir, SanitizeSpace(space));

        /// <summary>
        /// 指定スペースの meta ディレクトリの絶対パスを取得します
        /// </summary>
        public string GetSpaceMetaDir(string space) =>
            Path.Combine(GetSpaceDataDir(space), "meta");

        /// <summary>
        /// リポジトリの標準フォルダ構造（ベース＋既定スペース）を物理的に作成します。
        /// </summary>
        /// <returns>
        /// 実際に作成を行った場合は true。すでに.rinne/ が存在していた場合は false。
        /// </returns>
        public bool WriteFolderStructure()
        {
            if (string.IsNullOrWhiteSpace(RepoRoot))
                throw new InvalidOperationException("RepoRoot is null or empty.");

            var firstCreate = !Directory.Exists(RinneDir);

            // ベース構造
            Directory.CreateDirectory(RepoRoot);
            Directory.CreateDirectory(RinneDir);
            Directory.CreateDirectory(ConfigDir);
            Directory.CreateDirectory(DataRootDir);
            Directory.CreateDirectory(LogsDir);
            Directory.CreateDirectory(StateDir);
            Directory.CreateDirectory(TempDir);

            // 既定スペースの構造
            EnsureSpaceStructure(DefaultSpace);

            // currentファイル
            if (!File.Exists(CurrentSpacePath))
            {
                File.WriteAllText(CurrentSpacePath, DefaultSpace + Environment.NewLine);
            }

            // .rinneignore（なければ作成）
            if (!File.Exists(IgnorePath))
            {
                // フォルダ名指定で配下全除外の直観に合わせて末尾スラッシュを推奨
                File.WriteAllText(IgnorePath, ".rinne/" + Environment.NewLine);
            }

            //!!!非エンジニア向けのため隠し属性をつけない。linuxは玄人向けなので隠しのままでよい。!!!
            // Windows の場合は .rinne を Hidden に設定
            //if (OperatingSystem.IsWindows())
            //{
            //    try
            //    {
            //        var attr = File.GetAttributes(RinneDir);
            //        if ((attr & FileAttributes.Hidden) == 0)
            //            File.SetAttributes(RinneDir, attr | FileAttributes.Hidden);
            //    }
            //    catch { /* ignore */ }
            //}

            return firstCreate;
        }

        /// <summary>
        /// 指定スペースの物理構造（data/space/ と meta/）を作成します（冪等）。
        /// </summary>
        /// <param name="space">スペース名。</param>
        public void EnsureSpaceStructure(string space)
        {
            var b = SanitizeSpace(space);
            var data = GetSpaceDataDir(b);
            var meta = GetSpaceMetaDir(b);

            Directory.CreateDirectory(data);
            Directory.CreateDirectory(meta);
        }

        /// <summary>
        /// 既存のスペース名一覧を列挙します（.rinne/data 直下のディレクトリ）。
        /// </summary>
        public string[] EnumerateSpaces()
        {
            if (!Directory.Exists(DataRootDir)) return Array.Empty<string>();
            return Directory.EnumerateDirectories(DataRootDir)
                            .Select(Path.GetFileName)
                            .Where(n => !string.IsNullOrWhiteSpace(n))
                            .ToArray()!;
        }

        /// <summary>
        /// スペース名として使用できる安全なパスセグメントへ正規化します。
        /// </summary>
        private static string SanitizeSpace(string space)
        {
            if (string.IsNullOrWhiteSpace(space))
                return DefaultSpace;

            // 前後トリムして大文字小文字の区別を揃える
            space = space.Trim();

            // 危険文字を除去
            foreach (var c in Path.GetInvalidFileNameChars())
                space = space.Replace(c, '-');

            // 区切りを潰して1階層化
            space = space.Replace('/', '-').Replace('\\', '-');

            // Windows予約名（CON, PRN, AUX, NUL...）を回避
            if (OperatingSystem.IsWindows())
            {
                var upper = space.ToUpperInvariant();
                string[] reserved = { "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" };
                if (reserved.Contains(upper))
                    space = "_" + space;
            }

            // ドットのみ・空文字はmain扱い
            if (string.IsNullOrWhiteSpace(space) || space.Trim('.') == string.Empty)
                space = DefaultSpace;

            // 制御文字(0x00-0x1F)除去
            space = new string(space.Where(ch => !char.IsControl(ch)).ToArray());

            return space;
        }

        /// <summary>
        /// スペース名を厳格に解決します。省略時は current を読み取り、存在しなければ例外を投げます。
        /// </summary>
        public async Task<string> ResolveSpaceStrictAsync(string? space, CancellationToken ct = default)
        {
            if (!string.IsNullOrWhiteSpace(space))
                return SanitizeSpace(space);

            if (!File.Exists(CurrentSpacePath))
                throw new InvalidOperationException("space が指定されておらず current も存在しません。");

            var text = await File.ReadAllTextAsync(CurrentSpacePath, ct).ConfigureAwait(false);
            var value = text.Trim();

            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidOperationException("current ファイルが空です。");

            return SanitizeSpace(value);
        }


        /// <summary>
        /// 引数で指定があればそれを返し、なければ .rinne/state/current を UTF-8 で読み取り、
        /// 空なら <see cref="DefaultSpace"/> を返します。
        /// </summary>
        public string ResolveSpace(string? spaceFromArgs)
        {
            if (!string.IsNullOrWhiteSpace(spaceFromArgs))
                return SanitizeSpace(spaceFromArgs);

            if (TryReadCurrentSpace(out var current))
                return current;

            return DefaultSpace;
        }

        /// <summary>
        /// .rinne/state/current から現在のスペース名を読み取ります。
        /// 成功時は true を返し、<paramref name="space"/> に反映します。
        /// </summary>
        public bool TryReadCurrentSpace(out string space)
        {
            space = DefaultSpace;
            try
            {
                if (File.Exists(CurrentSpacePath))
                {
                    var text = File.ReadAllText(CurrentSpacePath, Encoding.UTF8).Trim();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        space = SanitizeSpace(text);
                        return true;
                    }
                }
            }
            catch { /* ignore and fallback */ }
            return false;
        }
    }
}
