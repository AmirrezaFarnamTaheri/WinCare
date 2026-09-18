namespace WinCare.Infrastructure.Native;

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

/// <summary>
/// Direct Win32 Power Scheme Governor utilizing PowrProf.dll APIs (AutoOS).
/// Provides zero-process-spawn, sub-millisecond enumeration, query, and switching of Windows power plans.
/// </summary>
public static class Win32PowerSchemeGovernor
{
    private const string PowrProfDll = "PowrProf.dll";

    // Well-known Windows power plan GUIDs
    public static readonly Guid HighPerformanceGuid = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
    public static readonly Guid BalancedGuid = new("381b4222-f694-41f0-9685-ff5bb260df2e");
    public static readonly Guid PowerSaverGuid = new("a1841308-3541-4fab-bc81-f71556f20b4a");
    public static readonly Guid UltimatePerformanceGuid = new("e9a42b02-d5df-448d-aa00-03f14749eb61");

    private const uint AccessScheme = 16;
    private const uint AccessSubgroup = 17;
    private const uint AccessIndividualSetting = 18;

    [DllImport(PowrProfDll, EntryPoint = "PowerGetActiveScheme", SetLastError = true)]
    private static extern unsafe uint PowerGetActiveScheme(nint userRootPowerKey, out Guid* activePolicyGuid);

    [DllImport(PowrProfDll, EntryPoint = "PowerSetActiveScheme", SetLastError = true)]
    private static extern uint PowerSetActiveScheme(nint userRootPowerKey, in Guid schemeGuid);

    [DllImport(PowrProfDll, EntryPoint = "PowerEnumerate", SetLastError = true)]
    private static extern unsafe uint PowerEnumerate(
        nint rootPowerKey,
        Guid* schemeGuid,
        Guid* subGroupOfPowerSettingsGuid,
        uint accessFlags,
        uint index,
        byte* buffer,
        ref uint bufferSize);

    [DllImport(PowrProfDll, EntryPoint = "PowerReadFriendlyName", SetLastError = true)]
    private static extern unsafe uint PowerReadFriendlyName(
        nint rootPowerKey,
        in Guid schemeGuid,
        Guid* subGroupOfPowerSettingsGuid,
        Guid* powerSettingGuid,
        byte* buffer,
        ref uint bufferSize);

    [DllImport(PowrProfDll, EntryPoint = "PowerReadACValueIndex", SetLastError = true)]
    private static extern uint PowerReadACValueIndex(
        nint rootPowerKey,
        in Guid schemeGuid,
        in Guid subGroupGuid,
        in Guid settingGuid,
        out uint acValueIndex);

    [DllImport(PowrProfDll, EntryPoint = "PowerWriteACValueIndex", SetLastError = true)]
    private static extern uint PowerWriteACValueIndex(
        nint rootPowerKey,
        in Guid schemeGuid,
        in Guid subGroupGuid,
        in Guid settingGuid,
        uint acValueIndex);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint LocalFree(nint hMem);

    /// <summary>
    /// Represents an enumerated power scheme.
    /// </summary>
    public sealed record PowerSchemeInfo(Guid SchemeGuid, string Name, bool IsActive);

