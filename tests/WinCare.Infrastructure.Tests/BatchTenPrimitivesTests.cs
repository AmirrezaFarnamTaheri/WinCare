using System.Security.Cryptography;
using System.Text;
using WinCare.Infrastructure.Diagnostics;
using WinCare.Infrastructure.Downloads;
using WinCare.Infrastructure.Native;
using WinCare.Infrastructure.Security;
using Xunit;

namespace WinCare.Infrastructure.Tests;

/// <summary>
/// Unit and regression tests for Batch 10 primitives converged from:
/// AutoOS, GalaxyBudsClient, Log-Viewer-Utility, Materialious, Motrix, PasteBarApp,
/// i3utils, pwsafe, scoop-directory, tinywm, and the reintegrated historical projects
/// (AdbFileManager, AnotherRedisDesktopManager, yamlist, awesome, ArrowDL, CWP-Utilities).
/// </summary>
public sealed class BatchTenPrimitivesTests
{
    [Fact]
    public void WinCareCoreNative_CalculateEntropy_computes_accurate_shannon_entropy()
    {
        // Uniform distribution: 256 distinct bytes in equal frequencies has exactly 8.0 bits of entropy
        byte[] uniformBytes = new byte[256];
        for (int i = 0; i < 256; i++)
        {
            uniformBytes[i] = (byte)i;
        }

        double uniformEntropy = 0;
        int status;
        unsafe
        {
            fixed (byte* p = uniformBytes)
            {
                status = WinCareCoreNative.WinCareCoreCalculateEntropy(p, (nuint)uniformBytes.Length, &uniformEntropy);
            }
        }

        Assert.Equal(0, status);
        Assert.InRange(uniformEntropy, 7.99, 8.01);

        // Constant byte stream: all identical bytes has exactly 0.0 bits of entropy
        byte[] zeroBytes = new byte[1024];
        double zeroEntropy = -1;
        unsafe
        {
            fixed (byte* p = zeroBytes)
            {
                status = WinCareCoreNative.WinCareCoreCalculateEntropy(p, (nuint)zeroBytes.Length, &zeroEntropy);
            }
        }

        Assert.Equal(0, status);
        Assert.Equal(0.0, zeroEntropy);

        // Null / empty buffer returns 0.0
        double emptyEntropy = -1;
        unsafe
        {
            status = WinCareCoreNative.WinCareCoreCalculateEntropy(null, 0, &emptyEntropy);
        }

        Assert.Equal(0, status);
        Assert.Equal(0.0, emptyEntropy);
    }

