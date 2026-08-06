using System.Runtime.InteropServices;

namespace WinCare.Infrastructure.Native;

internal static class WinCareCoreNative
{
    private const string LibraryName = "wincare_core";

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern uint wincare_core_abi_version();

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern unsafe int wincare_core_version(byte* buffer, nuint bufferLength, nuint* written);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    internal static extern unsafe int wincare_core_sha256_file(
        byte* pathUtf8,
        nuint pathLength,
        ulong maxBytes,
        byte* output,
        nuint outputLength);
}