    /// <summary>
    /// Queries the GUID of the currently active Windows power scheme.
    /// </summary>
    public static unsafe Guid GetActiveSchemeGuid()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return BalancedGuid;
        }

        try
        {
            uint result = PowerGetActiveScheme(0, out Guid* activeGuidPtr);
            if (result == 0 && activeGuidPtr != null)
            {
                try
                {
                    return *activeGuidPtr;
                }
                finally
                {
                    LocalFree((nint)activeGuidPtr);
                }
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or EntryPointNotFoundException or DllNotFoundException)
        {
            // A missing PowrProf export or an access denial means the scheme API is unavailable on
            // this host; unexpected marshalling faults are allowed to surface rather than being
            // converted into a silent "no active scheme".
            System.Diagnostics.Debug.WriteLine($"[Win32PowerSchemeGovernor] GetActiveSchemeGuid failed: {ex.Message}");
        }

        return Guid.Empty;
    }

    /// <summary>
    /// Enumerates all installed Windows power schemes on the system with their friendly names and active status.
    /// </summary>
    public static unsafe IReadOnlyList<PowerSchemeInfo> EnumerateSchemes()
    {
        var schemes = new List<PowerSchemeInfo>();
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            schemes.Add(new PowerSchemeInfo(BalancedGuid, "Balanced (Default)", true));
            schemes.Add(new PowerSchemeInfo(HighPerformanceGuid, "High Performance", false));
            schemes.Add(new PowerSchemeInfo(PowerSaverGuid, "Power Saver", false));
            return schemes;
        }

        Guid activeGuid = GetActiveSchemeGuid();
        uint index = 0;
        uint guidSize = (uint)sizeof(Guid);
        byte* guidBuffer = stackalloc byte[(int)guidSize];

        while (true)
        {
            uint currentSize = guidSize;
            uint res = PowerEnumerate(0, null, null, AccessScheme, index++, guidBuffer, ref currentSize);
            if (res != 0)
            {
                break;
            }

            // Never build a span wider than the 16-byte stack allocation: the ACCESS_SCHEME
            // contract always yields one GUID, but an unexpected native variant reporting a
            // larger buffer must not be trusted to read past it.
            if (currentSize == 0 || currentSize > guidSize)
            {
                break;
            }

            var schemeGuid = new Guid(new ReadOnlySpan<byte>(guidBuffer, (int)currentSize));
            string friendlyName = ReadFriendlyName(schemeGuid);
            if (string.IsNullOrWhiteSpace(friendlyName))
            {
                friendlyName = ResolveWellKnownName(schemeGuid);
            }

            schemes.Add(new PowerSchemeInfo(schemeGuid, friendlyName, schemeGuid == activeGuid));
        }

        if (schemes.Count == 0)
        {
            schemes.Add(new PowerSchemeInfo(BalancedGuid, "Balanced (Default)", true));
        }

        return schemes;
    }

    /// <summary>
    /// Reads the human-readable friendly name of a power scheme.
    /// </summary>
    public static unsafe string ReadFriendlyName(Guid schemeGuid)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return ResolveWellKnownName(schemeGuid);
        }

        try
        {
            uint bufferSize = 256;
            byte[] buffer = new byte[bufferSize];
            fixed (byte* pBuffer = buffer)
            {
                uint res = PowerReadFriendlyName(0, in schemeGuid, null, null, pBuffer, ref bufferSize);
                if (res == 0 && bufferSize > 0)
                {
                    return Encoding.Unicode.GetString(buffer, 0, (int)bufferSize).TrimEnd('\0');
                }
                else if (res == 234) // ERROR_MORE_DATA
                {
                    byte[] largerBuffer = new byte[bufferSize];
                    fixed (byte* pLarger = largerBuffer)
                    {
                        res = PowerReadFriendlyName(0, in schemeGuid, null, null, pLarger, ref bufferSize);
                        if (res == 0)
                        {
                            return Encoding.Unicode.GetString(largerBuffer, 0, (int)bufferSize).TrimEnd('\0');
                        }
                    }
                }
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or EntryPointNotFoundException or DllNotFoundException)
        {
            System.Diagnostics.Debug.WriteLine($"[Win32PowerSchemeGovernor] ReadFriendlyName failed for {schemeGuid}: {ex.Message}");
        }

        return ResolveWellKnownName(schemeGuid);
    }

    /// <summary>
    /// Sets the specified power scheme GUID as the active system scheme.
    /// </summary>
    public static bool SetActiveScheme(Guid schemeGuid)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return true;
        }

        try
        {
            uint result = PowerSetActiveScheme(0, in schemeGuid);
            return result == 0;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or EntryPointNotFoundException or DllNotFoundException)
        {
            System.Diagnostics.Debug.WriteLine($"[Win32PowerSchemeGovernor] SetActiveScheme failed for {schemeGuid}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Reads an AC value index for a specific power setting within a subgroup.
    /// </summary>
    public static bool TryReadAcValueIndex(Guid schemeGuid, Guid subGroupGuid, Guid settingGuid, out uint valueIndex)
    {
        valueIndex = 0;
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return false;
        }

        try
        {
            return PowerReadACValueIndex(0, in schemeGuid, in subGroupGuid, in settingGuid, out valueIndex) == 0;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or EntryPointNotFoundException or DllNotFoundException)
        {
            System.Diagnostics.Debug.WriteLine($"[Win32PowerSchemeGovernor] TryReadAcValueIndex failed for {settingGuid}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Writes an AC value index for a specific power setting within a subgroup.
    /// </summary>
    public static bool WriteAcValueIndex(Guid schemeGuid, Guid subGroupGuid, Guid settingGuid, uint valueIndex)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return false;
        }

        try
        {
            return PowerWriteACValueIndex(0, in schemeGuid, in subGroupGuid, in settingGuid, valueIndex) == 0;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or EntryPointNotFoundException or DllNotFoundException)
        {
            System.Diagnostics.Debug.WriteLine($"[Win32PowerSchemeGovernor] WriteAcValueIndex failed for {settingGuid}: {ex.Message}");
            return false;
        }
    }

    private static string ResolveWellKnownName(Guid guid)
    {
        if (guid == HighPerformanceGuid) return "High Performance";
        if (guid == BalancedGuid) return "Balanced";
        if (guid == PowerSaverGuid) return "Power Saver";
        if (guid == UltimatePerformanceGuid) return "Ultimate Performance";
        return $"Power Plan ({guid})";
    }
}
