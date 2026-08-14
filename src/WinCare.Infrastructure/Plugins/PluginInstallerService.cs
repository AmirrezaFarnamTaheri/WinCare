using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Application.Plugins;

namespace WinCare.Infrastructure.Plugins;

/// <summary>
/// Infrastructure service for downloading, extracting, validating, and atomically staging plugin packages.
/// </summary>
public class PluginInstallerService : IPluginInstallerService
{
    private static readonly Regex PluginIdRegex = new(@"^[a-zA-Z0-9][a-zA-Z0-9._-]{2,127}$", RegexOptions.Compiled);
    private static readonly Regex Sha256HexRegex = new(@"^[0-9a-fA-F]{64}$", RegexOptions.Compiled);

    /// <summary>Maximum allowed plugin package download size (50 MB).</summary>
    public const long MaxDownloadSizeBytes = 50 * 1024 * 1024;

    /// <summary>Maximum allowed archive entry count (500 entries).</summary>
    public const int MaxZipEntries = 500;

    /// <summary>Maximum allowed uncompressed extraction size (200 MB).</summary>
    public const long MaxUncompressedSizeBytes = 200 * 1024 * 1024;

    private readonly HttpClient _httpClient;
    private readonly string _pluginsBaseDirectory;

    /// <summary>
    /// Initializes a new instance of <see cref="PluginInstallerService"/>.
    /// </summary>
    public PluginInstallerService(HttpClient? httpClient = null, string? pluginsBaseDirectory = null)
    {
        _httpClient = httpClient ?? new HttpClient();

        if (string.IsNullOrWhiteSpace(pluginsBaseDirectory))
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _pluginsBaseDirectory = Path.Combine(localAppData, "WinCare", "Plugins");
        }
        else
        {
            _pluginsBaseDirectory = pluginsBaseDirectory;
        }

