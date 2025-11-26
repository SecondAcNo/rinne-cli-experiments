using System.Security.Cryptography;

namespace Rinne.Core.Common;

public static class UuidV7
{
    public static string CreateString()
    {
        Span<byte> b = stackalloc byte[16];

        ulong ms = (ulong)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() & 0xFFFFFFFFFFFF);
        b[0] = (byte)(ms >> 40);
        b[1] = (byte)(ms >> 32);
        b[2] = (byte)(ms >> 24);
        b[3] = (byte)(ms >> 16);
        b[4] = (byte)(ms >> 8);
        b[5] = (byte)ms;

        Span<byte> rnd = stackalloc byte[10];
        RandomNumberGenerator.Fill(rnd);

        ushort rand12 = (ushort)((rnd[0] << 8 | rnd[1]) & 0x0FFF);
        ushort thv = (ushort)(0x7 << 12 | rand12);
        b[6] = (byte)(thv >> 8);
        b[7] = (byte)(thv & 0xFF);

        b[8] = (byte)(0x80 | rnd[2] & 0x3F);
        b[9] = rnd[3];
        b[10] = rnd[4];
        b[11] = rnd[5];
        b[12] = rnd[6];
        b[13] = rnd[7];
        b[14] = rnd[8];
        b[15] = rnd[9];

        return ToHex(b);

        static string ToHex(ReadOnlySpan<byte> x)
        {
            Span<char> s = stackalloc char[36];
            int i = 0;
            for (int k = 0; k < 16; k++)
            {
                if (k is 4 or 6 or 8 or 10) s[i++] = '-';
                byte v = x[k];
                s[i++] = (char)(v >> 4 < 10 ? '0' + (v >> 4) : 'a' + ((v >> 4) - 10));
                s[i++] = (char)((v & 0xF) < 10 ? '0' + (v & 0xF) : 'a' + ((v & 0xF) - 10));
            }
            return new string(s);
        }
    }
}
