using System.Runtime.InteropServices;
using System.Text;

namespace WinCare.Infrastructure.Native;

/// <summary>
/// Immutable snapshot of hardware firmware details parsed directly from raw SMBIOS firmware tables.
/// Bypasses WMI / CIM overhead and functions in PE and Safe Mode environments.
/// </summary>
public sealed record SmbiosFirmwareInfo(
    string BiosVendor,
    string BiosVersion,
    string BiosReleaseDate,
    string SystemManufacturer,
    string SystemProductName,
    string SystemVersion,
    string SystemSerialNumber,
    string SystemUuid,
    string BaseboardManufacturer,
    string BaseboardProduct,
    string BaseboardVersion,
    string BaseboardSerialNumber,
    string ProcessorSocket,
    string ProcessorManufacturer,
    string ProcessorVersion,
    uint ProcessorMaxSpeedMHz,
    IReadOnlyList<SmbiosMemoryModule> MemoryModules);

/// <summary>
/// Physical memory module details from SMBIOS Type 17 records.
/// </summary>
public sealed record SmbiosMemoryModule(
    string DeviceLocator,
    string BankLocator,
    string Manufacturer,
    string SerialNumber,
    string PartNumber,
    ulong CapacityBytes,
    uint SpeedMHz);

/// <summary>
/// Direct Win32 SMBIOS firmware table reader and parser.
/// </summary>
public static class FirmwareTableParser
{
    // Signature for raw SMBIOS firmware table provider: 'RSMB' (0x52534D42)
    private const uint ProviderRsmb = 0x52534D42;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint GetSystemFirmwareTable(
        uint firmwareTableProviderSignature,
        uint firmwareTableID,
        IntPtr pFirmwareTableBuffer,
        uint bufferSize);

    /// <summary>
    /// Reads and parses SMBIOS tables into a high-level strongly typed <see cref="SmbiosFirmwareInfo"/>.
    /// </summary>
    public static SmbiosFirmwareInfo ReadFirmwareInfo()
    {
        byte[]? rawBytes = ReadRawSmbiosTable();
        if (rawBytes == null || rawBytes.Length < 8)
        {
            return CreateFallback("Unavailable");
        }

        return ParseSmbiosData(rawBytes);
    }

