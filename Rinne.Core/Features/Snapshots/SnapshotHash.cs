using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Rinne.Core.Features.Snapshots;

public readonly record struct HashItem(string Rel, long Length, Func<Stream> OpenRead);

public readonly record struct HashResult(int FileCount, long TotalBytes, string HashHex);

public static class SnapshotHash
{
    static readonly byte[] NL = new byte[] { (byte)'\n' };

    public readonly record struct IntermediateHashItem(string Rel, long Length, string HFileHex);

    public static IEnumerable<HashItem> ItemsFromPlan(
        IEnumerable<(string FullPath, string RelativePath, long Length)> planFiles,
        bool excludeMetaJson = true,
        bool excludeRinneDir = true)
    {
        foreach (var f in planFiles)
        {
            var rel = f.RelativePath.Replace('\\', '/');

            if (excludeRinneDir && rel.StartsWith(".rinne/", StringComparison.Ordinal))
                continue;
            if (excludeMetaJson && string.Equals(Path.GetFileName(rel), "meta.json", StringComparison.OrdinalIgnoreCase))
                continue;

            yield return new HashItem(
                rel,
                f.Length,
                () => new FileStream(
                    f.FullPath,
                    FileMode.Open, FileAccess.Read, FileShare.Read,
                    1 << 18, FileOptions.SequentialScan));
        }
    }

    public static IEnumerable<HashItem> ItemsFromManifestFilesystem(
        IEnumerable<(string RelativePath, long Bytes)> manifestFiles,
        string sourceRoot)
    {
        foreach (var fe in manifestFiles)
        {
            var rel = fe.RelativePath.Replace('\\', '/');
            var full = Path.Combine(sourceRoot, fe.RelativePath);

            yield return new HashItem(
                rel,
                fe.Bytes,
                () => fe.Bytes == 0
                    ? Stream.Null
                    : new FileStream(
                        full,
                        FileMode.Open, FileAccess.Read, FileShare.Read,
                        1 << 18, FileOptions.SequentialScan));
        }
    }

    public static HashResult Compute(IEnumerable<HashItem> items)
    {
        var list = items.OrderBy(x => x.Rel, StringComparer.Ordinal).ToList();
        int n = list.Count;

        byte[][] fileHashes = new byte[n][];

        Parallel.For(0, n, i =>
        {
            var it = list[i];
            using var sha = SHA256.Create();

            var relBytes = Encoding.UTF8.GetBytes(it.Rel);
            sha.TransformBlock(relBytes, 0, relBytes.Length, null, 0);
            sha.TransformBlock(NL, 0, NL.Length, null, 0);

            var sizeText = it.Length.ToString(CultureInfo.InvariantCulture);
            var sizeBytes = Encoding.UTF8.GetBytes(sizeText);
            sha.TransformBlock(sizeBytes, 0, sizeBytes.Length, null, 0);
            sha.TransformBlock(NL, 0, NL.Length, null, 0);

            if (it.Length > 0)
            {
                using var s = it.OpenRead();
                var buf = ArrayPool<byte>.Shared.Rent(1 << 18);
                try
                {
                    int nr;
                    while ((nr = s.Read(buf, 0, buf.Length)) > 0)
                        sha.TransformBlock(buf, 0, nr, null, 0);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buf);
                }
            }

            sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            fileHashes[i] = sha.Hash!;
        });

        using var finalSha = SHA256.Create();
        for (int i = 0; i < n; i++)
        {
            finalSha.TransformBlock(fileHashes[i], 0, fileHashes[i].Length, null, 0);
        }
        finalSha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);

        long totalBytes = list.Sum(x => x.Length);
        return new HashResult(n, totalBytes, Convert.ToHexString(finalSha.Hash!));
    }

    public static HashResult ComputeFromIntermediate(IEnumerable<IntermediateHashItem> items)
    {
        if (items is null) throw new ArgumentNullException(nameof(items));

        var list = items.OrderBy(x => x.Rel, StringComparer.Ordinal).ToList();
        int n = list.Count;

        byte[][] fileHashes = new byte[n][];

        for (int i = 0; i < n; i++)
        {
            var it = list[i];
            if (string.IsNullOrEmpty(it.HFileHex))
                throw new InvalidOperationException($"Intermediate hash is empty for: {it.Rel}");

            fileHashes[i] = Convert.FromHexString(it.HFileHex);
        }

        using var finalSha = SHA256.Create();
        for (int i = 0; i < n; i++)
        {
            finalSha.TransformBlock(fileHashes[i], 0, fileHashes[i].Length, null, 0);
        }
        finalSha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);

        long totalBytes = list.Sum(x => x.Length);
        return new HashResult(n, totalBytes, Convert.ToHexString(finalSha.Hash!));
    }
}
