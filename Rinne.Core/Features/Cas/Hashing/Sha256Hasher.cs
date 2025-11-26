using System.Buffers;
using System.Security.Cryptography;

namespace Rinne.Core.Features.Cas.Hashing;

public static class Sha256Hasher
{
    public static string ComputeHex(ReadOnlyMemory<byte> data)
    {
        using var ih = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        ih.AppendData(data.Span);
        return Convert.ToHexString(ih.GetHashAndReset());
    }

    public static string ComputeHex(IEnumerable<ReadOnlyMemory<byte>> chunks)
    {
        if (chunks is null) throw new ArgumentNullException(nameof(chunks));
        using var ih = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var c in chunks) ih.AppendData(c.Span);
        return Convert.ToHexString(ih.GetHashAndReset());
    }

    public static async Task<string> ComputeHexAsync(
        Stream src,
        int bufferSizeBytes = 1024 * 1024,
        bool preservePosition = false,
        CancellationToken ct = default)
    {
        if (src is null) throw new ArgumentNullException(nameof(src));
        if (!src.CanRead) throw new ArgumentException("Stream is not readable.", nameof(src));
        if (bufferSizeBytes <= 0 || bufferSizeBytes > 64 * 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(bufferSizeBytes));

        long? savedPos = null;
        if (preservePosition && src.CanSeek) savedPos = src.Position;

        var buffer = ArrayPool<byte>.Shared.Rent(bufferSizeBytes);
        try
        {
            using var ih = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                int read = await src.ReadAsync(buffer.AsMemory(0, bufferSizeBytes), ct)
                                    .ConfigureAwait(false);
                if (read <= 0) break;
                ih.AppendData(buffer.AsSpan(0, read));
            }
            return Convert.ToHexString(ih.GetHashAndReset());
        }
        finally
        {
            if (savedPos.HasValue) src.Seek(savedPos.Value, SeekOrigin.Begin);
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public static async Task<string> ComputeHexFromFilesAsync(
        IEnumerable<string> filePaths,
        int bufferSizeBytes = 1024 * 1024,
        CancellationToken ct = default)
    {
        if (filePaths is null) throw new ArgumentNullException(nameof(filePaths));
        if (bufferSizeBytes <= 0 || bufferSizeBytes > 64 * 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(bufferSizeBytes));

        var buffer = ArrayPool<byte>.Shared.Rent(bufferSizeBytes);
        try
        {
            using var ih = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

            foreach (var path in filePaths.OrderBy(p => p, StringComparer.Ordinal))
            {
                ct.ThrowIfCancellationRequested();

                using var fs = new FileStream(path, new FileStreamOptions
                {
                    Mode = FileMode.Open,
                    Access = FileAccess.Read,
                    Share = FileShare.Read,
                    Options = FileOptions.SequentialScan | FileOptions.Asynchronous
                });

                while (true)
                {
                    int read = await fs.ReadAsync(buffer.AsMemory(0, bufferSizeBytes), ct)
                                       .ConfigureAwait(false);
                    if (read <= 0) break;
                    ih.AppendData(buffer.AsSpan(0, read));
                }
            }

            return Convert.ToHexString(ih.GetHashAndReset());
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
