namespace WinCare.Application.Plugins;

using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

/// <summary>
/// Security record persisted outside an installed plugin directory after package admission.
/// The record binds the admitted manifest bytes to the trusted publisher assertion that was
/// used at install time, so rewriting files inside the plugin directory cannot rewrite the
/// trust anchor used by discovery.
/// </summary>
public sealed class PluginAdmissionRecord
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; } = 1;

    [JsonPropertyName("pluginId")]
    public string PluginId { get; init; } = string.Empty;

    [JsonPropertyName("manifestSha256")]
    public string ManifestSha256 { get; init; } = string.Empty;

    [JsonPropertyName("publisherId")]
    public string? PublisherId { get; init; }

    [JsonPropertyName("publisherPublicKeyPem")]
    public string? PublisherPublicKeyPem { get; init; }

    [JsonPropertyName("publisherSignature")]
    public string? PublisherSignature { get; init; }
}

/// <summary>
/// Shared helpers for the installer/discovery admission trust store.
/// </summary>
public static class PluginAdmissionTrustStore
{
    public const string DirectoryName = ".trust";
    public const string RecordSuffix = ".admission.json";
    public const int MaxRecordBytes = 2 * 1024 * 1024;

    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    /// <summary>
    /// Returns the admission-record path for a concrete plugin directory. Records live in a
    /// sibling <c>.trust</c> directory rather than inside the plugin directory itself.
    /// </summary>
    public static string GetRecordPath(string pluginDirectoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginDirectoryPath);

        string fullPluginPath = Path.GetFullPath(pluginDirectoryPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string pluginDirectoryName = Path.GetFileName(fullPluginPath);
        string? pluginsRoot = Path.GetDirectoryName(fullPluginPath);

        if (string.IsNullOrWhiteSpace(pluginDirectoryName) || string.IsNullOrWhiteSpace(pluginsRoot))
        {
            throw new ArgumentException("Plugin directory must have a parent plugins root.", nameof(pluginDirectoryPath));
        }

        return Path.Combine(pluginsRoot, DirectoryName, pluginDirectoryName + RecordSuffix);
    }

    /// <summary>
    /// Decodes manifest JSON as strict UTF-8 while accepting an optional UTF-8 BOM. The raw
    /// bytes passed to this method remain unchanged and must continue to be used for digests
    /// and publisher signature verification.
    /// </summary>
    public static string DecodeManifestJson(byte[] manifestBytes)
    {
        ArgumentNullException.ThrowIfNull(manifestBytes);

        ReadOnlySpan<byte> jsonBytes = manifestBytes;
        if (jsonBytes.Length >= 3 &&
            jsonBytes[0] == 0xEF &&
            jsonBytes[1] == 0xBB &&
            jsonBytes[2] == 0xBF)
        {
            jsonBytes = jsonBytes[3..];
        }

        return StrictUtf8.GetString(jsonBytes);
    }

    /// <summary>
    /// Verifies an RSA PKCS#1 or ECDSA SHA-256 signature over the exact manifest bytes.
    /// </summary>
    public static bool VerifyManifestSignature(byte[] manifestBytes, string signatureBase64, string publicKeyPem)
    {
        if (manifestBytes is null || manifestBytes.Length == 0 ||
            string.IsNullOrWhiteSpace(signatureBase64) || string.IsNullOrWhiteSpace(publicKeyPem))
        {
            return false;
        }

        byte[] signatureBytes;
        try
        {
            signatureBytes = Convert.FromBase64String(signatureBase64);
        }
        catch (FormatException)
        {
            return false;
        }

        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(publicKeyPem.AsSpan());
            if (rsa.VerifyData(manifestBytes, signatureBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
            {
                return true;
            }
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException or InvalidOperationException)
        {
            // The key may be ECDSA; try that format next.
        }

        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportFromPem(publicKeyPem.AsSpan());
            return ecdsa.VerifyData(manifestBytes, signatureBytes, HashAlgorithmName.SHA256);
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }
}

/// <summary>
/// Resolves an install request against a freshly downloaded catalog and rejects stale review
/// state. The reviewed card is never itself treated as the install-time trust boundary.
/// </summary>
public static class RemotePluginInstallPolicy
{
    public static RemotePluginItem ResolveFreshReviewedEntry(
        RemotePluginCatalog catalog,
        RemotePluginItem reviewedItem,
        IReadOnlyCollection<string>? consentedCapabilities)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(reviewedItem);