        Directory.CreateDirectory(_pluginsBaseDirectory);
    }

    /// <inheritdoc />
    public async Task<string> InstallPluginFromPackageAsync(
        string packageUrl,
        string? expectedPluginId = null,
        string? expectedSha256 = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(packageUrl))
        {
            throw new ArgumentException("Package URL cannot be empty or null.", nameof(packageUrl));
        }

        if (!Uri.TryCreate(packageUrl, UriKind.Absolute, out var uri) ||
            (!uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
             !uri.Scheme.Equals(Uri.UriSchemeFile, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("Package URL must use the HTTPS scheme.", nameof(packageUrl));
        }

        // Enforce mandatory well-formed SHA-256 for remote downloads
        if (!string.IsNullOrWhiteSpace(expectedSha256))
        {
            var cleanExpected = expectedSha256.Replace("-", string.Empty);
            if (!Sha256HexRegex.IsMatch(cleanExpected))
            {
                throw new ArgumentException($"Invalid expected SHA-256 digest format: '{expectedSha256}'. Must be 64-character hex.", nameof(expectedSha256));
            }
        }

        string pluginId;
        if (!string.IsNullOrWhiteSpace(expectedPluginId))
        {
            pluginId = expectedPluginId;
        }
        else
        {
            var fileName = Path.GetFileNameWithoutExtension(uri.AbsolutePath);
            pluginId = string.IsNullOrWhiteSpace(fileName) ? $"plugin_{Guid.NewGuid():N}" : fileName;
        }

        using var response = await _httpClient.GetAsync(packageUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        if (response.Content.Headers.ContentLength.HasValue && response.Content.Headers.ContentLength.Value > MaxDownloadSizeBytes)
        {
            throw new InvalidOperationException($"Package download size ({response.Content.Headers.ContentLength.Value} bytes) exceeds maximum limit of {MaxDownloadSizeBytes} bytes.");
        }

        await using var downloadStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var memoryStream = new MemoryStream();
        var buffer = new byte[81920];
        int bytesRead;
        long totalBytes = 0;

        while ((bytesRead = await downloadStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false)) > 0)
        {
            totalBytes += bytesRead;
            if (totalBytes > MaxDownloadSizeBytes)
            {
                throw new InvalidOperationException($"Package download exceeded maximum allowed size of {MaxDownloadSizeBytes} bytes.");
            }
            memoryStream.Write(buffer, 0, bytesRead);
        }

        memoryStream.Position = 0;

        return await InstallPluginFromStreamAsync(memoryStream, pluginId, expectedSha256, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<string> InstallPluginFromStreamAsync(
        Stream archiveStream,
        string targetPluginId,
        string? expectedSha256 = null,
        CancellationToken cancellationToken = default)
    {
        if (archiveStream == null)
        {
            throw new ArgumentNullException(nameof(archiveStream));
        }

        if (string.IsNullOrWhiteSpace(targetPluginId) || (!targetPluginId.StartsWith("plugin_") && !PluginIdRegex.IsMatch(targetPluginId)))
        {
            throw new ArgumentException($"Invalid plugin ID '{targetPluginId}'.", nameof(targetPluginId));
        }

        // Mandatory SHA-256 validation if provided
        if (!string.IsNullOrWhiteSpace(expectedSha256))
        {
            long originalPos = archiveStream.CanSeek ? archiveStream.Position : 0;
            if (archiveStream.CanSeek)
            {
                archiveStream.Position = 0;
            }

            byte[] streamBytes;
            if (archiveStream is MemoryStream ms)
            {
                streamBytes = ms.ToArray();
            }
            else
            {
                using var tempMs = new MemoryStream();
                await archiveStream.CopyToAsync(tempMs, cancellationToken).ConfigureAwait(false);
                streamBytes = tempMs.ToArray();
                if (archiveStream.CanSeek)
                {
                    archiveStream.Position = originalPos;
                }
            }

            var computedHash = Convert.ToHexString(SHA256.HashData(streamBytes));
            var cleanExpected = expectedSha256.Replace("-", string.Empty);
            if (!computedHash.Equals(cleanExpected, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Package integrity check failed. Expected SHA-256: {expectedSha256}, Actual: {computedHash}");
            }

            if (archiveStream.CanSeek)
            {
                archiveStream.Position = originalPos;
            }
        }

        var stagingBaseDir = Path.Combine(_pluginsBaseDirectory, ".staging");
        var stagingBackupsDir = Path.Combine(stagingBaseDir, "backups");
        Directory.CreateDirectory(stagingBaseDir);
        Directory.CreateDirectory(stagingBackupsDir);

        var tempExtractDir = Path.Combine(stagingBaseDir, $"install_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempExtractDir);

        try
        {
            using (var zipArchive = new ZipArchive(archiveStream, ZipArchiveMode.Read, leaveOpen: true))
            {
                if (zipArchive.Entries.Count > MaxZipEntries)
                {
                    throw new InvalidOperationException($"Package contains {zipArchive.Entries.Count} entries, exceeding maximum limit of {MaxZipEntries}.");
                }

                var canonicalTempPath = Path.GetFullPath(tempExtractDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                long totalUncompressedBytes = 0;

                foreach (var entry in zipArchive.Entries)
                {
                    totalUncompressedBytes += entry.Length;
                    if (totalUncompressedBytes > MaxUncompressedSizeBytes)
                    {
                        throw new InvalidOperationException($"Package uncompressed size exceeded maximum allowed limit of {MaxUncompressedSizeBytes} bytes.");
                    }

                    var destinationPath = Path.GetFullPath(Path.Combine(tempExtractDir, entry.FullName));
                    if (!destinationPath.StartsWith(canonicalTempPath, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException($"Security violation: ZIP entry '{entry.FullName}' attempts path traversal.");
                    }

                    if (string.IsNullOrEmpty(entry.Name))
                    {
                        Directory.CreateDirectory(destinationPath);
                    }
                    else
                    {
                        var parent = Path.GetDirectoryName(destinationPath);
                        if (!string.IsNullOrEmpty(parent))
                        {
                            Directory.CreateDirectory(parent);
                        }
                        entry.ExtractToFile(destinationPath, overwrite: true);
                    }
                }
            }

            var manifestPath = Path.Combine(tempExtractDir, "wincare-plugin.json");
            if (!File.Exists(manifestPath))
            {
                manifestPath = Path.Combine(tempExtractDir, "plugin.json");
            }

            if (!File.Exists(manifestPath))
            {
                throw new InvalidOperationException("Package admission rejected: Missing plugin manifest ('wincare-plugin.json' or 'plugin.json').");
            }

            var manifestJson = await File.ReadAllTextAsync(manifestPath, cancellationToken).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(manifestJson);
            if (!doc.RootElement.TryGetProperty("id", out var idProp) || string.IsNullOrWhiteSpace(idProp.GetString()))
            {
                throw new InvalidOperationException("Package admission rejected: Manifest is missing a valid 'id' property.");
            }

            var manifestId = idProp.GetString()!;

            // Finding 3: Enforce strict equality between manifest ID and catalog/expected targetPluginId
            if (!string.IsNullOrWhiteSpace(targetPluginId) && !targetPluginId.StartsWith("plugin_") && !string.Equals(manifestId, targetPluginId, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Package admission rejected: Manifest ID '{manifestId}' does not match expected target plugin ID '{targetPluginId}'.");
            }

            var finalPluginId = manifestId;
            var finalTargetDir = ValidateAndGetPluginDirectory(finalPluginId);

            // Atomic promotion with isolated staging backup support
            string? backupDir = null;
            if (Directory.Exists(finalTargetDir))
            {
                backupDir = Path.Combine(stagingBackupsDir, $"{finalPluginId}_{Guid.NewGuid():N}");
                try
                {
                    Directory.Move(finalTargetDir, backupDir);
                }
                catch (IOException)
                {
                    CopyDirectoryRecursive(finalTargetDir, backupDir);
                    Directory.Delete(finalTargetDir, recursive: true);
                }
            }

            Directory.CreateDirectory(Path.GetDirectoryName(finalTargetDir)!);
            try
            {
                Directory.Move(tempExtractDir, finalTargetDir);
            }
            catch (IOException)
            {
                try
                {
                    CopyDirectoryRecursive(tempExtractDir, finalTargetDir);
                    Directory.Delete(tempExtractDir, recursive: true);
                }
                catch
                {
                    // If promotion failed, restore backup from isolated staging
                    if (backupDir != null && Directory.Exists(backupDir))
                    {
                        try
                        {
                            if (Directory.Exists(finalTargetDir))
                            {
                                Directory.Delete(finalTargetDir, recursive: true);
                            }
                            Directory.Move(backupDir, finalTargetDir);
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[PluginInstaller] Failed restoring backup from '{backupDir}' to '{finalTargetDir}': {ex.GetType().Name} - {ex.Message}");
                        }
                    }
                    throw;
                }
            }

            // Cleanup backup directory on successful installation
            if (backupDir != null && Directory.Exists(backupDir))
            {
                try
                {
                    Directory.Delete(backupDir, recursive: true);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[PluginInstaller] Failed cleaning up backup directory '{backupDir}': {ex.GetType().Name} - {ex.Message}");
                }
            }

            return finalTargetDir;
        }
        catch
        {
            if (Directory.Exists(tempExtractDir))
            {
                try { Directory.Delete(tempExtractDir, recursive: true); }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[PluginInstaller] Failed cleaning up temporary extract directory '{tempExtractDir}': {ex.GetType().Name} - {ex.Message}");
                }
            }
            throw;
        }
    }

    /// <inheritdoc />
    public Task<bool> UninstallPluginAsync(string pluginId, CancellationToken cancellationToken = default)
    {
        var pluginDir = ValidateAndGetPluginDirectory(pluginId);
        if (Directory.Exists(pluginDir))
        {
            Directory.Delete(pluginDir, recursive: true);
            return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    private string ValidateAndGetPluginDirectory(string pluginId)
    {
        if (string.IsNullOrWhiteSpace(pluginId) || !PluginIdRegex.IsMatch(pluginId))
        {
            throw new ArgumentException($"Invalid plugin ID '{pluginId}'. Plugin IDs must match '^[a-zA-Z0-9][a-zA-Z0-9._-]{{2,127}}$'.", nameof(pluginId));
        }

        var targetDir = Path.GetFullPath(Path.Combine(_pluginsBaseDirectory, pluginId));
        var canonicalBaseDir = Path.GetFullPath(_pluginsBaseDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

        if (!targetDir.StartsWith(canonicalBaseDir, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Security violation: Plugin ID '{pluginId}' traverses outside plugins root directory.", nameof(pluginId));
        }

        return targetDir;
    }

    private static void CopyDirectoryRecursive(string sourceDir, string destinationDir)
    {
        Directory.CreateDirectory(destinationDir);
        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var destFile = Path.Combine(destinationDir, Path.GetFileName(file));
            File.Copy(file, destFile, overwrite: true);
        }
        foreach (var subDir in Directory.GetDirectories(sourceDir))
        {
            var destSubDir = Path.Combine(destinationDir, Path.GetFileName(subDir));
            CopyDirectoryRecursive(subDir, destSubDir);
        }
    }
}
