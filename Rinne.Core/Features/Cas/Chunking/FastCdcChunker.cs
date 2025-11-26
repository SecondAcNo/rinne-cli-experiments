using System.Buffers;
using System.Runtime.CompilerServices;

namespace Rinne.Core.Features.Cas.Chunking;

public static class FastCdcChunker
{
    public sealed record Chunk(int Index, int Length, byte[] Bytes);

    public static async IAsyncEnumerable<Chunk> SplitAsync(
        Stream src, int avgSize, int minSize, int maxSize,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ValidateParams(avgSize, minSize, maxSize);

        int k = Math.Max(1, (int)Math.Round(Math.Log2(avgSize)));
        ulong maskNormal = (1UL << k) - 1UL;
        ulong maskAfterAvg = (1UL << Math.Max(1, k - 1)) - 1UL;

        byte[] readBuf = ArrayPool<byte>.Shared.Rent(1 << 20);
        const int ProbeIntervalBytes = 4 << 20;
        try
        {
            using var builder = new PooledChunkBuilder(Math.Min(maxSize, 1 << 20));

            ulong h = 0;
            int index = 0;
            int processed = 0;

            int read;
            while ((read = await src.ReadAsync(readBuf.AsMemory(0, readBuf.Length), ct)
                                     .ConfigureAwait(false)) > 0)
            {
                int off = 0;
                while (off < read)
                {
                    ct.ThrowIfCancellationRequested();

                    int room = maxSize - builder.Length;
                    if (room <= 0)
                    {
                        yield return Detach(builder, index++);
                        h = 0;
                        room = maxSize;
                    }

                    int step = Math.Min(room, read - off);
                    builder.Append(readBuf, off, step);

                    int end = off + step;
                    for (int i = off; i < end; i++)
                        h = (h << 1) + _gear[readBuf[i]];

                    off = end;
                    processed += step;

                    if (builder.Length >= maxSize)
                    {
                        yield return Detach(builder, index++);
                        h = 0;
                    }
                    else if (builder.Length >= minSize)
                    {
                        ulong mask = builder.Length >= avgSize ? maskAfterAvg : maskNormal;
                        if ((h & mask) == 0)
                        {
                            yield return Detach(builder, index++);
                            h = 0;
                        }
                    }

                    if (processed >= ProbeIntervalBytes)
                    {
                        processed = 0;
                        ct.ThrowIfCancellationRequested();
                    }
                }
            }

            if (builder.Length > 0)
                yield return Detach(builder, index++);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(readBuf);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Chunk Detach(PooledChunkBuilder builder, int index)
    {
        var slab = builder.DetachExact();
        return new Chunk(index, slab.Length, slab);
    }

    private static void ValidateParams(int avgSize, int minSize, int maxSize)
    {
        if (avgSize <= 0) throw new ArgumentOutOfRangeException(nameof(avgSize));
        if (minSize <= 0) throw new ArgumentOutOfRangeException(nameof(minSize));
        if (maxSize <= 0) throw new ArgumentOutOfRangeException(nameof(maxSize));
        if (minSize > maxSize) throw new ArgumentException("minSize must be <= maxSize.", nameof(minSize));
        if (avgSize < minSize || avgSize > maxSize)
            throw new ArgumentException("avgSize must be within [minSize, maxSize].", nameof(avgSize));
    }

    private static readonly ulong[] _gear = GenerateGearTable();
    private static ulong[] GenerateGearTable()
    {
        var t = new ulong[256];
        ulong x = 0x9E3779B97F4A7C15UL;
        for (int i = 0; i < 256; i++)
        {
            x += 0x9E3779B97F4A7C15UL;
            ulong z = x;
            z ^= z >> 30; z *= 0xBF58476D1CE4E5B9UL;
            z ^= z >> 27; z *= 0x94D049BB133111EBUL;
            z ^= z >> 31;
            if (z == 0) z = 1;
            t[i] = z;
        }
        return t;
    }

    private sealed class PooledChunkBuilder : IDisposable
    {
        private byte[] _buf;
        private int _len;

        public int Length => _len;

        public PooledChunkBuilder(int initialCapacity)
        {
            if (initialCapacity <= 0) initialCapacity = 4096;
            _buf = ArrayPool<byte>.Shared.Rent(initialCapacity);
            _len = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Append(byte[] src, int offset, int count)
        {
            if (count <= 0) return;
            EnsureCapacity(_len + count);
            Buffer.BlockCopy(src, offset, _buf, _len, count);
            _len += count;
        }

        public byte[] DetachExact()
        {
            var res = GC.AllocateUninitializedArray<byte>(_len);
            Buffer.BlockCopy(_buf, 0, res, 0, _len);
            _len = 0;
            return res;
        }

        private void EnsureCapacity(int need)
        {
            if (need <= _buf.Length) return;
            int newCap = _buf.Length;
            while (newCap < need)
                newCap = newCap < 1 << 20 ? newCap * 2 : newCap + (1 << 20);
            var next = ArrayPool<byte>.Shared.Rent(newCap);
            Buffer.BlockCopy(_buf, 0, next, 0, _len);
            ArrayPool<byte>.Shared.Return(_buf, clearArray: false);
            _buf = next;
        }

        public void Dispose()
        {
            var buf = _buf;
            _buf = Array.Empty<byte>();
            if (buf.Length > 0) ArrayPool<byte>.Shared.Return(buf, clearArray: false);
        }
    }
}
