using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace WinCare.Infrastructure.Security;

/// <summary>
/// Scoped helper that enables Win32 token privileges (such as SeBackupPrivilege, SeRestorePrivilege,
/// or SeTakeOwnershipPrivilege) on the current process token for the duration of the scope, and
/// safely restores the previous privilege states upon disposal.
/// </summary>
public sealed class TokenPrivilegeScope : IDisposable
{
    public const string SeBackupPrivilege = "SeBackupPrivilege";
    public const string SeRestorePrivilege = "SeRestorePrivilege";
    public const string SeTakeOwnershipPrivilege = "SeTakeOwnershipPrivilege";
    public const string SeShutdownPrivilege = "SeShutdownPrivilege";
    public const string SeDebugPrivilege = "SeDebugPrivilege";

    private const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
    private const uint TOKEN_QUERY = 0x0008;
    private const uint SE_PRIVILEGE_ENABLED = 0x00000002;

    private readonly SafeAccessTokenHandle? _tokenHandle;
    private readonly List<(LUID Luid, uint PreviousAttributes)> _revertList = new();
    private bool _disposed;

    public TokenPrivilegeScope(params string[] privileges)
    {
        if (privileges is null || privileges.Length == 0)
        {
            return;
        }

        if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out SafeAccessTokenHandle tokenHandle))
        {
            return;
        }

        _tokenHandle = tokenHandle;

        foreach (string privilege in privileges)
        {
            if (string.IsNullOrWhiteSpace(privilege))
            {
                continue;
            }

            if (!LookupPrivilegeValue(null, privilege, out LUID luid))
            {
                continue;
            }

            TOKEN_PRIVILEGES newState = new()
            {
                PrivilegeCount = 1,
                Privileges = [new LUID_AND_ATTRIBUTES { Luid = luid, Attributes = SE_PRIVILEGE_ENABLED }]
            };

            TOKEN_PRIVILEGES previousState = new()
            {
                PrivilegeCount = 1,
                Privileges = [new LUID_AND_ATTRIBUTES()]
            };

            if (AdjustTokenPrivileges(
                _tokenHandle,
                DisableAllPrivileges: false,
                ref newState,
                (uint)Marshal.SizeOf<TOKEN_PRIVILEGES>(),
                ref previousState,
                out _))
            {
                if (previousState.PrivilegeCount > 0)
                {
                    _revertList.Add((luid, previousState.Privileges[0].Attributes));
                }
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_tokenHandle is not null && !_tokenHandle.IsInvalid && !_tokenHandle.IsClosed && _revertList.Count > 0)
        {
            foreach (var (luid, prevAttr) in _revertList)
            {
                TOKEN_PRIVILEGES revertState = new()
                {
                    PrivilegeCount = 1,
                    Privileges = [new LUID_AND_ATTRIBUTES { Luid = luid, Attributes = prevAttr }]
                };

                AdjustTokenPrivileges(
                    _tokenHandle,
                    DisableAllPrivileges: false,
                    ref revertState,
                    0,
                    IntPtr.Zero,
                    out _);
            }
        }

        _tokenHandle?.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID_AND_ATTRIBUTES
    {
        public LUID Luid;
        public uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TOKEN_PRIVILEGES
    {
        public uint PrivilegeCount;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1)]
        public LUID_AND_ATTRIBUTES[] Privileges;
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(
        IntPtr ProcessHandle,
        uint DesiredAccess,
        out SafeAccessTokenHandle TokenHandle);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool LookupPrivilegeValue(
        string? lpSystemName,
        string lpName,
        out LUID lpLuid);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool AdjustTokenPrivileges(
        SafeAccessTokenHandle TokenHandle,
        bool DisableAllPrivileges,
        ref TOKEN_PRIVILEGES NewState,
        uint BufferLength,
        ref TOKEN_PRIVILEGES PreviousState,
        out uint ReturnLength);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool AdjustTokenPrivileges(
        SafeAccessTokenHandle TokenHandle,
        bool DisableAllPrivileges,
        ref TOKEN_PRIVILEGES NewState,
        uint BufferLength,
        IntPtr PreviousState,
        out uint ReturnLength);
}