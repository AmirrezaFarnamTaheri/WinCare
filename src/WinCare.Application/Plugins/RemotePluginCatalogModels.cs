namespace WinCare.Application.Plugins;

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

/// <summary>
/// Represents the root container for the remote plugin catalog downloaded from online sources.
/// </summary>
public class RemotePluginCatalog
{
    /// <summary>
    /// Version schema identifier of the remote catalog format.
    /// </summary>
    [JsonPropertyName("catalogVersion")]
    public string CatalogVersion { get; set; } = "1.0";

    /// <summary>
    /// Timestamp when the catalog was last generated or refreshed.
    /// </summary>
    [JsonPropertyName("lastUpdated")]
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// List of plugin items offered in the catalog.
    /// </summary>
    [JsonPropertyName("plugins")]
    public List<RemotePluginItem> Plugins { get; set; } = new();

    /// <summary>
    /// List of revoked publisher identifiers whose packages must be blocked.
    /// </summary>
    [JsonPropertyName("revokedPublishers")]
    public List<string> RevokedPublishers { get; set; } = new();

    /// <summary>
    /// List of revoked package IDs that must not be admitted.
    /// </summary>
    [JsonPropertyName("revokedPackages")]
    public List<string> RevokedPackages { get; set; } = new();

    /// <summary>
    /// Runtime-only trust state. True only when the exact catalog bytes were verified against
    /// a WinCare-pinned catalog signing key. This value is never accepted from catalog JSON.
    /// </summary>
    [JsonIgnore]
    public bool IsTrustVerified { get; set; }

    /// <summary>Human-readable runtime explanation of the catalog trust state.</summary>
    [JsonIgnore]
    public string TrustStatusMessage { get; set; } = "Catalog signature has not been independently verified.";
}

/// <summary>
/// Represents a single plugin entry in the online catalog.
/// </summary>
public class RemotePluginItem
{
    /// <summary>
    /// Unique identifier of the plugin package.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Display name of the plugin package.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Summary description of the plugin functionality.
    /// </summary>
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Author or organization publishing the plugin.
    /// </summary>
    [JsonPropertyName("author")]
    public string Author { get; set; } = string.Empty;

    /// <summary>
    /// Publisher unique identifier or certificate thumbprint.
    /// </summary>
    [JsonPropertyName("publisherId")]
    public string? PublisherId { get; set; }

    /// <summary>
    /// Cryptographic digital signature of the package manifest.
    /// </summary>
    [JsonPropertyName("signature")]
    public string? Signature { get; set; }

    /// <summary>
    /// PEM formatted RSA/ECDSA public key for verifying manifest signature.
    /// </summary>
    [JsonPropertyName("publicKeyPem")]
    public string? PublicKeyPem { get; set; }

    /// <summary>
    /// Indicates whether this package has been revoked by catalog maintainers.
    /// </summary>
    [JsonPropertyName("isRevoked")]
    public bool IsRevoked { get; set; }

    /// <summary>
    /// Reason explaining why the package was revoked, if applicable.
    /// </summary>
    [JsonPropertyName("revocationReason")]
    public string? RevocationReason { get; set; }

    /// <summary>
    /// Package version string.
    /// </summary>
    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0.0";

    /// <summary>
    /// URL to the plugin icon image.
    /// </summary>
    [JsonPropertyName("iconUrl")]
    public string IconUrl { get; set; } = string.Empty;

    /// <summary>
    /// URL to download the zip package.
    /// </summary>
    [JsonPropertyName("packageUrl")]
    public string PackageUrl { get; set; } = string.Empty;

    /// <summary>
    /// Category navigation bucket.
    /// </summary>
    [JsonPropertyName("category")]
    public string Category { get; set; } = "General";

    /// <summary>
    /// Permissions requested by the plugin package.
    /// </summary>
    [JsonPropertyName("permissions")]
    public List<string> Permissions { get; set; } = new();

    /// <summary>
    /// List of command IDs provided by the plugin package.
    /// </summary>
    [JsonPropertyName("commandsProvided")]
    public List<string> CommandsProvided { get; set; } = new();

    /// <summary>
    /// Expected SHA-256 digest of the zip package.
    /// </summary>
    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = string.Empty;

    /// <summary>
    /// Publication date timestamp.
    /// </summary>
    [JsonPropertyName("publishedDate")]
    public DateTime PublishedDate { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Runtime-only propagation of the independently verified catalog trust state. Catalog JSON
    /// cannot set this flag; it is assigned only by <c>RemoteCatalogService</c> after detached
    /// signature verification.
    /// </summary>
    [JsonIgnore]
    public bool IsCatalogTrustVerified { get; set; }
}
