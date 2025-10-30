using System.Security.Cryptography;

namespace Rinne.Cli.Utility
{
    /// <summary>
    /// 各種ハッシュ関連のユーティリティ関数を提供します。
    /// </summary>
    public static class HashUtility
    {
        /// <summary>
        /// 指定ファイルの SHA-256 ハッシュを計算し、16進文字列（小文字）で返します。
        /// </summary>
        /// <param name="filePath">ハッシュを計算するファイルのパス。</param>
        /// <returns>SHA-256 ハッシュ値の小文字 16 進文字列。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="filePath"/> が null または空。</exception>
        /// <exception cref="FileNotFoundException">ファイルが存在しない場合。</exception>
        public static string ComputeSha256(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentNullException(nameof(filePath), "filePath is null or empty.");

            if (!File.Exists(filePath))
                throw new FileNotFoundException("File not found.", filePath);

            using var fs = File.OpenRead(filePath);
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(fs);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
    }
}
