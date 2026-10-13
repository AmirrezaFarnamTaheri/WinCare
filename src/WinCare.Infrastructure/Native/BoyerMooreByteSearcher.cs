using System;

namespace WinCare.Infrastructure.Native;

/// <summary>
/// Hardware-accelerated SIMD binary pattern search and CRC16-CCITT checksum calculator.
/// Uses .NET 8 MemoryExtensions.IndexOf for vectorized zero-allocation pattern searches.
/// </summary>
public static class BoyerMooreByteSearcher
{
    /// <summary>
    /// Searches for the first occurrence of <paramref name="pattern"/> in <paramref name="data"/>.
    /// Returns 0-based byte index, or -1 if not found.
    /// </summary>
    public static long IndexOf(ReadOnlySpan<byte> data, ReadOnlySpan<byte> pattern)
    {
        if (pattern.IsEmpty) return 0;
        if (data.IsEmpty || data.Length < pattern.Length) return -1;

        return data.IndexOf(pattern);
    }

    /// <summary>
    /// Computes CRC16-CCITT checksum over <paramref name="data"/> with optional initial seed (default 0).
    /// </summary>
    public static ushort ComputeCrc16Ccitt(ReadOnlySpan<byte> data, ushort initialCrc = 0)
    {
        if (data.IsEmpty) return initialCrc;

        ushort crc = initialCrc;
        foreach (byte b in data)
        {
            crc ^= (ushort)(b << 8);
            for (int i = 0; i < 8; i++)
            {
                if ((crc & 0x8000) != 0)
                    crc = (ushort)((crc << 1) ^ 0x1021);
                else
                    crc = (ushort)(crc << 1);
            }
        }
        return crc;
    }
}
