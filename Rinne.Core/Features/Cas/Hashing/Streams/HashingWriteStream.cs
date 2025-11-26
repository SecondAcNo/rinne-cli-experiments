using System.Security.Cryptography;

namespace Rinne.Core.Features.Cas.Hashing.Streams;

public sealed class HashingWriteStream : Stream
{
    private readonly Stream _inner;
    private readonly IncrementalHash _ih;
    private readonly bool _leaveOpen;
    private bool _disposed;
    private long _bytesHashed;

    public HashingWriteStream(Stream inner, bool leaveOpen = false)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        if (!inner.CanWrite) throw new ArgumentException("Stream is not writable.", nameof(inner));
        _ih = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        _leaveOpen = leaveOpen;
    }

    public long BytesHashed => _bytesHashed;

    public string GetHashHex() => Convert.ToHexString(_ih.GetHashAndReset());

    public override void Write(byte[] buffer, int offset, int count)
    {
        if (buffer is null) throw new ArgumentNullException(nameof(buffer));
        if ((uint)offset > buffer.Length || (uint)count > buffer.Length - offset)
            throw new ArgumentOutOfRangeException(nameof(offset));

        _ih.AppendData(new ReadOnlySpan<byte>(buffer, offset, count));
        _inner.Write(buffer, offset, count);
        _bytesHashed += count;
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        _ih.AppendData(buffer);
        _inner.Write(buffer);
        _bytesHashed += buffer.Length;
    }

    public override void WriteByte(byte value)
    {
        Span<byte> one = stackalloc byte[1];
        one[0] = value;
        _ih.AppendData(one);
        _inner.WriteByte(value);
        _bytesHashed += 1;
    }

    public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct)
    {
        if (buffer is null) throw new ArgumentNullException(nameof(buffer));
        if ((uint)offset > buffer.Length || (uint)count > buffer.Length - offset)
            throw new ArgumentOutOfRangeException(nameof(offset));

        _ih.AppendData(new ReadOnlySpan<byte>(buffer, offset, count));
        await _inner.WriteAsync(buffer.AsMemory(offset, count), ct).ConfigureAwait(false);
        _bytesHashed += count;
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
    {
        _ih.AppendData(buffer.Span);
        await _inner.WriteAsync(buffer, ct).ConfigureAwait(false);
        _bytesHashed += buffer.Length;
    }

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => _inner.CanWrite;

    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush() => _inner.Flush();
    public override Task FlushAsync(CancellationToken ct) => _inner.FlushAsync(ct);

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => _inner.SetLength(value);

    protected override void Dispose(bool disposing)
    {
        if (_disposed) return;
        _disposed = true;

        if (disposing)
        {
            _ih.Dispose();
            if (!_leaveOpen) _inner.Dispose();
        }
        base.Dispose(disposing);
    }
}
