using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace WinCare.Native
{
    public static class Productivity
    {
        private const uint SetDesktopWallpaper = 0x0014;
        private const uint UpdateIniFile = 0x01;
        private const uint SendChange = 0x02;

        [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SystemParametersInfo(uint action, uint parameter, string? value, uint flags);

        public static bool RefreshDesktopWallpaper()
        {
            if (!SystemParametersInfo(SetDesktopWallpaper, 0, null, UpdateIniFile | SendChange))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "SystemParametersInfo failed while refreshing the desktop wallpaper.");
            return true;
        }
    }
}
