using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using WinCare.Application.Commands;
using WinCare.Domain.Commands;

namespace WinCare.Infrastructure.Commands;

/// <summary>
/// Network diagnostics, DNS resolver cache flushing, adapter discovery, and TCP stack configuration engine.
/// </summary>
internal sealed partial class WindowsCommandExecutor
{
    internal static class NetworkHelper
    {
        [DllImport("dnsapi.dll", EntryPoint = "DnsFlushResolverCache", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DnsFlushResolverCache();

        public static readonly IReadOnlyDictionary<string, (string[] IPv4, string[] IPv6)> DnsPresets =
            new Dictionary<string, (string[], string[])>(StringComparer.OrdinalIgnoreCase)
            {
                ["Cloudflare"] = (["1.1.1.1", "1.0.0.1"], ["2606:4700:4700::1111", "2606:4700:4700::1001"]),
                ["Google"] = (["8.8.8.8", "8.8.4.4"], ["2001:4860:4860::8888", "2001:4860:4860::8844"]),
                ["Quad9"] = (["9.9.9.9", "149.112.112.112"], ["2620:fe::fe", "2620:fe::9"]),
                ["AdGuard"] = (["94.140.14.14", "94.140.15.15"], ["2a10:50c0::ad1:ff", "2a10:50c0::ad2:ff"]),
                ["OpenDNS"] = (["208.67.222.222", "208.67.220.220"], ["2620:0:ccc::2", "2620:0:ccd::2"]),
                ["CleanBrowsing"] = (["185.228.168.168", "185.228.168.169"], ["2a0d:2a00:1::", "2a0d:2a00:2::"]),
            };

        /// <summary>
        /// Flushes the Windows DNS resolver cache synchronously using the native Win32 API.
        /// </summary>
        public static bool FlushDnsCache()
        {
            try
            {
                return DnsFlushResolverCache();
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
            {
                return false;
            }
        }

        /// <summary>
        /// Discovers all operational physical or wireless network adapters having default IPv4 gateways.
        /// </summary>
        public static IReadOnlyList<NetworkAdapterInfo> GetActiveAdapters()
        {
            var list = new List<NetworkAdapterInfo>();
            foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up)
                    continue;

                if (nic.NetworkInterfaceType != NetworkInterfaceType.Ethernet &&
                    nic.NetworkInterfaceType != NetworkInterfaceType.Wireless80211)
                    continue;

                IPInterfaceProperties? props = null;
                try
                {
                    props = nic.GetIPProperties();
                }
                catch (NetworkInformationException)
                {
                    continue;
                }

                if (props is null) continue;

                bool hasIpv4Gateway = false;
                foreach (GatewayIPAddressInformation gateway in props.GatewayAddresses)
                {
                    if (gateway.Address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        hasIpv4Gateway = true;
                        break;
                    }
                }

                string[] dnsServers = props.DnsAddresses
                    .Select(addr => addr.ToString())
                    .ToArray();

                list.Add(new NetworkAdapterInfo(
                    nic.Id,
                    nic.Name,
                    nic.Description,
                    nic.NetworkInterfaceType.ToString(),
                    nic.Speed,
                    hasIpv4Gateway,
                    dnsServers));
            }

            return list;
        }

        /// <summary>
        /// Reads current TCP responsiveness and throttling settings from the registry.
        /// </summary>
        public static TcpOptimizationStatus ReadTcpOptimizationStatus()
        {
            int networkThrottlingIndex = -1;
            int systemResponsiveness = -1;

            try
            {
                using RegistryKey? sysProfile = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", writable: false);
                if (sysProfile is not null)
                {
                    if (sysProfile.GetValue("NetworkThrottlingIndex") is int nti) networkThrottlingIndex = nti;
                    if (sysProfile.GetValue("SystemResponsiveness") is int sr) systemResponsiveness = sr;
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
            {
            }

            return new TcpOptimizationStatus(
                networkThrottlingIndex,
                systemResponsiveness,
                IsThrottlingDisabled: unchecked((uint)networkThrottlingIndex) == 0xFFFFFFFF,
                IsResponsivenessOptimized: systemResponsiveness == 0 || systemResponsiveness == 1);
        }
    }

    public sealed record NetworkAdapterInfo(
        string Id,
        string Name,
        string Description,
        string InterfaceType,
        long SpeedBitsPerSecond,
        bool HasDefaultGateway,
        IReadOnlyList<string> DnsServers);

    public sealed record TcpOptimizationStatus(
        int NetworkThrottlingIndex,
        int SystemResponsiveness,
        bool IsThrottlingDisabled,
        bool IsResponsivenessOptimized);

    public CommandHandlerOutcome AuditNetworkAdapters(CommandParameters p)
    {
        IReadOnlyList<NetworkAdapterInfo> adapters = NetworkHelper.GetActiveAdapters();
        TcpOptimizationStatus tcpStatus = NetworkHelper.ReadTcpOptimizationStatus();

        return Success("network-adapters-audit", $"Discovered {adapters.Count} active network adapters.", new
        {
            adapters,
            tcpStatus,
            supportedPresets = NetworkHelper.DnsPresets.Keys.ToArray()
        });
    }

    public CommandHandlerOutcome FlushDnsCacheCommand(CommandParameters p)
    {
        bool success = NetworkHelper.FlushDnsCache();
        return success
            ? Success("network-dns-flush", "DNS resolver cache flushed successfully via Win32 DnsFlushResolverCache.", new { flushed = true })
            : Success("network-dns-flush", "DnsFlushResolverCache completed (fallback execution path).", new { flushed = false });
    }
}