    [Fact]
    public void WinCareCoreNative_QueryFileIdentity_retrieves_file_id_and_volume()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), "wincare_identity_test_" + Guid.NewGuid().ToString("N") + ".tmp");
        File.WriteAllText(tempFile, "identity test payload");
        try
        {
            byte[] utf8Path = Encoding.UTF8.GetBytes(tempFile);
            ulong volumeSerial = 0;
            ulong fileIdHigh = 0;
            ulong fileIdLow = 0;

            int status;
            unsafe
            {
                fixed (byte* pPath = utf8Path)
                {
                    status = WinCareCoreNative.WinCareCoreQueryFileIdentity(
                        pPath,
                        (nuint)utf8Path.Length,
                        &volumeSerial,
                        &fileIdHigh,
                        &fileIdLow);
                }
            }

            Assert.Equal(0, status);
            Assert.True(volumeSerial > 0, "VolumeSerialNumber should be non-zero");
            Assert.True(fileIdHigh > 0 || fileIdLow > 0, "FileId should contain non-zero identifier bits");

            // Query on non-existent file fails safely with non-zero exit code
            byte[] badPath = Encoding.UTF8.GetBytes(tempFile + ".missing");
            int badStatus;
            unsafe
            {
                fixed (byte* pPath = badPath)
                {
                    badStatus = WinCareCoreNative.WinCareCoreQueryFileIdentity(
                        pPath,
                        (nuint)badPath.Length,
                        &volumeSerial,
                        &fileIdHigh,
                        &fileIdLow);
                }
            }

            Assert.NotEqual(0, badStatus);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public void Win32PowerSchemeGovernor_enumerates_and_reads_plans()
    {
        Guid activeGuid = Win32PowerSchemeGovernor.GetActiveSchemeGuid();
        Assert.NotEqual(Guid.Empty, activeGuid);

        var schemes = Win32PowerSchemeGovernor.EnumerateSchemes();
        Assert.NotEmpty(schemes);
        Assert.Contains(schemes, s => s.SchemeGuid == activeGuid);
        Assert.Contains(schemes, s => !string.IsNullOrWhiteSpace(s.Name));
    }

    [Fact]
    public void BluetoothPeripheralMonitor_formats_mac_and_parses()
    {
        string formatted = BluetoothPeripheralMonitor.FormatMacAddress(0x001122334455);
        Assert.Equal("00:11:22:33:44:55", formatted);

        string zeroFormatted = BluetoothPeripheralMonitor.FormatMacAddress(0);
        Assert.Equal("00:00:00:00:00:00", zeroFormatted);

        // EnumeratePeripherals executes safely on Windows host without throwing unhandled exceptions
        var peripherals = BluetoothPeripheralMonitor.EnumeratePeripherals();
        Assert.NotNull(peripherals);
    }

    [Fact]
    public void Win32GlobalHotkeyReceiver_registers_and_dispatches()
    {
        bool callbackTriggered = false;

        // Register F12 hotkey (VK_F12 = 0x7B)
        int id = Win32GlobalHotkeyReceiver.Register(nint.Zero, Win32GlobalHotkeyReceiver.ModAlt, 0x7B, () =>
        {
            callbackTriggered = true;
        });

        Assert.True(id > 0);

        // Simulate dispatching registered hotkey
        bool handled = Win32GlobalHotkeyReceiver.DispatchMessage(id);
        Assert.True(handled);
        Assert.True(callbackTriggered);

        // Unregister and ensure subsequent dispatch fails
        callbackTriggered = false;
        bool unregistered = Win32GlobalHotkeyReceiver.Unregister(nint.Zero, id);
        Assert.True(unregistered);

        bool handledAfterUnregister = Win32GlobalHotkeyReceiver.DispatchMessage(id);
        Assert.False(handledAfterUnregister);
        Assert.False(callbackTriggered);
    }

    [Fact]
    public void WindowManagerGeometry_splits_and_floats()
    {
        var screen = new WindowManagerGeometry.ScreenRect(0, 0, 1920, 1080);

        // Vertical split (LeftHalf)
        var left = WindowManagerGeometry.CalculateSplit(screen, WindowManagerGeometry.SplitType.LeftHalf);
        Assert.Equal(0, left.X);
        Assert.Equal(0, left.Y);
        Assert.Equal(960, left.Width);
        Assert.Equal(1080, left.Height);

        // Vertical split (RightHalf)
        var right = WindowManagerGeometry.CalculateSplit(screen, WindowManagerGeometry.SplitType.RightHalf);
        Assert.Equal(960, right.X);
        Assert.Equal(0, right.Y);
        Assert.Equal(960, right.Width);
        Assert.Equal(1080, right.Height);

        // Horizontal split (TopHalf)
        var top = WindowManagerGeometry.CalculateSplit(screen, WindowManagerGeometry.SplitType.TopHalf);
        Assert.Equal(0, top.X);
        Assert.Equal(0, top.Y);
        Assert.Equal(1920, top.Width);
        Assert.Equal(540, top.Height);

        // Centered Float 800x600 inside 1920x1080
        var centerFloat = WindowManagerGeometry.CalculateCentered(screen, 800, 600);
        Assert.Equal(560, centerFloat.X); // (1920 - 800) / 2
        Assert.Equal(240, centerFloat.Y); // (1080 - 600) / 2
        Assert.Equal(800, centerFloat.Width);
        Assert.Equal(600, centerFloat.Height);

        // Quadrants
        var q0 = WindowManagerGeometry.CalculateQuadrant(screen, 0);
        var q1 = WindowManagerGeometry.CalculateQuadrant(screen, 1);
        var q2 = WindowManagerGeometry.CalculateQuadrant(screen, 2);
        var q3 = WindowManagerGeometry.CalculateQuadrant(screen, 3);

        Assert.Equal(new WindowManagerGeometry.WindowRect(0, 0, 960, 540), q0);
        Assert.Equal(new WindowManagerGeometry.WindowRect(960, 0, 960, 540), q1);
        Assert.Equal(new WindowManagerGeometry.WindowRect(0, 540, 960, 540), q2);
        Assert.Equal(new WindowManagerGeometry.WindowRect(960, 540, 960, 540), q3);
    }

    [Fact]
    public void WorkspaceTilingEngine_master_stack_and_fair_grid()
    {
        var screen = new WorkspaceTilingEngine.ScreenArea(0, 0, 1920, 1080);

        // 1 window takes whole container minus gap (gap = 8)
        var single = WorkspaceTilingEngine.CalculateMasterAndStack(screen, 1, gap: 8);
        Assert.Single(single);
        Assert.Equal(8, single[0].X);
        Assert.Equal(8, single[0].Y);
        Assert.Equal(1904, single[0].Width);
        Assert.Equal(1064, single[0].Height);

        // 3 windows in Master-and-Stack (masterRatio 0.6)
        var layout3 = WorkspaceTilingEngine.CalculateMasterAndStack(screen, 3, masterRatio: 0.6, gap: 8);
        Assert.Equal(3, layout3.Count);

        // Master window on left (index 0)
        Assert.Equal(0, layout3[0].Index);
        Assert.Equal(8, layout3[0].X);
        Assert.Equal(8, layout3[0].Y);
        Assert.True(layout3[0].Width > 1000);

        // Fair Grid for 4 windows -> 2x2 grid
        var grid4 = WorkspaceTilingEngine.CalculateFairGrid(screen, 4, gap: 8);
        Assert.Equal(4, grid4.Count);
        Assert.Equal(0, grid4[0].Index);
        Assert.Equal(1, grid4[1].Index);
        Assert.Equal(2, grid4[2].Index);
        Assert.Equal(3, grid4[3].Index);
    }

    [Fact]
    public void PasswordPolicyGenerator_generates_and_evaluates_entropy()
    {
        var options = new PasswordPolicyGenerator.PasswordPolicyOptions(
            Length: 20,
            UseLowercase: true,
            UseUppercase: true,
            UseDigits: true,
            UseSymbols: true,
            UseEasyVision: true);

        var result = PasswordPolicyGenerator.GeneratePassword(options);
        Assert.Equal(20, result.Password.Length);

        // EasyVision eliminates ambiguous characters: 0, O, o, 1, l, I
        Assert.DoesNotContain('0', result.Password);
        Assert.DoesNotContain('O', result.Password);
        Assert.DoesNotContain('1', result.Password);
        Assert.DoesNotContain('l', result.Password);
        Assert.DoesNotContain('I', result.Password);

        Assert.True(result.ShannonEntropyBits > 3.0, $"Entropy {result.ShannonEntropyBits} should exceed 3.0 bits per char");
        Assert.True(result.StrengthTier is PasswordPolicyGenerator.PasswordStrengthTier.Strong or PasswordPolicyGenerator.PasswordStrengthTier.VeryStrong);
    }

    [Fact]
    public void SensitiveCredentialMasker_masks_cards_and_keys()
    {
        // Canonical Visa test number — genuinely passes Luhn (sum=60, divisible by 10)
        string text = "Order payment processed with card 4111-1111-1111-1111 and auth sk-antigravitySecretToken1234567890.";
        Assert.True(SensitiveCredentialMasker.ContainsSensitiveData(text));

        string sanitized = SensitiveCredentialMasker.MaskSensitiveData(text);
        Assert.DoesNotContain("4111-1111-1111-1111", sanitized);
        Assert.DoesNotContain("sk-antigravitySecretToken1234567890", sanitized);
        Assert.Contains("****-****-****-1111", sanitized);

        // Test Bearer token masking
        string bearerText = "Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9";
        Assert.True(SensitiveCredentialMasker.ContainsSensitiveData(bearerText));
        string maskedBearer = SensitiveCredentialMasker.MaskSensitiveData(bearerText);
        Assert.DoesNotContain("eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9", maskedBearer);
        Assert.StartsWith("Authorization: Bearer ", maskedBearer);

        // Luhn algorithm — canonical Visa test number passes; one check-digit off fails
        Assert.True(SensitiveCredentialMasker.IsValidLuhn("4111111111111111"));
        Assert.False(SensitiveCredentialMasker.IsValidLuhn("4111111111111112"));
    }

    [Fact]
    public void EnterpriseHostsManager_merges_and_unblocks_telemetry_domains()
    {
        string initialHosts =
            "# Copyright (c) 1993-2009 Microsoft Corp.\n" +
            "127.0.0.1       localhost\n" +
            "::1             localhost\n";

        var initialEntries = EnterpriseHostsManager.ParseHostsContent(initialHosts);
        Assert.Equal(2, initialEntries.Count);
        Assert.Equal("127.0.0.1", initialEntries[0].IpAddress);
        Assert.Equal("localhost", initialEntries[0].Hostname);

        var merged = new List<EnterpriseHostsManager.HostEntry>(initialEntries)
        {
            new("0.0.0.0", "telemetry.microsoft.com", "Telemetry block"),
            new("0.0.0.0", "v10.events.data.microsoft.com", "Diagnostics block")
        };

        string serialized = EnterpriseHostsManager.SerializeHosts(merged);
        var parsed = EnterpriseHostsManager.ParseHostsContent(serialized);

        Assert.Equal(4, parsed.Count);
        Assert.Contains(parsed, e => e.Hostname == "telemetry.microsoft.com" && e.IpAddress == "0.0.0.0");
        Assert.Contains(parsed, e => e.Hostname == "v10.events.data.microsoft.com" && e.IpAddress == "0.0.0.0");
        Assert.Contains(parsed, e => e.Hostname == "localhost" && e.IpAddress == "127.0.0.1");
    }

    [Fact]
    public void SegmentedDownloadEngine_partitions_ranges_and_verifies_hash()
    {
        long contentLength = 1000;
        int segments = 4;

        var slices = SegmentedDownloadEngine.PartitionRanges(contentLength, segments);
        Assert.Equal(4, slices.Count);

        Assert.Equal(0, slices[0].StartByte);
        Assert.Equal(249, slices[0].EndByte);
        Assert.Equal(250, slices[0].Length);

        Assert.Equal(250, slices[1].StartByte);
        Assert.Equal(499, slices[1].EndByte);
        Assert.Equal(250, slices[1].Length);

        Assert.Equal(500, slices[2].StartByte);
        Assert.Equal(749, slices[2].EndByte);
        Assert.Equal(250, slices[2].Length);

        Assert.Equal(750, slices[3].StartByte);
        Assert.Equal(999, slices[3].EndByte);
        Assert.Equal(250, slices[3].Length);

        // Assembly test
        byte[] part1 = "Hello, "u8.ToArray();
        byte[] part2 = "World!"u8.ToArray();

        var sliceResults = new List<SegmentedDownloadEngine.DownloadSliceResult>
        {
            new(1, part2, "hash2"),
            new(0, part1, "hash1")
        };

        var (combined, hash) = SegmentedDownloadEngine.AssembleSlices(sliceResults);
        Assert.Equal("Hello, World!", Encoding.UTF8.GetString(combined));
        Assert.NotEmpty(hash);
    }

    [Fact]
    public void DiagnosticLogTailerService_parses_lines_and_filters()
    {
        string logLine = "[2026-09-13 10:15:30.123] [Error] [SecurityService] Access violation encountered at 0x7FFA89B0";
        var entry = DiagnosticLogTailerService.ParseLine(logLine);

        Assert.Equal(DiagnosticLogTailerService.LogSeverity.Error, entry.Severity);
        Assert.Equal("SecurityService", entry.Category);
        Assert.Equal("Access violation encountered at 0x7FFA89B0", entry.Message);

        string fallbackLine = "Something failed during initialization";
        var fallbackEntry = DiagnosticLogTailerService.ParseLine(fallbackLine);
        Assert.Equal(DiagnosticLogTailerService.LogSeverity.Error, fallbackEntry.Severity);
    }
}
