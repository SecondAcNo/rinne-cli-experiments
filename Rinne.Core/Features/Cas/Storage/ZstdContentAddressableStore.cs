using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using ZstdSharp;

namespace Rinne.Core.Features.Cas.Storage;

public sealed class ZstdContentAddressableStore
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();
    private static readonly Regex Sha256Hex = new(@"^[A-F0-9]{64}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public string Root { get; }
    public int DirectoryDepth { get; }
    public int CompressionLevel { get; }

    public ZstdContentAddressableStore(string root, int directoryDepth = 2, int compressionLevel = 5)
    {
        if (string.IsNullOrWhiteSpace(root)) throw new ArgumentException("Root is required.", nameof(root));
        if (directoryDepth < 0 || directoryDepth > 16) throw new ArgumentOutOfRangeException(nameof(directoryDepth));
        if (compressionLevel < 1 || compressionLevel > 22) throw new ArgumentOutOfRangeException(nameof(compressionLevel));

        Root = root;
        DirectoryDepth = directoryDepth;
        CompressionLevel = compressionLevel;

        Directory.CreateDirectory(Root);
    }

    public static string ComputeHashHex(ReadOnlySpan<byte> data)
        => Convert.ToHexString(SHA256.HashData(data));

    public string GetPathFor(string hashHex)
    {
        if (!IsValidHash(hashHex)) throw new ArgumentException("Invalid SHA-256 hex.", nameof(hashHex));

        var path = Root;
        for (int i = 0; i < DirectoryDepth; i++)
            path = Path.Combine(path, hashHex.Substring(i * 2, 2));

        return Path.Combine(path, hashHex + ".zst");
    }

    public bool Exists(string hashHex) => File.Exists(GetPathFor(hashHex));

    public async Task<string> PutIfAbsentAsync(ReadOnlyMemory<byte> raw, CancellationToken ct = default)
    {
        var hex = ComputeHashHex(raw.Span);
        var path = GetPathFor(hex);
        if (File.Exists(path)) return hex;

        var gate = _locks.GetOrAdd(hex, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            if (File.Exists(path)) return hex;

            var dir = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(dir);

            var tmp = Path.Combine(dir, "." + hex + "." + RandomNumberGenerator.GetHexString(12) + ".tmp");

            try
            {
                using (var fs = new FileStream(tmp, new FileStreamOptions
                {
                    Mode = FileMode.CreateNew,
                    Access = FileAccess.Write,
                    Share = FileShare.None,
                    Options = FileOptions.Asynchronous
                }))
                using (var z = new CompressionStream(fs, CompressionLevel, leaveOpen: false))
                {
                    await z.WriteAsync(raw, ct).ConfigureAwait(false);
                    await z.FlushAsync(ct).ConfigureAwait(false);
                }

                if (!File.Exists(path))
                {
                    File.Move(tmp, path, overwrite: false);
                }
            }
            catch (IOException)
            {
                if (!File.Exists(path)) throw;
            }
            finally
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            }

            // ★ 最終保証：ここまで来て存在しないのは異常
            if (!File.Exists(path))
                throw new IOException($"CAS write failed: {path} does not exist after PutIfAbsentAsync");

            return hex;
        }
        finally
        {
            gate.Release();
            if (gate.CurrentCount == 1 && _locks.TryRemove(hex, out var g)) g.Dispose();
        }
    }

    private static bool IsValidHash(string? hex) => hex is not null && Sha256Hex.IsMatch(hex);
}
