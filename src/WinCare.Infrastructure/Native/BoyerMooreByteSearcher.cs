namespace WinCare.Infrastructure.Native;

/// <summary>
/// High-performance Boyer-Moore binary pattern search and CRC16-CCITT checksum calculator.
/// Ports optimized jump-table mechanics from GalaxyBudsClient.
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

        unsafe
        {
            fixed (byte* pData = data)
            fixed (byte* pPat = pattern)
            {
                long index = -1;
                int status = WinCareCoreNative.WinCareCoreBoyerMooreSearch(
                    pData, (nuint)data.Length,
                    pPat, (nuint)pattern.Length,
                    &index);

                if (status == 0) return index;
            }
        }

        // Managed fallback if native fails
        return ManagedIndexOf(data, pattern);
    }

    /// <summary>
    /// Computes CRC16-CCITT checksum over <paramref name="data"/> with optional initial seed (default 0).
    /// </summary>
    public static ushort ComputeCrc16Ccitt(ReadOnlySpan<byte> data, ushort initialCrc = 0)
    {
        if (data.IsEmpty) return initialCrc;

        unsafe
        {
            fixed (byte* pData = data)
            {
                ushort crc = 0;
                int status = WinCareCoreNative.WinCareCoreCrc16Ccitt(
                    pData, (nuint)data.Length,
                    initialCrc, &crc);

                if (status == 0) return crc;
            }
        }

        // Managed fallback
        return ManagedCrc16Ccitt(data, initialCrc);
    }

    private static long ManagedIndexOf(ReadOnlySpan<byte> data, ReadOnlySpan<byte> pattern)
    {
        int patLen = pattern.Length;
        int dataLen = data.Length;

        Span<int> jump = stackalloc int[256];
        for (int b = 0; b < 256; b++) jump[b] = patLen;
        for (int j = 0; j < patLen - 1; j++) jump[pattern[j]] = patLen - 1 - j;

        int i = patLen - 1;
        while (i < dataLen)
        {
            int j = patLen - 1;
            int k = i;
            while (data[k] == pattern[j])
            {
                if (j == 0) return k;
                j--;
                k--;
            }
            i += jump[data[i]];
        }

        return -1;
    }

    private static ushort ManagedCrc16Ccitt(ReadOnlySpan<byte> data, ushort initialCrc)
    {
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
