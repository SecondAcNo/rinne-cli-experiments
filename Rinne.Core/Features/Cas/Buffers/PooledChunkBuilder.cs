using System.Buffers;
using System.Runtime.CompilerServices;

namespace Rinne.Core.Features.Cas.Buffers;

public sealed class PooledChunkBuilder : IDisposable
{
    private byte[] _buf;
    private int _len;
    private bool _disposed;

    public PooledChunkBuilder(int initialCapacity = 1 << 20)
    {
        if (initialCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(initialCapacity));
        _buf = ArrayPool<byte>.Shared.Rent(initialCapacity);
        _len = 0;
    }

    public int Length => _len;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlyMemory<byte> AsMemory()
    {
        ThrowIfDisposed();
        if (_len == 0) return ReadOnlyMemory<byte>.Empty;
        var slab = GC.AllocateUninitializedArray<byte>(_len);
        Buffer.BlockCopy(_buf, 0, slab, 0, _len);
        return slab;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Append(ReadOnlySpan<byte> data)
    {
        ThrowIfDisposed();
        EnsureCapacity(_len + data.Length);
        data.CopyTo(_buf.AsSpan(_len));
        _len += data.Length;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Append(byte b)
    {
        ThrowIfDisposed();
        if (_len == _buf.Length) Grow(NextCapacity(_buf.Length, _len + 1));
        _buf[_len++] = b;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte[] DetachExact()
    {
        ThrowIfDisposed();
        if (_len == 0) return Array.Empty<byte>();
        var slab = GC.AllocateUninitializedArray<byte>(_len);
        Buffer.BlockCopy(_buf, 0, slab, 0, _len);
        _len = 0;
        return slab;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Reset()
    {
        ThrowIfDisposed();
        _len = 0;
    }

    private void EnsureCapacity(int need)
    {
        if (need <= _buf.Length) return;
        Grow(NextCapacity(_buf.Length, need));
    }

    private static int NextCapacity(int current, int need)
    {
        const int MaxArray = 0x7FFFFFC7;
        if (need < 0) throw new OutOfMemoryException();
        long cap = current <= 0 ? 1 : current;
        while (cap < need)
        {
            cap <<= 1;
            if (cap > MaxArray)
            {
                if (need > MaxArray) throw new OutOfMemoryException();
                cap = MaxArray;
                break;
            }
        }
        return (int)cap;
    }

    private void Grow(int newCap)
    {
        var n = ArrayPool<byte>.Shared.Rent(newCap);
        Buffer.BlockCopy(_buf, 0, n, 0, _len);
        ArrayPool<byte>.Shared.Return(_buf, clearArray: true);
        _buf = n;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(PooledChunkBuilder));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_buf.Length > 0)
            ArrayPool<byte>.Shared.Return(_buf, clearArray: true);
        _buf = Array.Empty<byte>();
        _len = 0;
    }
}
