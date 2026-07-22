using System;
using System.Runtime.InteropServices;

namespace WinCare.Native
{
    public static class Productivity
    {
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SystemParametersInfo(uint uiAction, uint uiParam, string pvParam, uint fWinIni);

        public static bool RefreshDesktopWallpaper()
        {
            return SystemParametersInfo(0x0014, 0, null, 0x01 | 0x02);
        }
    }
}
