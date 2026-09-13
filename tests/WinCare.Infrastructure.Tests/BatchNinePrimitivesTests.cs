using System.Text;
using WinCare.Infrastructure.Commands;
using WinCare.Infrastructure.Native;
using Xunit;

namespace WinCare.Infrastructure.Tests;

/// <summary>
/// Unit and regression tests for Batch 9 primitives converged from
/// AutoOS, GalaxyBudsClient, Motrix, PasswordSafe, and TinyWM.
/// </summary>
public sealed class BatchNinePrimitivesTests
{
    [Fact]
    public void BoyerMooreByteSearcher_finds_exact_pattern_in_byte_stream()
    {
        byte[] text = "The quick brown fox jumps over the lazy dog"u8.ToArray();
        byte[] pattern = "fox"u8.ToArray();

        long index = BoyerMooreByteSearcher.IndexOf(text, pattern);
        Assert.Equal(16, index);

        byte[] dogPattern = "dog"u8.ToArray();
        long dogIndex = BoyerMooreByteSearcher.IndexOf(text, dogPattern);
        Assert.Equal(40, dogIndex);

        byte[] missingPattern = "cat"u8.ToArray();
        long missingIndex = BoyerMooreByteSearcher.IndexOf(text, missingPattern);
        Assert.Equal(-1, missingIndex);

        long emptyPatternIndex = BoyerMooreByteSearcher.IndexOf(text, ReadOnlySpan<byte>.Empty);
        Assert.Equal(0, emptyPatternIndex);
    }

    [Fact]
    public void BoyerMooreByteSearcher_computes_crc16_ccitt_correctly()
    {
        byte[] data = "123456789"u8.ToArray();
        ushort crc = BoyerMooreByteSearcher.ComputeCrc16Ccitt(data, initialCrc: 0x0000);
        Assert.Equal(0x31C3, crc);

        ushort emptyCrc = BoyerMooreByteSearcher.ComputeCrc16Ccitt(ReadOnlySpan<byte>.Empty, initialCrc: 0xFFFF);
        Assert.Equal(0xFFFF, emptyCrc);
    }

    [Fact]
    public void ClipboardPrivacyGuard_wipes_memory_with_three_passes()
    {
        byte[] secret = [0x12, 0x34, 0x56, 0x78, 0x9A, 0xBC, 0xDE, 0xF0];
        ClipboardPrivacyGuard.SecureTrashMemory(secret);

        Assert.All(secret, b => Assert.Equal(0, b));
    }

    [Fact]
    public void CalculateRelativeWindowDelta_moves_and_resizes_windows_properly()
    {
        var initial = new WindowsCommandExecutor.WindowPlacementRect(200, 300, 800, 600);

        // Mode 1: Move from (100, 100) to (150, 120) -> delta (+50, +20)
        var moved = WindowsCommandExecutor.WindowManagerHelper.CalculateRelativeWindowDelta(
            100, 100, 150, 120, initial, mode: 1);
        Assert.Equal(250, moved.X);
        Assert.Equal(320, moved.Y);
        Assert.Equal(800, moved.Width);
        Assert.Equal(600, moved.Height);

        // Mode 2: Resize from (100, 100) to (150, 120) -> delta (+50, +20)
        var resized = WindowsCommandExecutor.WindowManagerHelper.CalculateRelativeWindowDelta(
            100, 100, 150, 120, initial, mode: 2);
        Assert.Equal(200, resized.X);
        Assert.Equal(300, resized.Y);
        Assert.Equal(850, resized.Width);
        Assert.Equal(620, resized.Height);

        // Mode 3: Both move & resize
        var both = WindowsCommandExecutor.WindowManagerHelper.CalculateRelativeWindowDelta(
            100, 100, 150, 120, initial, mode: 3);
        Assert.Equal(250, both.X);
        Assert.Equal(320, both.Y);
        Assert.Equal(850, both.Width);
        Assert.Equal(620, both.Height);
    }

    [Fact]
    public void FirmwareTableParser_parses_synthetic_smbios_tables_accurately()
    {
        // Construct a valid synthetic SMBIOS byte stream
        // 8-byte RawSMBIOSData header: [0, 2, 8, 0, len_low, len_high, 0, 0]
        List<byte> stream = new() { 0, 2, 8, 0, 0, 0, 0, 0 };

        // Type 0: BIOS info
        // Header: Type 0, formatted length 18, handle 0x0001
        // Offset 4: Vendor str index 1
        // Offset 5: Version str index 2
        // Offset 8: Date str index 3
        byte[] type0Formatted = new byte[18];
        type0Formatted[0] = 0; // Type 0
        type0Formatted[1] = 18; // Formatted length
        type0Formatted[2] = 1;
        type0Formatted[3] = 0; // Handle
        type0Formatted[4] = 1; // Vendor
        type0Formatted[5] = 2; // Version
        type0Formatted[8] = 3; // Release date
        stream.AddRange(type0Formatted);
        stream.AddRange(Encoding.ASCII.GetBytes("American Megatrends Inc.\0"));
        stream.AddRange(Encoding.ASCII.GetBytes("2.50\0"));
        stream.AddRange(Encoding.ASCII.GetBytes("09/13/2026\0\0")); // Double null termination

        // Type 1: System info
        // Formatted length 25
        byte[] type1Formatted = new byte[25];
        type1Formatted[0] = 1;
        type1Formatted[1] = 25;
        type1Formatted[2] = 2;
        type1Formatted[3] = 0;
        type1Formatted[4] = 1; // Manufacturer
        type1Formatted[5] = 2; // Product Name
        type1Formatted[6] = 3; // Version
        type1Formatted[7] = 4; // Serial Number
        Guid testGuid = Guid.NewGuid();
        Array.Copy(testGuid.ToByteArray(), 0, type1Formatted, 8, 16);
        stream.AddRange(type1Formatted);
        stream.AddRange(Encoding.ASCII.GetBytes("Dell Inc.\0"));
        stream.AddRange(Encoding.ASCII.GetBytes("Precision 5570\0"));
        stream.AddRange(Encoding.ASCII.GetBytes("1.0\0"));
        stream.AddRange(Encoding.ASCII.GetBytes("SN-987654321\0\0"));

        // Type 127: End of Table
        stream.AddRange(new byte[] { 127, 4, 0xFF, 0xFF, 0, 0 });

        SmbiosFirmwareInfo parsed = FirmwareTableParser.ParseSmbiosData(stream.ToArray());

        Assert.Equal("American Megatrends Inc.", parsed.BiosVendor);
        Assert.Equal("2.50", parsed.BiosVersion);
        Assert.Equal("09/13/2026", parsed.BiosReleaseDate);
        Assert.Equal("Dell Inc.", parsed.SystemManufacturer);
        Assert.Equal("Precision 5570", parsed.SystemProductName);
        Assert.Equal("SN-987654321", parsed.SystemSerialNumber);
        Assert.Equal(testGuid.ToString(), parsed.SystemUuid);
    }

    [Fact]
    public void WinCareCoreNative_UnlinkPosix_deletes_temporary_file()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), "wincare_posix_test_" + Guid.NewGuid().ToString("N") + ".tmp");
        File.WriteAllText(tempFile, "temp content");
        Assert.True(File.Exists(tempFile));

        byte[] utf8 = Encoding.UTF8.GetBytes(tempFile);
        unsafe
        {
            fixed (byte* p = utf8)
            {
                int status = WinCareCoreNative.WinCareCoreUnlinkPosix(p, (nuint)utf8.Length);
                Assert.Equal(0, status);
            }
        }

        Assert.False(File.Exists(tempFile));
    }
}
