using System.Runtime.InteropServices;
using System.Text;
using WinCare.Application.Commands;
using WinCare.Domain.Commands;

namespace WinCare.Infrastructure.Commands;

/// <summary>
/// Native Windows Installer (MSI/MSP) verification and SQUID packed GUID decoding routines.
/// </summary>
internal sealed partial class WindowsCommandExecutor
{
    internal static class MsiPackageHelper
    {
        private const uint MSIINSTALLCONTEXT_ALL = 7;
        private const int ERROR_SUCCESS = 0;
        private const int ERROR_NO_MORE_ITEMS = 259;

        [DllImport("msi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern uint MsiEnumProductsExW(
            string? szProductCode,
            string? szUserSid,
            uint dwContext,
            uint dwIndex,
            [Out] StringBuilder lpInstalledProductCode,
            out uint pdwInstalledContext,
            [Out] StringBuilder? lpInstalledUserSid,
            ref uint pcchInstalledUserSid);

        [DllImport("msi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern uint MsiGetProductInfoW(
            string szProduct,
            string szAttribute,
            [Out] StringBuilder lpValueBuf,
            ref uint pcchValueBuf);

        /// <summary>
        /// Compresses a standard 36-character hyphenated GUID into a 32-character SQUID
        /// (Small Quasi-Unique Identifier) used by the Windows Installer registry keys.
        /// </summary>
        public static string GuidToSquid(Guid guid)
        {
            string raw = guid.ToString("N");
            if (raw.Length != 32)
            {
                return raw;
            }

            var sb = new StringBuilder(32);

            // Block 1: 8 hex chars reversed
            for (int i = 7; i >= 0; i--) sb.Append(raw[i]);

            // Block 2: 4 hex chars reversed
            for (int i = 11; i >= 8; i--) sb.Append(raw[i]);

            // Block 3: 4 hex chars reversed
            for (int i = 15; i >= 12; i--) sb.Append(raw[i]);

            // Block 4: 4 hex chars, byte-pair reversed
            sb.Append(raw[17]).Append(raw[16]);
            sb.Append(raw[19]).Append(raw[18]);

            // Block 5: 12 hex chars, byte-pair reversed
            for (int i = 20; i < 32; i += 2)
            {
                sb.Append(raw[i + 1]).Append(raw[i]);
            }

            return sb.ToString().ToUpperInvariant();
        }

        /// <summary>
        /// Reverses a 32-character SQUID back into a standard System.Guid.
        /// </summary>
        public static bool TrySquidToGuid(string squid, out Guid guid)
        {
            guid = Guid.Empty;
            if (string.IsNullOrWhiteSpace(squid) || squid.Length != 32)
            {
                return false;
            }

            var sb = new StringBuilder(32);

            // Block 1
            for (int i = 7; i >= 0; i--) sb.Append(squid[i]);
            // Block 2
            for (int i = 11; i >= 8; i--) sb.Append(squid[i]);
            // Block 3
            for (int i = 15; i >= 12; i--) sb.Append(squid[i]);
            // Block 4
            sb.Append(squid[17]).Append(squid[16]);
            sb.Append(squid[19]).Append(squid[18]);
            // Block 5
            for (int i = 20; i < 32; i += 2)
            {
                sb.Append(squid[i + 1]).Append(squid[i]);
            }

            return Guid.TryParse(sb.ToString(), out guid);
        }

        /// <summary>
        /// Reads registered Windows Installer product property (e.g. ProductName, Publisher, VersionString).
        /// </summary>
        public static string? GetProductProperty(string productGuidString, string property)
        {
            var sb = new StringBuilder(512);
            uint len = (uint)sb.Capacity;
            uint res = MsiGetProductInfoW(productGuidString, property, sb, ref len);
            return res == ERROR_SUCCESS ? sb.ToString() : null;
        }
    }
}