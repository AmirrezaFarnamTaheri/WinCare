namespace WinCare.Application.Plugins;

using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using System.Security.Principal;
using System.Security.AccessControl;

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

    /// <summary>
    /// SHA-256 of the compiled plugin assembly bytes recorded at install time. When present,
    /// discovery must refuse to instantiate an assembly whose on-disk bytes no longer match:
    /// the manifest can still validate while a swapped assembly would run arbitrary code.
    /// Null for script plugins and for records predating this binding.
    /// </summary>
    [JsonPropertyName("assemblySha256")]
    public string? AssemblySha256 { get; init; }

    /// <summary>
    /// Trust scope of this record: "machine" for a record anchored in the machine trust store by an
    /// elevated install, or "user" for a per-user fallback written beside the plugin bytes. Discovery
    /// refuses a user-scoped record when loading with administrator privileges, because the account
    /// that can rewrite the plugin bytes can also rewrite that record. Records written before this
    /// field existed deserialize as null and are treated as user-scoped.
    /// </summary>
    [JsonPropertyName("trustScope")]
    public string? TrustScope { get; init; }
}

/// <summary>
/// Shared helpers for the installer/discovery admission trust store.
/// </summary>
public static class PluginAdmissionTrustStore
{
    /// <summary>Per-user fallback trust directory, created beside the plugins root.</summary>
    public const string UserScopedDirectoryName = ".trust";

    /// <summary>Machine-wide trust store created under the shared program data root.</summary>
    public const string MachineTrustStoreName = "plugin-trust";

    public const string RecordSuffix = ".admission.json";
    public const int MaxRecordBytes = 2 * 1024 * 1024;

    /// <summary>
    /// Trust scope recorded for a plugin whose admission was anchored in the machine trust store.
    /// </summary>
    public const string MachineTrustScope = "machine";

    /// <summary>
    /// Trust scope recorded for a plugin admitted by the installing user account only.
    /// </summary>
    public const string UserTrustScope = "user";

    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    /// <summary>
    /// Returns the machine-wide trust root that anchors plugin admission records. Residing under
    /// the shared program data root is not by itself a security boundary: that root's default ACL
    /// grants every local account container-inherit write access, so a store an unprivileged
    /// process creates inherits it. The store is only an anchor once an elevated session has
    /// hardened it to an admin-only ACL, which is why every consumer verifies it through
    /// <see cref="IsMachineTrustStoreSecured" /> rather than trusting the path.
    /// </summary>
    public static string GetMachineTrustRoot(string? overrideRoot = null)
    {
        if (!string.IsNullOrWhiteSpace(overrideRoot))
        {
            return Path.GetFullPath(overrideRoot);
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "WinCare",
            MachineTrustStoreName);
    }

