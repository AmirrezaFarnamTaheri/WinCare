namespace WinCare.Application.Plugins;

using System.IO;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Service contract for acquiring, downloading, and installing plugin packages into local isolation storage.
/// </summary>
public interface IPluginInstallerService
{
    /// <summary>
    /// Downloads and installs a plugin package from a remote URL.
    /// </summary>
    Task<string> InstallPluginFromPackageAsync(string packageUrl, CancellationToken cancellationToken = default);

    /// <summary>
    /// Extracts and installs a plugin package from a zip archive stream.
    /// </summary>
    Task<string> InstallPluginFromStreamAsync(Stream archiveStream, string targetPluginId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Safely uninstalls and removes a plugin directory from local isolation storage.
    /// </summary>
    Task<bool> UninstallPluginAsync(string pluginId, CancellationToken cancellationToken = default);
}
