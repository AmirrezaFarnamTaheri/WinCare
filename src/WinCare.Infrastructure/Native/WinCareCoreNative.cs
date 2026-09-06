using System.Runtime.InteropServices;

namespace WinCare.Infrastructure.Native;

[StructLayout(LayoutKind.Sequential)]
internal struct NativeSysSnapshot
{
    public float CpuUsagePct;
    public ulong RamUsedBytes;
    public ulong RamTotalBytes;
    public ulong DiskFreeBytes;
    public ulong DiskTotalBytes;
    public byte NetActive;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeCleanResult
{
    public ulong BytesReclaimed;
    public uint FilesRemoved;
    public int ErrorCode;
}

internal static class WinCareCoreNative
{
    private const string LibraryName = "wincare_core";

    [DllImport(LibraryName, EntryPoint = "wincare_core_abi_version", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern uint WinCareCoreAbiVersion();

    [DllImport(LibraryName, EntryPoint = "wincare_core_version", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern unsafe int WinCareCoreVersion(byte* buffer, nuint bufferLength, nuint* written);

    [DllImport(LibraryName, EntryPoint = "wincare_core_sha256_file", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern unsafe int WinCareCoreSha256File(
        byte* pathUtf8,
        nuint pathLength,
        ulong maxBytes,
        byte* output,
        nuint outputLength);

    [DllImport(LibraryName, EntryPoint = "wincare_core_dir_size", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern unsafe int WinCareCoreDirSize(
        byte* pathUtf8,
        nuint pathLength,
        ulong* sizeOut);

    [DllImport(LibraryName, EntryPoint = "wincare_core_sys_info", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern unsafe int WinCareCoreSysInfo(
        byte* buffer,
        nuint bufferLength,
        nuint* written);

    [DllImport(LibraryName, EntryPoint = "wincare_sys_snapshot_all", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern unsafe int WinCareSysSnapshotAll(NativeSysSnapshot* outSnapshot);

    [DllImport(LibraryName, EntryPoint = "wincare_clean_temp_files", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern unsafe int WinCareCleanTempFiles(byte dryRun, NativeCleanResult* outResult);
}
