using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Application.Plugins;

namespace WinCare.Infrastructure.Plugins;

/// <summary>
/// Infrastructure service for downloading, extracting, and staging plugin packages.
/// </summary>
public class PluginInstallerService : IPluginInstallerService
{
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

    public async Task<string> InstallPluginFromPackageAsync(string packageUrl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(packageUrl))
        {
            throw new ArgumentException("Package URL cannot be empty or null.", nameof(packageUrl));
        }

        string pluginId;
        if (Uri.TryCreate(packageUrl, UriKind.Absolute, out var uri))
        {
            var fileName = Path.GetFileNameWithoutExtension(uri.AbsolutePath);
            pluginId = string.IsNullOrWhiteSpace(fileName) ? $"plugin_{Guid.NewGuid():N}" : fileName;
        }
        else
        {
            pluginId = $"plugin_{Guid.NewGuid():N}";
        }

        using var response = await _httpClient.GetAsync(packageUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var downloadStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

        return await InstallPluginFromStreamAsync(downloadStream, pluginId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> InstallPluginFromStreamAsync(Stream archiveStream, string targetPluginId, CancellationToken cancellationToken = default)
    {
        if (archiveStream == null)
        {
            throw new ArgumentNullException(nameof(archiveStream));
        }

        var stagingBaseDir = Path.Combine(_pluginsBaseDirectory, ".staging");
        Directory.CreateDirectory(stagingBaseDir);
        var tempExtractDir = Path.Combine(stagingBaseDir, $"install_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempExtractDir);

        try
        {
            using (var zipArchive = new ZipArchive(archiveStream, ZipArchiveMode.Read, leaveOpen: true))
            {
                var canonicalTempPath = Path.GetFullPath(tempExtractDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

                foreach (var entry in zipArchive.Entries)
                {
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

            var finalPluginId = targetPluginId;

            if (File.Exists(manifestPath))
            {
                try
                {
                    var manifestJson = await File.ReadAllTextAsync(manifestPath, cancellationToken).ConfigureAwait(false);
                    using var doc = JsonDocument.Parse(manifestJson);
                    if (doc.RootElement.TryGetProperty("id", out var idProp) && !string.IsNullOrWhiteSpace(idProp.GetString()))
                    {
                        finalPluginId = idProp.GetString()!;
                    }
                }
                catch
                {
                    // Fallback to targetPluginId if manifest parsing fails
                }
            }

            var finalTargetDir = Path.Combine(_pluginsBaseDirectory, finalPluginId);
            if (Directory.Exists(finalTargetDir))
            {
                Directory.Delete(finalTargetDir, recursive: true);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(finalTargetDir)!);
            try
            {
                Directory.Move(tempExtractDir, finalTargetDir);
            }
            catch (IOException)
            {
                CopyDirectoryRecursive(tempExtractDir, finalTargetDir);
                Directory.Delete(tempExtractDir, recursive: true);
            }

            return finalTargetDir;
        }
        catch
        {
            if (Directory.Exists(tempExtractDir))
            {
                try { Directory.Delete(tempExtractDir, recursive: true); } catch { }
            }
            throw;
        }
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
