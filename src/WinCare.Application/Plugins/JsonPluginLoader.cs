namespace WinCare.Application.Plugins;

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

    /// <summary>
    /// Loads and validates a declarative JSON plugin from the specified plugin directory.
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
            return new PluginLoadResult(false, null, Array.Empty<CommandDefinition>(), $"Manifest file missing: {ManifestFileName}");
        }

        try
        {
            var json = File.ReadAllText(manifestPath);
            var manifest = JsonSerializer.Deserialize<PluginManifest>(json);

            if (manifest == null || string.IsNullOrWhiteSpace(manifest.Id))
            {
                return new PluginLoadResult(false, null, Array.Empty<CommandDefinition>(), "Invalid plugin manifest: missing 'id'");
            }

            var canonicalDir = Path.GetFullPath(pluginDirectoryPath);
            var commands = new List<CommandDefinition>();

            foreach (var tool in manifest.Tools)
            {
                if (!string.IsNullOrWhiteSpace(tool.ScriptPath))
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