    /// <summary>
    /// Returns the admission-record path a plugin directory uses inside the machine trust store. The
    /// record is keyed by a hash of the full plugin directory path, so per-user plugin directories
    /// that share a name across accounts never collide in the shared store, and one account cannot
    /// target another account's record by directory name alone.
    /// </summary>
    public static string GetMachineRecordPath(string pluginDirectoryPath, string? machineTrustRootOverride = null)
    {
        string pluginDirectoryName = SplitPluginDirectory(pluginDirectoryPath).pluginDirectoryName;
        string canonical = Path.GetFullPath(pluginDirectoryPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant()[..32];
        return Path.Combine(GetMachineTrustRoot(machineTrustRootOverride), key, pluginDirectoryName + RecordSuffix);
    }

    /// <summary>
    /// Returns the per-user admission-record path for a plugin directory: a sibling <c>.trust</c>
    /// directory beside the plugins root. This is the fallback used when the installing session
    /// could not reach the machine trust store. The record lives in the same user-writable tree as
    /// the plugin bytes, so discovery treats it as account-scoped trust only.
    /// </summary>
    public static string GetUserScopedRecordPath(string pluginDirectoryPath)
    {
        (string pluginDirectoryName, string pluginsRoot) = SplitPluginDirectory(pluginDirectoryPath);
        return Path.Combine(pluginsRoot, UserScopedDirectoryName, pluginDirectoryName + RecordSuffix);
    }

    /// <summary>
    /// Resolves the authoritative admission record for a plugin directory, preferring the machine
    /// trust store and falling back to the per-user record. Returns null when neither exists, so a
    /// caller can apply its missing-trust policy.
    /// </summary>
    /// <remarks>
    /// The machine record is preferred only while its store is actually admin-only. A store an
    /// unprivileged account created inherits the shared program data root's write access, so a
    /// record found there is user-scoped trust at best and must not shadow a genuine per-user
    /// record; when the store is not secured this falls back to the per-user record, and returns
    /// the machine path only if no per-user record exists so the package can still be evaluated as
    /// user-scoped trust rather than dropping its evidence entirely.
    /// </remarks>
    public static string? TryResolveAdmissionRecordPath(string pluginDirectoryPath, string? machineTrustRootOverride = null)
    {
        string machinePath = GetMachineRecordPath(pluginDirectoryPath, machineTrustRootOverride);
        if (File.Exists(machinePath) && IsMachineTrustStoreSecured(machineTrustRootOverride))
        {
            return machinePath;
        }

        string userPath = GetUserScopedRecordPath(pluginDirectoryPath);
        return File.Exists(userPath) ? userPath : (File.Exists(machinePath) ? machinePath : null);
    }

    /// <summary>
    /// Reports whether the machine trust store currently exists and is writable only by privileged
    /// accounts. Merely residing under the shared program data root is not sufficient: that root
    /// grants every local account container-inherit write access, so a store an unprivileged
    /// process created inherits it and its records are as rewritable as the plugin bytes they bind.
    /// This check never creates or modifies the store.
    /// </summary>
    public static bool IsMachineTrustStoreSecured(string? machineTrustRootOverride = null)
    {
        string root = GetMachineTrustRoot(machineTrustRootOverride);
        if (!Directory.Exists(root))
        {
            return false;
        }

        try
        {
            return IsAdminOnly(new DirectoryInfo(root).GetAccessControl());
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or ArgumentException)
        {
            // If the ACL cannot be read at all, treat the store as unsecured rather than guessing.
            return false;
        }
    }

    /// <summary>
    /// Establishes an admin-only machine trust store: creating it with a hardened ACL when it is
    /// missing, and re-securing one that an unprivileged account created with the shared program
    /// data root's permissive inherited rules. Returns false when the store cannot be secured,
    /// including when this process is not elevated, so the caller records per-user trust instead
    /// and discovery then refuses to load that plugin with administrator privileges.
    /// </summary>
    /// <remarks>
    /// A store that already exists here but is not admin-only was created by an unprivileged
    /// account. Records already written under the loose ACL keep their weaker inherited rules, so
    /// <see cref="IsMachineRecordTrustworthy" /> still rejects them and a package anchored before
    /// the store was secured must be reinstalled. Re-securing such a store may fail if the elevated
    /// session cannot write the directory's ACL; that is reported as failure so the caller falls
    /// back to per-user trust instead of recording an anchor that is not actually privileged.
    /// </remarks>
    public static bool TryEnsureMachineTrustStore(string? machineTrustRootOverride = null, Func<bool>? isProcessElevated = null)
    {
        if (!((isProcessElevated ?? IsCurrentProcessElevated)()))
        {
            // Only an elevated session can place records beyond the installing account's reach: an
            // unprivileged session that created the store would own it and could rewrite it.
            return false;
        }

        string root = GetMachineTrustRoot(machineTrustRootOverride);
        if (IsMachineTrustStoreSecured(machineTrustRootOverride))
        {
            return true;
        }

        try
        {
            if (!Directory.Exists(root))
            {
                // Create through DirectoryInfo so the hardened ACL applies to the directory at
                // creation instead of leaving a window where it inherits the shared root's
                // permissive rules.
                new DirectoryInfo(root).Create(CreateAdminOnlyDirectorySecurity());
            }
            else
            {
                new DirectoryInfo(root).SetAccessControl(CreateAdminOnlyDirectorySecurity());
            }

            return IsMachineTrustStoreSecured(machineTrustRootOverride);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// Reports whether an admission record path is a genuine machine anchor: the record must live
    /// inside a machine trust store that is admin-only, and the record itself must not be writable
    /// by any unprivileged account. A record planted while the store was still loosely ACLed keeps
    /// its weaker inherited rules, so this check catches a squatting record even after the store is
    /// later secured.
    /// </summary>
    public static bool IsMachineRecordTrustworthy(string recordPath, string? machineTrustRootOverride = null)
    {
        ArgumentNullException.ThrowIfNull(recordPath);

        string root = GetMachineTrustRoot(machineTrustRootOverride);
        string fullRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string fullRecordPath = Path.GetFullPath(recordPath);

        if (!fullRecordPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!IsMachineTrustStoreSecured(machineTrustRootOverride))
        {
            return false;
        }

        try
        {
            return File.Exists(fullRecordPath) && IsAdminOnly(new FileInfo(fullRecordPath).GetAccessControl());
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// Rights that would let an account rewrite a recorded digest, delete the store that holds it,
    /// or change its owner. These are the write-ish bits only: a mask built from
    /// <see cref="FileSystemRights.FullControl" /> would match every allow rule, because full
    /// control carries the read and execute bits as well, and a read-only ACE for the built-in
    /// Users group would then look dangerous. Composite rights like full control and modify still
    /// trip this mask because they contain the write bits.
    /// </summary>
    private const FileSystemRights TamperingRights =
        FileSystemRights.Write |
        FileSystemRights.Delete |
        FileSystemRights.DeleteSubdirectoriesAndFiles |
        FileSystemRights.ChangePermissions |
        FileSystemRights.TakeOwnership;

    /// <summary>Trusted Installer, the only service identity that should ever hold store rights.</summary>
    private const string TrustedInstallerSidValue = "S-1-5-80-956008885-3418522649-1831038044-1853298831-2271478464";

    /// <summary>
    /// Reports whether an object's access rules grant no privileged right to any identity other than
    /// Local System, the built-in Administrators group, or Trusted Installer. Explicit and inherited
    /// rules are both examined: the inherited rule from the permissive shared program data root is
    /// exactly the hole this check exists to close.
    /// </summary>
    private static bool IsAdminOnly(FileSystemSecurity security)
    {
        AuthorizationRuleCollection rules = security.GetAccessRules(
            includeExplicit: true,
            includeInherited: true,
            targetType: typeof(SecurityIdentifier));

        foreach (FileSystemAccessRule rule in rules)
        {
            if (rule.AccessControlType != AccessControlType.Allow)
            {
                continue;
            }

            if ((rule.FileSystemRights & TamperingRights) == 0)
            {
                continue;
            }

            if (!IsPrivilegedIdentity(rule.IdentityReference))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Reports whether an identity is privileged for the purposes of anchoring plugin trust. A
    /// specific user account is never counted here even when that account happens to be an
    /// administrator: its ACE is the account's own, not the Administrators group's, so a store that
    /// grants it write access is within that account's reach.
    /// </summary>
    private static bool IsPrivilegedIdentity(IdentityReference? identity)
    {
        if (identity is not SecurityIdentifier sid)
        {
            return false;
        }

        return sid.IsWellKnown(WellKnownSidType.LocalSystemSid)
            || sid.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid)
            || string.Equals(sid.Value, TrustedInstallerSidValue, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Applies the store's access rules to a directory or file: Local System and Administrators
    /// inherit full control, standard accounts inherit read and execute only. Callers disable
    /// inheritance first so the shared program data root's permissive rules cannot reach the store
    /// or anything recorded under it.
    /// </summary>
    internal static void ApplyAdminOnlyAccessRules(FileSystemSecurity security)
    {
        ArgumentNullException.ThrowIfNull(security);

        InheritanceFlags inheritance = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;

        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            FileSystemRights.FullControl,
            inheritance,
            PropagationFlags.None,
            AccessControlType.Allow));

        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            FileSystemRights.FullControl,
            inheritance,
            PropagationFlags.None,
            AccessControlType.Allow));

        // Standard accounts may read the anchors an elevated session recorded, so a non-elevated
        // WinCare can still verify a machine-anchored plugin, but they must not be able to rewrite
        // them.
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
            FileSystemRights.ReadAndExecute | FileSystemRights.ListDirectory,
            inheritance,
            PropagationFlags.None,
            AccessControlType.Allow));
    }

    /// <summary>
    /// Builds the ACL for the machine trust store with inheritance disabled, so the shared program
    /// data root's permissive rules cannot reach the store or anything recorded under it.
    /// </summary>
    private static DirectorySecurity CreateAdminOnlyDirectorySecurity()
    {
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        ApplyAdminOnlyAccessRules(security);
        return security;
    }

    /// <summary>
    /// Reports whether the current process runs with administrator privileges. An elevated WinCare
    /// process may only load a per-user plugin whose trust is anchored in the machine store: a
    /// user-scoped record is rewritable by the same account that can swap the plugin bytes, so it
    /// cannot be the trust anchor for code that runs with administrator privileges.
    /// </summary>
    public static bool IsCurrentProcessElevated()
    {
        try
        {
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch (Exception ex) when (ex is ArgumentException or PlatformNotSupportedException)
        {
            // Without an interactive Windows token there is no elevation to gate on; fail closed by
            // reporting elevated only when the token can actually be examined.
            return false;
        }
    }

    private static (string pluginDirectoryName, string pluginsRoot) SplitPluginDirectory(string pluginDirectoryPath)
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

        return (pluginDirectoryName, pluginsRoot);
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
