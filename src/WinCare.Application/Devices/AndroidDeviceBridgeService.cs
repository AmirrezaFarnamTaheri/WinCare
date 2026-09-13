namespace WinCare.Application.Devices;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

/// <summary>
/// Android Debug Bridge (ADB) discovery, device enumeration, and storage diagnostics (AdbFileManager).
/// Inspects Android devices, emulators, and WSA subsystems for staging residues and partition metrics.
/// </summary>
public static class AndroidDeviceBridgeService
{
    public sealed record AndroidDevice(
        string Serial,
        string State,
        string Model,
        string Product,
        bool IsEmulator);

    public sealed record StoragePartition(
        string MountPoint,
        long TotalBytes,
        long UsedBytes,
        long AvailableBytes,
        double UsedPercentage);

    private static readonly Regex DeviceLineRegex = new(
        @"^(?<serial>[^\s]+)\s+(?<state>device|offline|unauthorized|recovery)(?:\s+product:(?<product>[^\s]+))?(?:\s+model:(?<model>[^\s]+))?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Locates the active adb.exe executable on the system across SDK paths, WSA directories, and standard locations.
    /// </summary>
    public static string? LocateAdbExecutable()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

        string[] candidatePaths =
        [
            Path.Combine(localAppData, "Android", "Sdk", "platform-tools", "adb.exe"),
            Path.Combine(programFiles, "Android", "platform-tools", "adb.exe"),
            Path.Combine(localAppData, "Programs", "WSA", "adb.exe")
        ];

        foreach (var path in candidatePaths)
        {
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    /// <summary>
    /// Parses raw 'adb devices -l' console output into strongly-typed <see cref="AndroidDevice"/> records.
    /// </summary>
    public static IReadOnlyList<AndroidDevice> ParseAdbDevicesOutput(string output)
    {
        var devices = new List<AndroidDevice>();
        if (string.IsNullOrWhiteSpace(output)) return devices;

        using var reader = new StringReader(output);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            string trimmed = line.Trim();
            if (trimmed.StartsWith("List of devices", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(trimmed))
            {
                continue;
            }

            var match = DeviceLineRegex.Match(trimmed);
            if (match.Success)
            {
                string serial = match.Groups["serial"].Value;
                string state = match.Groups["state"].Value;
                string product = match.Groups["product"].Success ? match.Groups["product"].Value : "generic";
                string model = match.Groups["model"].Success ? match.Groups["model"].Value : "Android Device";
                bool isEmulator = serial.StartsWith("emulator-", StringComparison.OrdinalIgnoreCase) || serial.StartsWith("127.0.0.1:", StringComparison.OrdinalIgnoreCase);

                devices.Add(new AndroidDevice(serial, state, model, product, isEmulator));
            }
        }

        return devices;
    }

    /// <summary>
    /// Parses raw 'df -k' or 'df' console output from Android device into <see cref="StoragePartition"/> records.
    /// </summary>
    public static IReadOnlyList<StoragePartition> ParseDfOutput(string output)
    {
        var partitions = new List<StoragePartition>();
        if (string.IsNullOrWhiteSpace(output)) return partitions;

        using var reader = new StringReader(output);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            string trimmed = line.Trim();
            if (trimmed.StartsWith("Filesystem", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(trimmed))
            {
                continue;
            }

            var parts = trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 6)
            {
                // Format: Filesystem 1K-blocks Used Available Use% Mounted on
                if (long.TryParse(parts[1], out long totalKb) &&
                    long.TryParse(parts[2], out long usedKb) &&
                    long.TryParse(parts[3], out long availKb))
                {
                    string mount = parts[5];
                    double pct = totalKb > 0 ? (usedKb * 100.0) / totalKb : 0.0;
                    partitions.Add(new StoragePartition(
                        MountPoint: mount,
                        TotalBytes: totalKb * 1024,
                        UsedBytes: usedKb * 1024,
                        AvailableBytes: availKb * 1024,
                        UsedPercentage: Math.Round(pct, 1)));
                }
            }
        }

        return partitions;
    }
}
