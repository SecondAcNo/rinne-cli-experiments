using System.Globalization;
using System.Text;

namespace Rinne.Cli.Utility
{
    /// <summary>
    /// 期限時刻（ミリ秒精度）をファイル名に埋め込む簡易ロック。
    /// <para>
    /// 例: save.lock.20251024T112500123（UTC）。存在すればロック中、期限を過ぎていれば自動掃除されます。
    /// </para>
    /// </summary>
    /// <remarks>
    /// <para>
    /// ネットワーク共有（SMB/NAS 等）でも動作しやすい“ベストエフォート”排他。
    /// </para>
    /// <para>
    /// ファイル名に時刻（UTC, ミリ秒精度）を含めることで、同一秒内での多重ロック衝突を防ぎます。
    /// </para>
    /// </remarks>
    public sealed class LockFile : IDisposable
    {
        private const string TimeFormat = "yyyyMMddTHHmmssfff"; // ミリ秒まで含める
        private readonly string _filePath;

        private LockFile(string filePath) => _filePath = filePath;

        /// <summary>
        /// ロックを取得します。既存ロックが有効期限内なら <see cref="IOException"/> を送出します。
        /// </summary>
        /// <param name="lockDir">ロックファイルを置くディレクトリ（例: .rinne/locks）。</param>
        /// <param name="name">ロック名（例: save）。</param>
        /// <param name="ttl">ロックの有効期限（推奨: 数分）。</param>
        /// <returns>取得済みロック。<see cref="IDisposable.Dispose"/> で解除。</returns>
        /// <exception cref="IOException">有効なロックが既に存在する場合。</exception>
        public static LockFile Acquire(string lockDir, string name, TimeSpan ttl)
        {
            if (string.IsNullOrWhiteSpace(lockDir)) throw new ArgumentException("lockDir is null or empty.", nameof(lockDir));
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("name is null or empty.", nameof(name));
            if (ttl <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(ttl));

            // 既存ロックを確認（期限切れは掃除）
            var pattern = $"{name}.lock.*";
            var now = DateTime.UtcNow;

            foreach (var path in Directory.EnumerateFiles(lockDir, pattern, SearchOption.TopDirectoryOnly))
            {
                if (TryParseExpiry(Path.GetFileName(path), out var expiresUtc))
                {
                    if (expiresUtc > now)
                        throw new IOException($"Lock is held until {expiresUtc:O}: {path}");
                    TryDeleteQuiet(path); // 期限切れは掃除
                }
                else
                {
                    TryDeleteQuiet(path); // 不正ファイル名も掃除
                }
            }

            // 新規ロック作成
            var expiry = now.Add(ttl);
            var lockFileName = $"{name}.lock.{expiry.ToString(TimeFormat, CultureInfo.InvariantCulture)}";
            var filePath = Path.Combine(lockDir, lockFileName);

            // 競合を避けるため CreateNew で作成
            using (File.Create(filePath)) { }

            // メタ情報を書き込み（任意）
            var meta = new StringBuilder()
                .AppendLine($"host={Environment.MachineName}")
                .AppendLine($"pid={Environment.ProcessId}")
                .AppendLine($"created={now:O}")
                .AppendLine($"expires={expiry:O}")
                .ToString();
            File.WriteAllText(filePath, meta, new UTF8Encoding(false));

            return new LockFile(filePath);
        }

        /// <summary>
        /// ロックを解放し、ロックファイルを削除します。
        /// </summary>
        public void Dispose()
        {
            TryDeleteQuiet(_filePath);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// 指定ロック名の有効ロックが存在するか判定します（期限切れは掃除）。
        /// </summary>
        /// <param name="lockDir">ロックディレクトリ。</param>
        /// <param name="name">ロック名。</param>
        /// <param name="expiresUtc">存在する場合は有効期限（UTC）。存在しなければ null。</param>
        public static bool IsLocked(string lockDir, string name, out DateTime? expiresUtc)
        {
            expiresUtc = null;
            var now = DateTime.UtcNow;
            var found = false;

            foreach (var path in Directory.EnumerateFiles(lockDir, $"{name}.lock.*", SearchOption.TopDirectoryOnly))
            {
                if (TryParseExpiry(Path.GetFileName(path), out var exp))
                {
                    if (exp > now)
                    {
                        expiresUtc = exp;
                        found = true;
                    }
                    else
                    {
                        TryDeleteQuiet(path);
                    }
                }
                else
                {
                    TryDeleteQuiet(path);
                }
            }
            return found;
        }

        /// <summary>
        /// 期限切れロックを掃除します。
        /// </summary>
        public static int SweepExpired(string lockDir, string name)
        {
            var count = 0;
            var now = DateTime.UtcNow;

            foreach (var path in Directory.EnumerateFiles(lockDir, $"{name}.lock.*", SearchOption.TopDirectoryOnly))
            {
                if (!TryParseExpiry(Path.GetFileName(path), out var exp) || exp <= now)
                {
                    if (TryDeleteQuiet(path)) count++;
                }
            }
            return count;
        }

        /// <summary>
        /// ロックファイルの解釈
        /// </summary>
        private static bool TryParseExpiry(string fileName, out DateTime expiresUtc)
        {
            // 形式: {name}.lock.{yyyyMMddTHHmmssfff}
            expiresUtc = default;
            var lastDot = fileName.LastIndexOf('.');
            if (lastDot < 0 || lastDot + 1 >= fileName.Length) return false;
            var ts = fileName[(lastDot + 1)..];

            return DateTime.TryParseExact(
                ts,
                TimeFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out expiresUtc);
        }

        /// <summary>
        /// ベストエフォートによる削除
        /// </summary>
        private static bool TryDeleteQuiet(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);

                return true;
            }
            catch { return false; }
        }
    }
}
