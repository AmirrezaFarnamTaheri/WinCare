using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace WinCare.Infrastructure.Commands;

internal sealed partial class WindowsCommandExecutor
{
    internal static class HardwareTunerHelper
    {
        public sealed record PowerStatusSnapshot(
            bool Success,
            byte ACLineStatus,
            string PowerSource,
            byte BatteryFlag,
            byte BatteryLifePercent,
            bool IsCharging,
            bool HasBattery,
            bool BatterySaverActive,
            int RemainingLifeSeconds,
            string Summary);

        public sealed record PowerSchemeInfo(
            string ActiveSchemeGuid,
            string FriendlyName,
            IReadOnlyList<string> AvailableSchemes);

        public sealed record DisplayMetrics(
            bool Success,
            string DeviceName,
            uint Width,
            uint Height,
            uint RefreshRateHz,
            uint BitsPerPixel,
            string Summary);

        public static PowerStatusSnapshot ReadPowerStatus()
        {
            if (!WindowsInterop.GetSystemPowerStatus(out var status))
            {
                return new PowerStatusSnapshot(
                    Success: false,
                    ACLineStatus: 255,
                    PowerSource: "Unknown",
                    BatteryFlag: 255,
                    BatteryLifePercent: 255,
                    IsCharging: false,
                    HasBattery: false,
                    BatterySaverActive: false,
                    RemainingLifeSeconds: -1,
                    Summary: "Power status query unavailable.");
            }

            bool hasBattery = (status.BatteryFlag & 128) == 0 && status.BatteryFlag != 255 && status.BatteryLifePercent <= 100;
            bool isCharging = (status.BatteryFlag & 8) != 0;
            string powerSource = status.ACLineStatus switch
            {
                0 => "Battery",
                1 => "AC Online",
                _ => "Unknown"
            };

            string summary = hasBattery
                ? $"{status.BatteryLifePercent}% · {powerSource}{(isCharging ? " (Charging)" : string.Empty)}"
                : $"Desktop / AC Powered ({powerSource})";

            return new PowerStatusSnapshot(
                Success: true,
                ACLineStatus: status.ACLineStatus,
                PowerSource: powerSource,
                BatteryFlag: status.BatteryFlag,
                BatteryLifePercent: status.BatteryLifePercent,
                IsCharging: isCharging,
                HasBattery: hasBattery,
                BatterySaverActive: status.SystemStatusFlag == 1,
                RemainingLifeSeconds: status.BatteryLifeTime,
                Summary: summary);
        }

        public static PowerSchemeInfo GetActivePowerScheme()
        {
            const string subKey = @"SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes";
            using var key = Registry.LocalMachine.OpenSubKey(subKey);
            string activeGuid = key?.GetValue("ActivePowerScheme") as string ?? "381b4222-f694-41f0-9685-ff5bb260df2e";

            var available = new List<string>();
            if (key != null)
            {
                foreach (var name in key.GetSubKeyNames())
                {
                    available.Add(name);
                }
            }

            string friendlyName = ResolveSchemeName(activeGuid);

            return new PowerSchemeInfo(activeGuid, friendlyName, available.AsReadOnly());
        }

        public static DisplayMetrics GetDisplayMetrics()
        {
            var devMode = new WindowsInterop.DevMode
            {
                dmSize = (ushort)Marshal.SizeOf<WindowsInterop.DevMode>()
            };

            if (!WindowsInterop.EnumDisplaySettings(null, WindowsInterop.EnumCurrentSettings, ref devMode))
            {
                return new DisplayMetrics(
                    Success: false,
                    DeviceName: "Default Display",
                    Width: 0,
                    Height: 0,
                    RefreshRateHz: 60,
                    BitsPerPixel: 32,
                    Summary: "Display settings enumeration unavailable.");
            }

            string summary = $"{devMode.dmPelsWidth}x{devMode.dmPelsHeight} @ {devMode.dmDisplayFrequency}Hz ({devMode.dmBitsPerPel}-bit)";
            return new DisplayMetrics(
                Success: true,
                DeviceName: string.IsNullOrWhiteSpace(devMode.dmDeviceName) ? "Primary Display" : devMode.dmDeviceName,
                Width: devMode.dmPelsWidth,
                Height: devMode.dmPelsHeight,
                RefreshRateHz: devMode.dmDisplayFrequency,
                BitsPerPixel: devMode.dmBitsPerPel,
                Summary: summary);
        }

        private static string ResolveSchemeName(string guid) =>
            guid.Trim('{', '}').ToLowerInvariant() switch
            {
                "381b4222-f694-41f0-9685-ff5bb260df2e" => "Balanced",
                "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c" => "High Performance",
                "a1841308-3541-4fab-bc81-f71556f20b4a" => "Power Saver",
                "e9a42b02-d5df-448d-aa00-03f14749eb61" => "Ultimate Performance",
                _ => "Custom Power Scheme"
            };
    }
}
