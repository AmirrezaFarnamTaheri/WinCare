namespace WinCare.Application.Plugins;

using System.IO;
using System.Threading;
using System.Threading.Tasks;

public interface IPluginInstallerService
{
    Task<string> InstallPluginFromPackageAsync(string packageUrl, CancellationToken cancellationToken = default);
    Task<string> InstallPluginFromStreamAsync(Stream archiveStream, string targetPluginId, CancellationToken cancellationToken = default);
}
