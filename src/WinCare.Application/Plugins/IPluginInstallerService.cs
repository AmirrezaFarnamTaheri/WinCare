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
    /// Downloads and installs a plugin package from a remote URL with HTTPS enforcement and optional SHA-256 verification.
    /// </summary>
    Task<string> InstallPluginFromPackageAsync(string packageUrl, string? expectedPluginId = null, string? expectedSha256 = null, CancellationToken cancellationToken = default);
    Task<string> InstallPluginFromPackageAsync(string packageUrl, string? expectedPluginId, string? expectedSha256, string? expectedPublisherPublicKeyPem, string? expectedPublisherSignature, CancellationToken cancellationToken = default);

    /// <summary>
    /// Extracts and installs a plugin package from a zip archive stream with optional SHA-256 verification.
    /// </summary>
    Task<string> InstallPluginFromStreamAsync(Stream archiveStream, string targetPluginId, string? expectedSha256 = null, CancellationToken cancellationToken = default);
    Task<string> InstallPluginFromStreamAsync(Stream archiveStream, string targetPluginId, string? expectedSha256, string? expectedPublisherPublicKeyPem, string? expectedPublisherSignature, CancellationToken cancellationToken = default);

    /// <summary>
    /// Safely uninstalls and removes a plugin directory from local isolation storage.
    /// </summary>
    Task<bool> UninstallPluginAsync(string pluginId, CancellationToken cancellationToken = default);
}
