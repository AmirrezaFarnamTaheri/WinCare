namespace WinCare.Application.Plugins;

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WinCare.CommandCatalog.Models;

/// <summary>
/// Result of loading a plugin package.
/// </summary>
public sealed record PluginLoadResult(
    bool Success,
    PluginManifest? Manifest,
    IReadOnlyList<CommandDefinition> Commands,
    string? ErrorMessage
);

/// <summary>
/// Handles discovery, parsing, and path traversal security validation for declarative JSON plugin packages.
/// </summary>
public static class JsonPluginLoader
{
    private const string ManifestFileName = "wincare-plugin.json";
    private const string LegacyManifestDigestFileName = ".wincare-manifest.sha256";
    private const int MaxManifestBytes = 1024 * 1024;

    /// <summary>
    /// Loads and validates a declarative JSON plugin from the specified plugin directory.
    /// Installer-managed plugins are rebound to an admission record stored outside the plugin
    /// directory, so modifying both the manifest and a colocated checksum cannot bypass the
    /// discovery integrity check.
    /// </summary>
    public static PluginLoadResult LoadFromDirectory(string pluginDirectoryPath)
    {
        if (string.IsNullOrWhiteSpace(pluginDirectoryPath) || !Directory.Exists(pluginDirectoryPath))
        {
            return new PluginLoadResult(false, null, Array.Empty<CommandDefinition>(), $"Plugin directory does not exist: {pluginDirectoryPath}");
        }

        var manifestPath = Path.Combine(pluginDirectoryPath, ManifestFileName);
        if (!File.Exists(manifestPath))
        {
            manifestPath = Path.Combine(pluginDirectoryPath, "plugin.json");
            if (!File.Exists(manifestPath))
            {
                return new PluginLoadResult(false, null, Array.Empty<CommandDefinition>(), $"Manifest file missing: {ManifestFileName}");
            }
        }

        try
        {
            var fileInfo = new FileInfo(manifestPath);
            if (fileInfo.Length > MaxManifestBytes)
            {
                return new PluginLoadResult(false, null, Array.Empty<CommandDefinition>(),
                    $"Manifest file exceeds the {MaxManifestBytes}-byte size limit: {Path.GetFileName(manifestPath)}");
            }

            byte[] manifestBytes = File.ReadAllBytes(manifestPath);
            PluginAdmissionRecord? admissionRecord = null;
            string admissionPath = PluginAdmissionTrustStore.GetRecordPath(pluginDirectoryPath);

            if (File.Exists(admissionPath))
            {
                var admissionInfo = new FileInfo(admissionPath);
                if (admissionInfo.Length > PluginAdmissionTrustStore.MaxRecordBytes)
                {
                    return new PluginLoadResult(false, null, Array.Empty<CommandDefinition>(),
                        "Plugin admission record exceeds the safety limit.");
                }

                try
                {
                    admissionRecord = JsonSerializer.Deserialize<PluginAdmissionRecord>(
                        File.ReadAllText(admissionPath),
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
                {
                    return new PluginLoadResult(false, null, Array.Empty<CommandDefinition>(),
                        $"Failed to read plugin admission record: {ex.Message}");
                }

                if (admissionRecord is null || admissionRecord.SchemaVersion != 1 ||
                    string.IsNullOrWhiteSpace(admissionRecord.PluginId) ||
                    string.IsNullOrWhiteSpace(admissionRecord.ManifestSha256))
                {
                    return new PluginLoadResult(false, null, Array.Empty<CommandDefinition>(),
                        "Plugin admission record is missing required trust metadata.");
                }

                string actualDigest = Convert.ToHexString(SHA256.HashData(manifestBytes)).ToLowerInvariant();
                if (!string.Equals(admissionRecord.ManifestSha256, actualDigest, StringComparison.OrdinalIgnoreCase))
                {
                    return new PluginLoadResult(false, null, Array.Empty<CommandDefinition>(),
                        "Manifest integrity check failed: the plugin manifest no longer matches the externally stored admission record.");
                }

                bool hasKey = !string.IsNullOrWhiteSpace(admissionRecord.PublisherPublicKeyPem);
                bool hasSignature = !string.IsNullOrWhiteSpace(admissionRecord.PublisherSignature);
                if (hasKey != hasSignature)
                {
                    return new PluginLoadResult(false, null, Array.Empty<CommandDefinition>(),
                        "Plugin admission record contains an incomplete publisher trust assertion.");
                }

                if (hasSignature && !PluginAdmissionTrustStore.VerifyManifestSignature(
                    manifestBytes,
                    admissionRecord.PublisherSignature!,
                    admissionRecord.PublisherPublicKeyPem!))
                {
                    return new PluginLoadResult(false, null, Array.Empty<CommandDefinition>(),
                        "Manifest publisher signature verification failed during plugin discovery.");
                }
            }
            else if (File.Exists(Path.Combine(pluginDirectoryPath, LegacyManifestDigestFileName)))
            {
                // A checksum stored beside the manifest can be rewritten by the same actor that
                // rewrites the manifest. Do not silently downgrade an installer-managed package
                // to that legacy trust model; require a reinstall to create the external record.
                return new PluginLoadResult(false, null, Array.Empty<CommandDefinition>(),
                    "Legacy colocated manifest integrity metadata is no longer trusted. Reinstall the plugin to create an external admission record.");
            }

            var json = Encoding.UTF8.GetString(manifestBytes);
            PluginLoadResult result = LoadFromString(json, pluginDirectoryPath);
            if (!result.Success || result.Manifest is null || admissionRecord is null)
            {
                return result;
            }

            if (!string.Equals(result.Manifest.Id, admissionRecord.PluginId, StringComparison.OrdinalIgnoreCase))
            {
                return new PluginLoadResult(false, null, Array.Empty<CommandDefinition>(),
                    "Manifest identity does not match the externally stored plugin admission record.");
            }

            return result;
        }
        catch (Exception ex)
        {
            return new PluginLoadResult(false, null, Array.Empty<CommandDefinition>(), $"Failed to read manifest file: {ex.Message}");
        }
    }

    /// <summary>
    /// Loads and validates a declarative JSON plugin from a raw JSON manifest string.
    /// </summary>
    public static PluginLoadResult LoadFromString(string json, string pluginDirectoryPath = "")
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new PluginLoadResult(false, null, Array.Empty<CommandDefinition>(), "Manifest JSON content is empty.");
        }

        try
        {
            var manifest = JsonSerializer.Deserialize<PluginManifest>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (manifest == null || string.IsNullOrWhiteSpace(manifest.Id))
            {
                return new PluginLoadResult(false, null, Array.Empty<CommandDefinition>(), "Invalid plugin manifest: missing 'id'");
            }

            // Reject conflicting alias spellings (e.g. `title` vs `name`) instead of
            // silently serializing whichever value was applied last.
            manifest.ValidateAliasConsistency();

            var canonicalDir = string.IsNullOrWhiteSpace(pluginDirectoryPath) ? string.Empty : Path.GetFullPath(pluginDirectoryPath);
            var commands = new List<CommandDefinition>();

            foreach (var tool in manifest.Tools)
            {
                if (!string.IsNullOrWhiteSpace(canonicalDir) && !string.IsNullOrWhiteSpace(tool.ScriptPath))
                {
                    var resolvedScriptPath = Path.GetFullPath(Path.Combine(canonicalDir, tool.ScriptPath));
                    var relativePath = Path.GetRelativePath(canonicalDir, resolvedScriptPath);
                    // Path Traversal Security Gate: Ensure script path stays strictly inside plugin directory root
                    if (relativePath.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relativePath))
                    {
                        return new PluginLoadResult(false, null, Array.Empty<CommandDefinition>(), "Security Violation: Script path traverses outside plugin directory.");
                    }
                }

                commands.Add(tool.ToCommandDefinition(manifest.Id));
            }

            return new PluginLoadResult(true, manifest, commands, null);
        }
        catch (Exception ex)
        {
            return new PluginLoadResult(false, null, Array.Empty<CommandDefinition>(), $"Failed to parse manifest: {ex.Message}");
        }
    }
}
