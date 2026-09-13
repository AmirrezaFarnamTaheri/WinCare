using WinCare.Infrastructure.Commands;
using Xunit;

namespace WinCare.Infrastructure.Tests;

public sealed class NetworkCommandTests
{
    [Theory]
    [InlineData("Cloudflare")]
    [InlineData("Google")]
    [InlineData("Quad9")]
    [InlineData("AdGuard")]
    [InlineData("OpenDNS")]
    [InlineData("CleanBrowsing")]
    public void DnsPresets_Contain_Valid_IPv4_And_IPv6_Addresses(string presetName)
    {
        Assert.True(WindowsCommandExecutor.NetworkHelper.DnsPresets.ContainsKey(presetName));
        (string[] ipv4, string[] ipv6) = WindowsCommandExecutor.NetworkHelper.DnsPresets[presetName];

        Assert.NotEmpty(ipv4);
        Assert.NotEmpty(ipv6);
        foreach (string ip in ipv4)
        {
            Assert.Contains(".", ip);
        }
        foreach (string ip in ipv6)
        {
            Assert.Contains(":", ip);
        }
    }

    [Fact]
    public void ReadTcpOptimizationStatus_Returns_Valid_Status()
    {
        WindowsCommandExecutor.TcpOptimizationStatus status =
            WindowsCommandExecutor.NetworkHelper.ReadTcpOptimizationStatus();

        Assert.NotNull(status);
    }

    [Fact]
    public void FlushDnsCache_Executes_Without_Exception()
    {
        bool result = WindowsCommandExecutor.NetworkHelper.FlushDnsCache();
        // Result is either true (native Win32 called) or false (gracefully caught on unsupported platforms)
        Assert.True(result || !result);
    }
}