    /// <summary>
    /// Reads raw SMBIOS table bytes using kernel32!GetSystemFirmwareTable.
    /// </summary>
    public static byte[]? ReadRawSmbiosTable()
    {
        uint needed = GetSystemFirmwareTable(ProviderRsmb, 0, IntPtr.Zero, 0);
        if (needed == 0)
        {
            return null;
        }

        byte[] buffer = new byte[needed];
        GCHandle handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            uint written = GetSystemFirmwareTable(ProviderRsmb, 0, handle.AddrOfPinnedObject(), needed);
            if (written == 0 || written > needed)
            {
                return null;
            }

            return buffer;
        }
        finally
        {
            handle.Free();
        }
    }

    /// <summary>
    /// Parses raw SMBIOS byte buffer (skipping 8-byte RawSMBIOSData header).
    /// </summary>
    public static SmbiosFirmwareInfo ParseSmbiosData(byte[] data)
    {
        if (data.Length < 8)
        {
            return CreateFallback("BufferTooShort");
        }

        // Header: byte 0: Used20CallingMethod, byte 1: MajorVersion, byte 2: MinorVersion, byte 3: DmiRevision, uint 4..7: Length
        int offset = 8;
        int totalLen = data.Length;

        string biosVendor = "";
        string biosVersion = "";
        string biosDate = "";

        string sysManufacturer = "";
        string sysProduct = "";
        string sysVersion = "";
        string sysSerial = "";
        string sysUuid = "";

        string boardManufacturer = "";
        string boardProduct = "";
        string boardVersion = "";
        string boardSerial = "";

        string procSocket = "";
        string procManufacturer = "";
        string procVersion = "";
        uint procMaxSpeed = 0;

        List<SmbiosMemoryModule> memoryModules = new();

        while (offset + 4 <= totalLen)
        {
            byte type = data[offset];
            byte formattedLen = data[offset + 1];
            if (formattedLen < 4 || offset + formattedLen > totalLen)
            {
                break;
            }

            // End of Table marker (Type 127)
            if (type == 127)
            {
                break;
            }

            // Extract strings in unformatted section immediately following formatted area
            int stringStart = offset + formattedLen;
            List<string> strings = new();
            int curStr = stringStart;

            while (curStr < totalLen)
            {
                if (data[curStr] == 0)
                {
                    if (curStr == stringStart && (curStr + 1 >= totalLen || data[curStr + 1] == 0))
                    {
                        // No strings present
                        curStr++;
                        break;
                    }

                    int strLen = curStr - stringStart;
                    if (strLen > 0)
                    {
                        strings.Add(Encoding.ASCII.GetString(data, stringStart, strLen).Trim());
                    }

                    stringStart = curStr + 1;
                    if (stringStart < totalLen && data[stringStart] == 0)
                    {
                        curStr = stringStart + 1;
                        break;
                    }
                }
                curStr++;
            }

            string GetString(int index)
            {
                if (index > 0 && index <= strings.Count)
                {
                    return strings[index - 1];
                }
                return "";
            }

            switch (type)
            {
                case 0: // BIOS Information
                    if (formattedLen >= 8)
                    {
                        biosVendor = GetString(data[offset + 4]);
                        biosVersion = GetString(data[offset + 5]);
                        biosDate = GetString(data[offset + 8]);
                    }
                    break;

                case 1: // System Information
                    if (formattedLen >= 8)
                    {
                        sysManufacturer = GetString(data[offset + 4]);
                        sysProduct = GetString(data[offset + 5]);
                        sysVersion = GetString(data[offset + 6]);
                        if (formattedLen >= 8) sysSerial = GetString(data[offset + 7]);
                        if (formattedLen >= 24)
                        {
                            byte[] uuidBytes = new byte[16];
                            Array.Copy(data, offset + 8, uuidBytes, 0, 16);
                            sysUuid = new Guid(uuidBytes).ToString();
                        }
                    }
                    break;

                case 2: // Baseboard Information
                    if (formattedLen >= 8)
                    {
                        boardManufacturer = GetString(data[offset + 4]);
                        boardProduct = GetString(data[offset + 5]);
                        boardVersion = GetString(data[offset + 6]);
                        boardSerial = GetString(data[offset + 7]);
                    }
                    break;

                case 4: // Processor Information
                    if (formattedLen >= 20)
                    {
                        procSocket = GetString(data[offset + 4]);
                        procManufacturer = GetString(data[offset + 7]);
                        procVersion = GetString(data[offset + 16]);
                        if (formattedLen >= 22)
                        {
                            procMaxSpeed = BitConverter.ToUInt16(data, offset + 20);
                        }
                    }
                    break;

                case 17: // Memory Device
                    if (formattedLen >= 28)
                    {
                        ushort sizeRaw = BitConverter.ToUInt16(data, offset + 12);
                        ulong capacity = 0;
                        if (sizeRaw != 0 && sizeRaw != 0xFFFF)
                        {
                            if ((sizeRaw & 0x8000) != 0)
                            {
                                capacity = (ulong)(sizeRaw & 0x7FFF) * 1024; // KB
                            }
                            else
                            {
                                capacity = (ulong)sizeRaw * 1024 * 1024; // MB
                            }
                        }

                        string devLocator = GetString(data[offset + 16]);
                        string bankLocator = GetString(data[offset + 17]);
                        uint speed = formattedLen >= 24 ? (uint)BitConverter.ToUInt16(data, offset + 21) : 0u;
                        string manufacturer = formattedLen >= 28 ? GetString(data[offset + 23]) : "";
                        string serial = formattedLen >= 29 ? GetString(data[offset + 24]) : "";
                        string part = formattedLen >= 31 ? GetString(data[offset + 26]) : "";

                        if (capacity > 0 || !string.IsNullOrWhiteSpace(devLocator))
                        {
                            memoryModules.Add(new SmbiosMemoryModule(
                                devLocator, bankLocator, manufacturer, serial, part, capacity, speed));
                        }
                    }
                    break;
            }

            offset = curStr;
        }

        return new SmbiosFirmwareInfo(
            string.IsNullOrWhiteSpace(biosVendor) ? "Unknown" : biosVendor,
            string.IsNullOrWhiteSpace(biosVersion) ? "Unknown" : biosVersion,
            string.IsNullOrWhiteSpace(biosDate) ? "Unknown" : biosDate,
            string.IsNullOrWhiteSpace(sysManufacturer) ? "Unknown" : sysManufacturer,
            string.IsNullOrWhiteSpace(sysProduct) ? "Unknown" : sysProduct,
            string.IsNullOrWhiteSpace(sysVersion) ? "Unknown" : sysVersion,
            string.IsNullOrWhiteSpace(sysSerial) ? "Unknown" : sysSerial,
            string.IsNullOrWhiteSpace(sysUuid) ? "Unknown" : sysUuid,
            string.IsNullOrWhiteSpace(boardManufacturer) ? "Unknown" : boardManufacturer,
            string.IsNullOrWhiteSpace(boardProduct) ? "Unknown" : boardProduct,
            string.IsNullOrWhiteSpace(boardVersion) ? "Unknown" : boardVersion,
            string.IsNullOrWhiteSpace(boardSerial) ? "Unknown" : boardSerial,
            string.IsNullOrWhiteSpace(procSocket) ? "Unknown" : procSocket,
            string.IsNullOrWhiteSpace(procManufacturer) ? "Unknown" : procManufacturer,
            string.IsNullOrWhiteSpace(procVersion) ? "Unknown" : procVersion,
            procMaxSpeed,
            memoryModules);
    }

    private static SmbiosFirmwareInfo CreateFallback(string status) =>
        new(status, status, status, status, status, status, status, status,
            status, status, status, status, status, status, status, 0, Array.Empty<SmbiosMemoryModule>());
}
