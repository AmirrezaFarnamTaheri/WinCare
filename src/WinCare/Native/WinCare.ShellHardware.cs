using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace WinCare.Native
{
    public sealed class MonitorVcpRecord
    {
        public string DeviceName { get; set; }
        public int PhysicalIndex { get; set; }
        public string Description { get; set; }
        public byte FeatureCode { get; set; }
        public uint CurrentValue { get; set; }
        public uint MaximumValue { get; set; }
        public bool Supported { get; set; }
        public int Win32Error { get; set; }
    }

    public sealed class ProcessPackageRecord
    {
        public int ProcessId { get; set; }
        public string PackageFullName { get; set; }
        public int Win32Error { get; set; }
    }

    public static class ShellHardware
    {
        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
        private const int ERROR_INSUFFICIENT_BUFFER = 122;
        private const int APPMODEL_ERROR_NO_PACKAGE = 15700;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct MONITORINFOEX
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szDevice;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct PHYSICAL_MONITOR
        {
            public IntPtr hPhysicalMonitor;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szPhysicalMonitorDescription;
        }

        private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

        [DllImport("user32.dll")]
        private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX info);

        [DllImport("dxva2.dll", SetLastError = true)]
        private static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, out uint count);

        [DllImport("dxva2.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, uint count, [Out] PHYSICAL_MONITOR[] monitors);

        [DllImport("dxva2.dll", SetLastError = true)]
        private static extern bool DestroyPhysicalMonitors(uint count, [In] PHYSICAL_MONITOR[] monitors);

        [DllImport("dxva2.dll", SetLastError = true)]
        private static extern bool GetVCPFeatureAndVCPFeatureReply(IntPtr monitor, byte code, out uint type, out uint current, out uint maximum);

        [DllImport("dxva2.dll", SetLastError = true)]
        private static extern bool SetVCPFeature(IntPtr monitor, byte code, uint value);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetPackageFullName(IntPtr process, ref int packageFullNameLength, StringBuilder packageFullName);

        [ComImport, Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")]
        private class ApplicationActivationManagerClass { }

        [ComImport, Guid("2E941141-7F97-4756-BA1D-9DECDE894A3D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IApplicationActivationManager
        {
            [PreserveSig]
            int ActivateApplication([MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
                [MarshalAs(UnmanagedType.LPWStr)] string arguments, uint options, out uint processId);
            [PreserveSig]
            int ActivateForFile([MarshalAs(UnmanagedType.LPWStr)] string appUserModelId, IntPtr itemArray,
                [MarshalAs(UnmanagedType.LPWStr)] string verb, out uint processId);
            [PreserveSig]
            int ActivateForProtocol([MarshalAs(UnmanagedType.LPWStr)] string appUserModelId, IntPtr itemArray,
                out uint processId);
        }

        private sealed class LogicalMonitor
        {
            public IntPtr Handle;
            public string DeviceName;
        }

        private static System.Collections.Generic.List<LogicalMonitor> EnumerateLogicalMonitors()
        {
            var result = new System.Collections.Generic.List<LogicalMonitor>();
            MonitorEnumProc callback = delegate (IntPtr hMonitor, IntPtr hdc, ref RECT rect, IntPtr data)
            {
                MONITORINFOEX info = new MONITORINFOEX();
                info.cbSize = Marshal.SizeOf(typeof(MONITORINFOEX));
                if (GetMonitorInfo(hMonitor, ref info))
                    result.Add(new LogicalMonitor { Handle = hMonitor, DeviceName = info.szDevice ?? "" });
                return true;
            };
            if (!EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "EnumDisplayMonitors failed.");
            return result;
        }

        private static MonitorVcpRecord ReadFeature(string deviceName, int physicalIndex, string description, IntPtr handle, byte code)
        {
            uint type, current, maximum;
            if (GetVCPFeatureAndVCPFeatureReply(handle, code, out type, out current, out maximum))
            {
                return new MonitorVcpRecord
                {
                    DeviceName = deviceName,
                    PhysicalIndex = physicalIndex,
                    Description = description ?? "",
                    FeatureCode = code,
                    CurrentValue = current,
                    MaximumValue = maximum,
                    Supported = true,
                    Win32Error = 0
                };
            }
            return new MonitorVcpRecord
            {
                DeviceName = deviceName,
                PhysicalIndex = physicalIndex,
                Description = description ?? "",
                FeatureCode = code,
                Supported = false,
                Win32Error = Marshal.GetLastWin32Error()
            };
        }

        public static MonitorVcpRecord[] EnumerateMonitorVcpFeatures()
        {
            var records = new System.Collections.Generic.List<MonitorVcpRecord>();
            foreach (var logical in EnumerateLogicalMonitors())
            {
                uint count;
                if (!GetNumberOfPhysicalMonitorsFromHMONITOR(logical.Handle, out count) || count == 0 || count > 64)
                    continue;
                var physical = new PHYSICAL_MONITOR[checked((int)count)];
                if (!GetPhysicalMonitorsFromHMONITOR(logical.Handle, count, physical))
                    continue;
                try
                {
                    for (int i = 0; i < physical.Length; i++)
                    {
                        records.Add(ReadFeature(logical.DeviceName, i, physical[i].szPhysicalMonitorDescription, physical[i].hPhysicalMonitor, 0x10));
                        records.Add(ReadFeature(logical.DeviceName, i, physical[i].szPhysicalMonitorDescription, physical[i].hPhysicalMonitor, 0x12));
                    }
                }
                finally { DestroyPhysicalMonitors(count, physical); }
            }
            return records.ToArray();
        }

        public static MonitorVcpRecord SetMonitorVcpFeature(string deviceName, int physicalIndex, string expectedDescription, byte featureCode,
            uint value, uint expectedCurrent, uint expectedMaximum)
        {
            foreach (var logical in EnumerateLogicalMonitors())
            {
                if (!String.Equals(logical.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase)) continue;
                uint count;
                if (!GetNumberOfPhysicalMonitorsFromHMONITOR(logical.Handle, out count) || count == 0 || count > 64)
                    throw new InvalidOperationException("Physical monitor enumeration failed.");
                var physical = new PHYSICAL_MONITOR[checked((int)count)];
                if (!GetPhysicalMonitorsFromHMONITOR(logical.Handle, count, physical))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Physical monitor enumeration failed.");
                try
                {
                    if (physicalIndex < 0 || physicalIndex >= physical.Length)
                        throw new InvalidOperationException("Physical monitor index changed after preview.");
                    if (!String.Equals(physical[physicalIndex].szPhysicalMonitorDescription ?? "", expectedDescription ?? "", StringComparison.Ordinal))
                        throw new InvalidOperationException("Physical monitor description changed after preview.");
                    var before = ReadFeature(logical.DeviceName, physicalIndex, physical[physicalIndex].szPhysicalMonitorDescription,
                        physical[physicalIndex].hPhysicalMonitor, featureCode);
                    if (!before.Supported) throw new Win32Exception(before.Win32Error, "The requested VCP feature is unavailable.");
                    if (before.CurrentValue != expectedCurrent || before.MaximumValue != expectedMaximum)
                        throw new InvalidOperationException("Monitor VCP state changed after preview.");
                    if (value > before.MaximumValue) throw new ArgumentOutOfRangeException("value");
                    if (!SetVCPFeature(physical[physicalIndex].hPhysicalMonitor, featureCode, value))
                        throw new Win32Exception(Marshal.GetLastWin32Error(), "SetVCPFeature failed.");
                    var after = ReadFeature(logical.DeviceName, physicalIndex, physical[physicalIndex].szPhysicalMonitorDescription,
                        physical[physicalIndex].hPhysicalMonitor, featureCode);
                    if (!after.Supported || after.CurrentValue != value)
                        throw new InvalidOperationException("Monitor VCP update could not be verified.");
                    return after;
                }
                finally { DestroyPhysicalMonitors(count, physical); }
            }
            throw new InvalidOperationException("Logical monitor identity changed after preview.");
        }

        public static ProcessPackageRecord GetProcessPackage(int processId)
        {
            ProcessPackageRecord result = new ProcessPackageRecord { ProcessId = processId };
            IntPtr handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
            if (handle == IntPtr.Zero) { result.Win32Error = Marshal.GetLastWin32Error(); return result; }
            try
            {
                int length = 0;
                int status = GetPackageFullName(handle, ref length, null);
                if (status == APPMODEL_ERROR_NO_PACKAGE) return result;
                if (status != ERROR_INSUFFICIENT_BUFFER || length <= 0 || length > 32768)
                { result.Win32Error = status; return result; }
                var builder = new StringBuilder(length);
                status = GetPackageFullName(handle, ref length, builder);
                if (status == 0) result.PackageFullName = builder.ToString();
                else result.Win32Error = status;
                return result;
            }
            finally { CloseHandle(handle); }
        }

        public static int ActivateApplication(string appUserModelId)
        {
            if (String.IsNullOrWhiteSpace(appUserModelId) || appUserModelId.Length > 1024)
                throw new ArgumentException("Application user model ID is invalid.");
            var manager = (IApplicationActivationManager)new ApplicationActivationManagerClass();
            try
            {
                uint processId;
                int hr = manager.ActivateApplication(appUserModelId, null, 0, out processId);
                if (hr < 0) Marshal.ThrowExceptionForHR(hr);
                return checked((int)processId);
            }
            finally
            {
                if (Marshal.IsComObject(manager)) Marshal.FinalReleaseComObject(manager);
            }
        }
    }
}