        if (!catalog.IsTrustVerified)
        {
            throw new InvalidOperationException(
                "Remote plugin installation is disabled because the current catalog is not independently signed by a WinCare-pinned trust root.");
        }

        RemotePluginItem? freshItem = (catalog.Plugins ?? new List<RemotePluginItem>()).FirstOrDefault(item =>
            string.Equals(item.Id, reviewedItem.Id, StringComparison.OrdinalIgnoreCase));

        if (freshItem is null)
        {
            throw new InvalidOperationException(
                $"Plugin '{reviewedItem.Id}' is no longer present in the current catalog. Refresh and review the store entry again.");
        }

        if (IsRevoked(catalog, freshItem))
        {
            throw new InvalidOperationException(
                $"Plugin '{freshItem.Id}' is revoked by the current security advisory and cannot be installed.");
        }

        if (string.IsNullOrWhiteSpace(freshItem.PackageUrl) ||
            string.IsNullOrWhiteSpace(freshItem.Sha256) ||
            string.IsNullOrWhiteSpace(freshItem.PublicKeyPem) ||
            string.IsNullOrWhiteSpace(freshItem.Signature))
        {
            throw new InvalidOperationException(
                $"Plugin '{freshItem.Id}' no longer has complete trusted installation metadata. Refresh and review the catalog entry again.");
        }

        if (!ReviewedSecurityMetadataMatches(reviewedItem, freshItem))
        {
            throw new InvalidOperationException(
                $"Plugin '{freshItem.Id}' changed in the catalog after it was reviewed. Refresh the store and review the updated version, publisher, permissions, and integrity metadata before installing.");
        }

        var consented = new HashSet<string>(
            consentedCapabilities ?? Array.Empty<string>(),
            StringComparer.OrdinalIgnoreCase);
        var freshPermissions = freshItem.Permissions ?? new List<string>();
        var missingConsent = freshPermissions
            .Where(permission => !string.IsNullOrWhiteSpace(permission) && !consented.Contains(permission))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (missingConsent.Length > 0)
        {
            throw new InvalidOperationException(
                "Plugin installation requires fresh consent for: " + string.Join(", ", missingConsent) + ".");
        }

        return freshItem;
    }

    private static bool IsRevoked(RemotePluginCatalog catalog, RemotePluginItem item)
    {
        if (item.IsRevoked)
        {
            return true;
        }

        if ((catalog.RevokedPackages ?? new List<string>()).Any(id => string.Equals(id, item.Id, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return (catalog.RevokedPublishers ?? new List<string>()).Any(revoked =>
            (!string.IsNullOrWhiteSpace(item.PublisherId) && string.Equals(revoked, item.PublisherId, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrWhiteSpace(item.Author) && string.Equals(revoked, item.Author, StringComparison.OrdinalIgnoreCase)));
    }

    private static bool ReviewedSecurityMetadataMatches(RemotePluginItem reviewed, RemotePluginItem fresh)
    {
        return string.Equals(reviewed.Id, fresh.Id, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(reviewed.Version, fresh.Version, StringComparison.Ordinal) &&
               string.Equals(reviewed.Author, fresh.Author, StringComparison.Ordinal) &&
               string.Equals(reviewed.PublisherId ?? string.Empty, fresh.PublisherId ?? string.Empty, StringComparison.Ordinal) &&
               string.Equals(reviewed.PackageUrl, fresh.PackageUrl, StringComparison.Ordinal) &&
               string.Equals(reviewed.Sha256, fresh.Sha256, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(reviewed.PublicKeyPem ?? string.Empty, fresh.PublicKeyPem ?? string.Empty, StringComparison.Ordinal) &&
               string.Equals(reviewed.Signature ?? string.Empty, fresh.Signature ?? string.Empty, StringComparison.Ordinal) &&
               new HashSet<string>(reviewed.Permissions ?? new List<string>(), StringComparer.OrdinalIgnoreCase)
                   .SetEquals(fresh.Permissions ?? new List<string>());
    }
}
