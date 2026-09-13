namespace WinCare.Infrastructure.Native;

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

/// <summary>
/// Win32 Bluetooth peripheral device enumerator and telemetry prober (GalaxyBudsClient).
/// Queries paired and connected Bluetooth audio/HID accessories and their connection state.
/// </summary>
public static class BluetoothPeripheralMonitor
{
    private const string BthPropsDll = "bthprops.cpl";

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct BLUETOOTH_DEVICE_SEARCH_PARAMS
    {
        public uint dwSize;
        public int fReturnAuthenticated;
        public int fReturnRemembered;
        public int fReturnUnknown;
        public int fReturnConnected;
        public int fIssueInquiry;
        public byte cTimeoutMultiplier;
        public nint hRadio;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct BLUETOOTH_DEVICE_INFO
    {
        public uint dwSize;
        public ulong Address;
        public uint ulClassofDevice;
        public int fConnected;
        public int fRemembered;
        public int fAuthenticated;
        public SYSTEMTIME stLastSeen;
        public SYSTEMTIME stLastUsed;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 248)]
        public string szName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEMTIME
    {
        public ushort wYear;
        public ushort wMonth;
        public ushort wDayOfWeek;
        public ushort wDay;
        public ushort wHour;
        public ushort wMinute;
        public ushort wSecond;
        public ushort wMilliseconds;
    }

    [DllImport(BthPropsDll, EntryPoint = "BluetoothFindFirstDevice", SetLastError = true)]
    private static extern nint BluetoothFindFirstDevice(
        in BLUETOOTH_DEVICE_SEARCH_PARAMS searchParams,
        ref BLUETOOTH_DEVICE_INFO deviceInfo);

    [DllImport(BthPropsDll, EntryPoint = "BluetoothFindNextDevice", SetLastError = true)]
    private static extern bool BluetoothFindNextDevice(nint hFind, ref BLUETOOTH_DEVICE_INFO deviceInfo);

    [DllImport(BthPropsDll, EntryPoint = "BluetoothFindDeviceClose", SetLastError = true)]
    private static extern bool BluetoothFindDeviceClose(nint hFind);

    /// <summary>
    /// Telemetry snapshot for a discovered Bluetooth peripheral.
    /// </summary>
    public sealed record BluetoothPeripheral(
        string Name,
        string MacAddress,
        bool IsConnected,
        bool IsRemembered,
        bool IsAuthenticated,
        uint DeviceClass);

    /// <summary>
    /// Enumerates all remembered and connected Bluetooth devices on the system.
    /// </summary>
    public static IReadOnlyList<BluetoothPeripheral> EnumeratePeripherals()
    {
        var results = new List<BluetoothPeripheral>();
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return results;
        }

        try
        {
            var searchParams = new BLUETOOTH_DEVICE_SEARCH_PARAMS
            {
                dwSize = (uint)Marshal.SizeOf<BLUETOOTH_DEVICE_SEARCH_PARAMS>(),
                fReturnAuthenticated = 1,
                fReturnRemembered = 1,
                fReturnUnknown = 0,
                fReturnConnected = 1,
                fIssueInquiry = 0,
                cTimeoutMultiplier = 2,
                hRadio = 0
            };

            var deviceInfo = new BLUETOOTH_DEVICE_INFO
            {
                dwSize = (uint)Marshal.SizeOf<BLUETOOTH_DEVICE_INFO>()
            };

            nint hFind = BluetoothFindFirstDevice(in searchParams, ref deviceInfo);
            if (hFind == 0)
            {
                return results;
            }

            try
            {
                do
                {
                    string mac = FormatMacAddress(deviceInfo.Address);
                    results.Add(new BluetoothPeripheral(
                        Name: string.IsNullOrWhiteSpace(deviceInfo.szName) ? "Unknown Bluetooth Device" : deviceInfo.szName,
                        MacAddress: mac,
                        IsConnected: deviceInfo.fConnected != 0,
                        IsRemembered: deviceInfo.fRemembered != 0,
                        IsAuthenticated: deviceInfo.fAuthenticated != 0,
                        DeviceClass: deviceInfo.ulClassofDevice
                    ));

                    deviceInfo = new BLUETOOTH_DEVICE_INFO
                    {
                        dwSize = (uint)Marshal.SizeOf<BLUETOOTH_DEVICE_INFO>()
                    };
                } while (BluetoothFindNextDevice(hFind, ref deviceInfo));
            }
            finally
            {
                BluetoothFindDeviceClose(hFind);
            }
        }
        catch
        {
            // Fallback for systems where Bluetooth service or hardware is absent
        }

        return results;
    }

    /// <summary>
    /// Formats a 64-bit unsigned Bluetooth address into standard colon-separated hex format.
    /// </summary>
    public static string FormatMacAddress(ulong rawAddress)
    {
        byte[] bytes = BitConverter.GetBytes(rawAddress);
        return $"{bytes[5]:X2}:{bytes[4]:X2}:{bytes[3]:X2}:{bytes[2]:X2}:{bytes[1]:X2}:{bytes[0]:X2}";
    }
}
