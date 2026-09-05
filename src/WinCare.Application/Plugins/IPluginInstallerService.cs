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
    Task<string> InstallPluginFromPackageAsync(string packageUrl, string? expectedPluginId, string? expectedSha256, string? expectedPublisherPublicKeyPem, string? expectedPublisherSignature, IReadOnlyCollection<string>? revokedPackageIds, IReadOnlyCollection<string>? revokedPublishers, CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads and installs a plugin package, additionally enforcing that every capability the
    /// manifest declares is covered by the explicitly consented set. Pass a non-null
    /// <paramref name="consentedCapabilities"/> to gate on per-capability user consent (an empty
    /// set rejects any plugin that declares capabilities); pass <c>null</c> to skip the gate.
    /// </summary>
    Task<string> InstallPluginFromPackageAsync(string packageUrl, string? expectedPluginId, string? expectedSha256, string? expectedPublisherPublicKeyPem, string? expectedPublisherSignature, IReadOnlyCollection<string>? revokedPackageIds, IReadOnlyCollection<string>? revokedPublishers, IReadOnlyCollection<string>? consentedCapabilities, CancellationToken cancellationToken = default);

    /// <summary>
    /// Security-sensitive catalog installation path. The publisher identifier is supplied by the
    /// freshly fetched trusted catalog and is enforced independently of the package manifest's
    /// author string. Implementations that do not override this default still fail closed for an
    /// explicitly revoked publisher identifier before delegating to the standard hardened path.
    /// </summary>
    Task<string> InstallTrustedPluginFromPackageAsync(
        string packageUrl,
        string expectedPluginId,
        string expectedSha256,
        string expectedPublisherId,
        string expectedPublisherPublicKeyPem,
        string expectedPublisherSignature,
        IReadOnlyCollection<string>? revokedPackageIds,
        IReadOnlyCollection<string>? revokedPublishers,
        IReadOnlyCollection<string>? consentedCapabilities,
        CancellationToken cancellationToken = default)
    {
        if (revokedPublishers is not null)
        {
            foreach (string revokedPublisher in revokedPublishers)
            {
                if (string.Equals(revokedPublisher, expectedPublisherId, StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromException<string>(new InvalidOperationException(
                        $"Package admission rejected: Publisher '{expectedPublisherId}' is listed on the security revocation advisory."));
                }
            }
        }

        return InstallPluginFromPackageAsync(
            packageUrl,
            expectedPluginId,
            expectedSha256,
            expectedPublisherPublicKeyPem,
            expectedPublisherSignature,
            revokedPackageIds,
            revokedPublishers,
            consentedCapabilities,
            cancellationToken);
    }

    /// <summary>
    /// Extracts and installs a plugin package from a zip archive stream with optional SHA-256 verification.
    /// </summary>
    Task<string> InstallPluginFromStreamAsync(Stream archiveStream, string targetPluginId, string? expectedSha256 = null, CancellationToken cancellationToken = default);
    Task<string> InstallPluginFromStreamAsync(Stream archiveStream, string targetPluginId, string? expectedSha256, string? expectedPublisherPublicKeyPem, string? expectedPublisherSignature, CancellationToken cancellationToken = default);
    Task<string> InstallPluginFromStreamAsync(Stream archiveStream, string targetPluginId, string? expectedSha256, string? expectedPublisherPublicKeyPem, string? expectedPublisherSignature, IReadOnlyCollection<string>? revokedPackageIds, IReadOnlyCollection<string>? revokedPublishers, CancellationToken cancellationToken = default);

    /// <summary>
    /// Extracts and installs a plugin package from a zip archive stream, additionally enforcing
    /// per-capability user consent (see the package overload for semantics).
    /// </summary>
    Task<string> InstallPluginFromStreamAsync(Stream archiveStream, string targetPluginId, string? expectedSha256, string? expectedPublisherPublicKeyPem, string? expectedPublisherSignature, IReadOnlyCollection<string>? revokedPackageIds, IReadOnlyCollection<string>? revokedPublishers, IReadOnlyCollection<string>? consentedCapabilities, CancellationToken cancellationToken = default);

    /// <summary>
    /// Safely uninstalls and removes a plugin directory from local isolation storage.
    /// </summary>
    Task<bool> UninstallPluginAsync(string pluginId, CancellationToken cancellationToken = default);
}
