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

    /// <summary>Maximum allowed manifest / signature file size (1 MB).</summary>
    public const int MaxManifestBytes = 1024 * 1024;

    /// <summary>
    /// Legacy colocated digest filename. New installations do not create or trust this file;
    /// admission state is stored in the external <see cref="PluginAdmissionTrustStore"/>.
    /// </summary>
    public const string ManifestDigestFileName = ".wincare-manifest.sha256";

    /// <summary>Maximum time to wait for a cross-process plugin operation lock before failing.</summary>
    private static readonly TimeSpan OperationLockTimeout = TimeSpan.FromSeconds(30);

    private readonly HttpClient _httpClient;
    private readonly string _pluginsBaseDirectory;
    private readonly string _operationLocksDirectory;

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
        _operationLocksDirectory = Path.Combine(_pluginsBaseDirectory, ".locks");
    }

    /// <inheritdoc />
    public Task<string> InstallPluginFromPackageAsync(string packageUrl, string? expectedPluginId = null, string? expectedSha256 = null, CancellationToken cancellationToken = default)
        => InstallPluginFromPackageCoreAsync(packageUrl, expectedPluginId, expectedSha256, null, null, null, null, null, null, cancellationToken);

    /// <inheritdoc />
    public Task<string> InstallPluginFromPackageAsync(
        string packageUrl,
        string? expectedPluginId,
        string? expectedSha256,
        string? expectedPublisherPublicKeyPem,
        string? expectedPublisherSignature,
        CancellationToken cancellationToken = default)
        => InstallPluginFromPackageCoreAsync(packageUrl, expectedPluginId, expectedSha256, expectedPublisherPublicKeyPem, expectedPublisherSignature, null, null, null, null, cancellationToken);

    /// <inheritdoc />
    public Task<string> InstallPluginFromPackageAsync(
        string packageUrl,
        string? expectedPluginId,
        string? expectedSha256,
        string? expectedPublisherPublicKeyPem,
        string? expectedPublisherSignature,
        IReadOnlyCollection<string>? revokedPackageIds,
        IReadOnlyCollection<string>? revokedPublishers,
        CancellationToken cancellationToken = default)
        => InstallPluginFromPackageCoreAsync(packageUrl, expectedPluginId, expectedSha256, expectedPublisherPublicKeyPem, expectedPublisherSignature, null, revokedPackageIds, revokedPublishers, null, cancellationToken);

    /// <inheritdoc />
    public Task<string> InstallPluginFromPackageAsync(
        string packageUrl,
        string? expectedPluginId,
        string? expectedSha256,
        string? expectedPublisherPublicKeyPem,
        string? expectedPublisherSignature,
        IReadOnlyCollection<string>? revokedPackageIds,
        IReadOnlyCollection<string>? revokedPublishers,
        IReadOnlyCollection<string>? consentedCapabilities,
        CancellationToken cancellationToken = default)
        => InstallPluginFromPackageCoreAsync(packageUrl, expectedPluginId, expectedSha256, expectedPublisherPublicKeyPem, expectedPublisherSignature, null, revokedPackageIds, revokedPublishers, consentedCapabilities, cancellationToken);

    /// <inheritdoc />
    public Task<string> InstallTrustedPluginFromPackageAsync(
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
        => InstallPluginFromPackageCoreAsync(
            packageUrl,
            expectedPluginId,
            expectedSha256,
            expectedPublisherPublicKeyPem,
            expectedPublisherSignature,
            expectedPublisherId,
            revokedPackageIds,
            revokedPublishers,
            consentedCapabilities,
            cancellationToken);

    private async Task<string> InstallPluginFromPackageCoreAsync(
        string packageUrl,
        string? expectedPluginId,
        string? expectedSha256,
        string? expectedPublisherPublicKeyPem,
        string? expectedPublisherSignature,
        string? expectedPublisherId,
        IReadOnlyCollection<string>? revokedPackageIds,
        IReadOnlyCollection<string>? revokedPublishers,
        IReadOnlyCollection<string>? consentedCapabilities,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(packageUrl))
        {
            throw new ArgumentException("Package URL cannot be empty or null.", nameof(packageUrl));
        }

        if (!Uri.TryCreate(packageUrl, UriKind.Absolute, out var uri) ||
            (!uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
             !uri.Scheme.Equals(Uri.UriSchemeFile, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("Package URL must use the HTTPS scheme or an explicit local file URI.", nameof(packageUrl));
        }

        bool isRemote = uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
        if (isRemote)
        {
            if (string.IsNullOrWhiteSpace(expectedSha256))
            {
                throw new ArgumentException("Remote package installation requires a non-empty expectedSha256 digest.", nameof(expectedSha256));
            }

            if (string.IsNullOrWhiteSpace(expectedPublisherPublicKeyPem) || string.IsNullOrWhiteSpace(expectedPublisherSignature))
            {
                throw new ArgumentException(
                    "Remote package installation requires a publisher key and manifest signature supplied by an independently trusted catalog boundary.",
                    nameof(expectedPublisherPublicKeyPem));
            }
        }

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

        Stream? downloadStream = null;
        HttpResponseMessage? response = null;
        try
        {
            if (uri.Scheme.Equals(Uri.UriSchemeFile, StringComparison.OrdinalIgnoreCase))
            {
                if (!File.Exists(uri.LocalPath))
                {
                    throw new FileNotFoundException($"Local package file not found: {uri.LocalPath}");
                }
                downloadStream = File.OpenRead(uri.LocalPath);
            }
            else
            {
                response = await _httpClient.GetAsync(packageUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                if (response.Content.Headers.ContentLength.HasValue && response.Content.Headers.ContentLength.Value > MaxDownloadSizeBytes)
                {
                    throw new InvalidOperationException($"Package download size ({response.Content.Headers.ContentLength.Value} bytes) exceeds maximum limit of {MaxDownloadSizeBytes} bytes.");
                }

                downloadStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            }

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
            return await InstallPluginFromStreamCoreAsync(
                memoryStream,
                pluginId,
                expectedSha256,
                expectedPublisherPublicKeyPem,
                expectedPublisherSignature,
                expectedPublisherId,
                revokedPackageIds,
                revokedPublishers,
                consentedCapabilities,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (downloadStream != null)
            {
                await downloadStream.DisposeAsync().ConfigureAwait(false);
            }
            response?.Dispose();
        }
    }

    /// <summary>
    /// Validates package publisher authenticity against a trusted publisher key and signature.
    /// </summary>
    public static bool VerifyPublisherAuthenticity(string author, string? signature, out string trustLevel, string? publicKeyPem = null, byte[]? manifestBytes = null)
    {
        if (!string.IsNullOrWhiteSpace(signature))
        {
            if (!string.IsNullOrWhiteSpace(publicKeyPem) && manifestBytes != null && manifestBytes.Length > 0)
            {
                if (VerifyManifestSignature(manifestBytes, signature, publicKeyPem))
                {
                    trustLevel = "Digitally Signed (Cryptographically Verified)";
                    return true;
                }

                trustLevel = "Signature Verification Failed";
                return false;
            }

            trustLevel = "Signature Not Verified";
            return false;
        }

        trustLevel = "Community / Unsigned";
        return false;
    }

    /// <summary>
    /// Cryptographically verifies a digital signature over exact manifest content bytes.
    /// </summary>
    public static bool VerifyManifestSignature(byte[] manifestBytes, string signatureBase64, string publicKeyPem)
        => PluginAdmissionTrustStore.VerifyManifestSignature(manifestBytes, signatureBase64, publicKeyPem);

    /// <inheritdoc />
    public Task<string> InstallPluginFromStreamAsync(Stream archiveStream, string targetPluginId, string? expectedSha256 = null, CancellationToken cancellationToken = default)
        => InstallPluginFromStreamCoreAsync(archiveStream, targetPluginId, expectedSha256, null, null, null, null, null, null, cancellationToken);

    /// <inheritdoc />
    public Task<string> InstallPluginFromStreamAsync(
        Stream archiveStream,
        string targetPluginId,
        string? expectedSha256,
        string? expectedPublisherPublicKeyPem,
        string? expectedPublisherSignature,
        CancellationToken cancellationToken = default)
        => InstallPluginFromStreamCoreAsync(archiveStream, targetPluginId, expectedSha256, expectedPublisherPublicKeyPem, expectedPublisherSignature, null, null, null, null, cancellationToken);

    /// <inheritdoc />
    public Task<string> InstallPluginFromStreamAsync(
        Stream archiveStream,
        string targetPluginId,
        string? expectedSha256,
        string? expectedPublisherPublicKeyPem,
        string? expectedPublisherSignature,
        IReadOnlyCollection<string>? revokedPackageIds,
        IReadOnlyCollection<string>? revokedPublishers,
        CancellationToken cancellationToken = default)
        => InstallPluginFromStreamCoreAsync(archiveStream, targetPluginId, expectedSha256, expectedPublisherPublicKeyPem, expectedPublisherSignature, null, revokedPackageIds, revokedPublishers, null, cancellationToken);

    /// <inheritdoc />
    public Task<string> InstallPluginFromStreamAsync(
        Stream archiveStream,
        string targetPluginId,
        string? expectedSha256,
        string? expectedPublisherPublicKeyPem,
        string? expectedPublisherSignature,
        IReadOnlyCollection<string>? revokedPackageIds,
        IReadOnlyCollection<string>? revokedPublishers,
        IReadOnlyCollection<string>? consentedCapabilities,
        CancellationToken cancellationToken = default)
        => InstallPluginFromStreamCoreAsync(archiveStream, targetPluginId, expectedSha256, expectedPublisherPublicKeyPem, expectedPublisherSignature, null, revokedPackageIds, revokedPublishers, consentedCapabilities, cancellationToken);

    /// <summary>
    /// Trusted stream install variant used by security regression tests and non-HTTP trusted
    /// catalog boundaries. Publisher identity is enforced independently from manifest author.
    /// </summary>
    public Task<string> InstallTrustedPluginFromStreamAsync(
        Stream archiveStream,
        string targetPluginId,
        string? expectedSha256,
        string expectedPublisherId,
        string? expectedPublisherPublicKeyPem,
        string? expectedPublisherSignature,
        IReadOnlyCollection<string>? revokedPackageIds,
        IReadOnlyCollection<string>? revokedPublishers,
        IReadOnlyCollection<string>? consentedCapabilities,
        CancellationToken cancellationToken = default)
        => InstallPluginFromStreamCoreAsync(
            archiveStream,
            targetPluginId,
            expectedSha256,
            expectedPublisherPublicKeyPem,
            expectedPublisherSignature,
            expectedPublisherId,
            revokedPackageIds,
            revokedPublishers,
            consentedCapabilities,
            cancellationToken);

    private async Task<string> InstallPluginFromStreamCoreAsync(
        Stream archiveStream,
        string targetPluginId,
        string? expectedSha256,
        string? expectedPublisherPublicKeyPem,
        string? expectedPublisherSignature,
        string? expectedPublisherId,
        IReadOnlyCollection<string>? revokedPackageIds,
        IReadOnlyCollection<string>? revokedPublishers,
        IReadOnlyCollection<string>? consentedCapabilities,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(archiveStream);

        if (string.IsNullOrWhiteSpace(targetPluginId) || !PluginIdRegex.IsMatch(targetPluginId))
        {
            throw new ArgumentException($"Invalid plugin ID '{targetPluginId}'.", nameof(targetPluginId));
        }

        using var operationLock = await AcquirePluginOperationLockAsync(targetPluginId, cancellationToken).ConfigureAwait(false);
        MemoryStream? ownedMemoryStream = null;
        string? tempExtractDir = null;
        Stream workingStream = archiveStream;

        if (!archiveStream.CanSeek)
        {
            ownedMemoryStream = new MemoryStream();
            try
            {
                await CopyBoundedAsync(archiveStream, ownedMemoryStream, MaxDownloadSizeBytes, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                ownedMemoryStream.Dispose();
                throw;
            }
            ownedMemoryStream.Position = 0;
            workingStream = ownedMemoryStream;
        }
        else if (archiveStream.Length > MaxDownloadSizeBytes)
        {
            throw new InvalidOperationException($"Package stream exceeds maximum allowed size of {MaxDownloadSizeBytes} bytes.");
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(expectedSha256))
            {
                long originalPos = workingStream.Position;
                workingStream.Position = 0;
                var computedHash = Convert.ToHexString(
                    await SHA256.HashDataAsync(workingStream, cancellationToken).ConfigureAwait(false));
                var cleanExpected = expectedSha256.Replace("-", string.Empty);
                if (!computedHash.Equals(cleanExpected, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Package integrity check failed. Expected SHA-256: {expectedSha256}, Actual: {computedHash}");
                }
                workingStream.Position = originalPos;
            }

            var stagingBaseDir = Path.Combine(_pluginsBaseDirectory, ".staging");
            var stagingBackupsDir = Path.Combine(stagingBaseDir, "backups");
            Directory.CreateDirectory(stagingBaseDir);
            Directory.CreateDirectory(stagingBackupsDir);

            tempExtractDir = Path.Combine(stagingBaseDir, $"install_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempExtractDir);

            using (var zipArchive = new ZipArchive(workingStream, ZipArchiveMode.Read, leaveOpen: true))
            {
                if (zipArchive.Entries.Count > MaxZipEntries)
                {
                    throw new InvalidOperationException($"Package contains {zipArchive.Entries.Count} entries, exceeding maximum limit of {MaxZipEntries}.");
                }

                var canonicalTempPath = Path.GetFullPath(tempExtractDir)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
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

            var manifestInfo = new FileInfo(manifestPath);
            if (manifestInfo.Length > MaxManifestBytes)
            {
                throw new InvalidOperationException($"Package admission rejected: Manifest exceeds the {MaxManifestBytes}-byte size limit.");
            }

            byte[] manifestRawBytes = await File.ReadAllBytesAsync(manifestPath, cancellationToken).ConfigureAwait(false);
            var manifestJson = System.Text.Encoding.UTF8.GetString(manifestRawBytes);
            using var doc = JsonDocument.Parse(manifestJson);
            if (!doc.RootElement.TryGetProperty("id", out var idProp) || string.IsNullOrWhiteSpace(idProp.GetString()))
            {
                throw new InvalidOperationException("Package admission rejected: Manifest is missing a valid 'id' property.");
            }

            var manifestId = idProp.GetString()!;
            if (!string.Equals(manifestId, targetPluginId, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Package admission rejected: Manifest ID '{manifestId}' does not match expected target plugin ID '{targetPluginId}'.");
            }

            var author = doc.RootElement.TryGetProperty("author", out var authorProp) ? authorProp.GetString() ?? string.Empty : string.Empty;
            EnforceRevocationPolicy(manifestId, author, expectedPublisherId, revokedPackageIds, revokedPublishers);
            EnforceCapabilityConsent(doc.RootElement, consentedCapabilities);

            string? manifestSignature = null;
            var sigFilePath = Path.Combine(tempExtractDir, "wincare-plugin.sig");
            if (File.Exists(sigFilePath))
            {
                var sigInfo = new FileInfo(sigFilePath);
                if (sigInfo.Length > MaxManifestBytes)
                {
                    throw new InvalidOperationException("Package admission rejected: Signature file exceeds the size limit.");
                }
                manifestSignature = (await File.ReadAllTextAsync(sigFilePath, cancellationToken).ConfigureAwait(false)).Trim();
            }

            bool hasInlineSignature = doc.RootElement.TryGetProperty("signature", out var inlineSignature) &&
                inlineSignature.ValueKind != JsonValueKind.Null &&
                (inlineSignature.ValueKind != JsonValueKind.String || !string.IsNullOrWhiteSpace(inlineSignature.GetString()));
            if ((manifestSignature != null || hasInlineSignature) && string.IsNullOrWhiteSpace(expectedPublisherSignature))
            {
                throw new InvalidOperationException("Package admission rejected: Package-supplied signature is not an independent trust assertion.");
            }

            bool hasExpectedKey = !string.IsNullOrWhiteSpace(expectedPublisherPublicKeyPem);
            bool hasExpectedSignature = !string.IsNullOrWhiteSpace(expectedPublisherSignature);
            if (hasExpectedKey != hasExpectedSignature)
            {
                throw new InvalidOperationException("Package admission rejected: Publisher key and signature must both be supplied by the trusted catalog boundary.");
            }

            if (hasExpectedSignature &&
                !VerifyPublisherAuthenticity(author, expectedPublisherSignature, out var trustLevel, expectedPublisherPublicKeyPem, manifestRawBytes))
            {
                throw new InvalidOperationException($"Package admission rejected: Digital signature verification failed ({trustLevel}).");
            }

            var manifestDigest = Convert.ToHexString(SHA256.HashData(manifestRawBytes)).ToLowerInvariant();
            var admissionRecord = new PluginAdmissionRecord
            {
                PluginId = manifestId,
                ManifestSha256 = manifestDigest,
                PublisherId = string.IsNullOrWhiteSpace(expectedPublisherId) ? null : expectedPublisherId,
                PublisherPublicKeyPem = hasExpectedKey ? expectedPublisherPublicKeyPem : null,
                PublisherSignature = hasExpectedSignature ? expectedPublisherSignature : null,
            };

            var finalTargetDir = ValidateAndGetPluginDirectory(manifestId);
            await PromoteWithAdmissionRecordAsync(
                tempExtractDir,
                finalTargetDir,
                admissionRecord,
                stagingBackupsDir,
                cancellationToken).ConfigureAwait(false);

            tempExtractDir = null;
            return finalTargetDir;
        }
        catch
        {
            if (tempExtractDir != null && Directory.Exists(tempExtractDir))
            {
                try { Directory.Delete(tempExtractDir, recursive: true); }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[PluginInstaller] Failed cleaning up temporary extract directory '{tempExtractDir}': {ex.GetType().Name} - {ex.Message}");
                }
            }
            throw;
        }
        finally
        {
            ownedMemoryStream?.Dispose();
        }
    }

    /// <inheritdoc />
    public async Task<bool> UninstallPluginAsync(string pluginId, CancellationToken cancellationToken = default)
    {
        var pluginDir = ValidateAndGetPluginDirectory(pluginId);
        using var operationLock = await AcquirePluginOperationLockAsync(pluginId, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        bool removed = false;
        if (Directory.Exists(pluginDir))
        {
            Directory.Delete(pluginDir, recursive: true);
            removed = true;
        }

        string admissionPath = PluginAdmissionTrustStore.GetRecordPath(pluginDir);
        if (File.Exists(admissionPath))
        {
            File.Delete(admissionPath);
            removed = true;
        }

        return removed;
    }

    private static async Task PromoteWithAdmissionRecordAsync(
        string stagedPluginDir,
        string finalTargetDir,
        PluginAdmissionRecord admissionRecord,
        string stagingBackupsDir,
        CancellationToken cancellationToken)
    {
        string admissionPath = PluginAdmissionTrustStore.GetRecordPath(finalTargetDir);
        string admissionDirectory = Path.GetDirectoryName(admissionPath)!;
        Directory.CreateDirectory(admissionDirectory);

        string transactionId = Guid.NewGuid().ToString("N");
        string pendingAdmissionPath = admissionPath + "." + transactionId + ".tmp";
        string? pluginBackupDir = null;
        string? admissionBackupPath = null;
        bool oldPluginBackedUp = false;
        bool newPluginPromoted = false;

        var admissionJson = JsonSerializer.Serialize(admissionRecord, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(pendingAdmissionPath, admissionJson, cancellationToken).ConfigureAwait(false);

        try
        {
            if (File.Exists(admissionPath))
            {
                admissionBackupPath = Path.Combine(stagingBackupsDir, $"{Path.GetFileName(finalTargetDir)}_{transactionId}.admission.json");
                File.Copy(admissionPath, admissionBackupPath, overwrite: true);
            }

            if (Directory.Exists(finalTargetDir))
            {
                pluginBackupDir = Path.Combine(stagingBackupsDir, $"{Path.GetFileName(finalTargetDir)}_{transactionId}");
                try
                {
                    Directory.Move(finalTargetDir, pluginBackupDir);
                }
                catch (IOException)
                {
                    CopyDirectoryRecursive(finalTargetDir, pluginBackupDir);
                    Directory.Delete(finalTargetDir, recursive: true);
                }
                oldPluginBackedUp = true;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(finalTargetDir)!);
            try
            {
                Directory.Move(stagedPluginDir, finalTargetDir);
            }
            catch (IOException)
            {
                CopyDirectoryRecursive(stagedPluginDir, finalTargetDir);
                Directory.Delete(stagedPluginDir, recursive: true);
            }
            newPluginPromoted = true;

            // Commit the external trust record last. If this fails, the plugin directory is
            // rolled back before the install can be observed as successfully admitted.
            File.Move(pendingAdmissionPath, admissionPath, overwrite: true);

            if (pluginBackupDir != null && Directory.Exists(pluginBackupDir))
            {
                TryDeleteDirectory(pluginBackupDir);
            }
            if (admissionBackupPath != null && File.Exists(admissionBackupPath))
            {
                TryDeleteFile(admissionBackupPath);
            }
        }
        catch
        {
            if (newPluginPromoted && Directory.Exists(finalTargetDir))
            {
                TryDeleteDirectory(finalTargetDir);
            }

            if (oldPluginBackedUp && pluginBackupDir != null && Directory.Exists(pluginBackupDir))
            {
                try
                {
                    Directory.Move(pluginBackupDir, finalTargetDir);
                }
                catch (IOException)
                {
                    CopyDirectoryRecursive(pluginBackupDir, finalTargetDir);
                    TryDeleteDirectory(pluginBackupDir);
                }
            }

            // The existing admission record is not overwritten until the final commit move.
            // Restore from the backup defensively if a platform-specific overwrite partially
            // changed it before throwing.
            if (admissionBackupPath != null && File.Exists(admissionBackupPath))
            {
                try { File.Copy(admissionBackupPath, admissionPath, overwrite: true); } catch { }
            }
            else if (!oldPluginBackedUp && File.Exists(admissionPath) && newPluginPromoted)
            {
                TryDeleteFile(admissionPath);
            }

            throw;
        }
        finally
        {
            if (File.Exists(pendingAdmissionPath))
            {
                TryDeleteFile(pendingAdmissionPath);
            }
            if (admissionBackupPath != null && File.Exists(admissionBackupPath))
            {
                TryDeleteFile(admissionBackupPath);
            }
        }
    }

    /// <summary>
    /// Acquires an exclusive, cross-process lock for mutations to a single plugin.
    /// The lock file is intentionally retained after release; the handle's sharing
    /// mode, not file deletion, defines ownership and avoids delete/create races.
    /// Acquisition fails closed after a bounded wait rather than busy-waiting forever.
    /// </summary>
    private async Task<FileStream> AcquirePluginOperationLockAsync(string pluginId, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_operationLocksDirectory);
        var lockPath = Path.Combine(_operationLocksDirectory, $"{pluginId}.lock");

        var deadline = DateTime.UtcNow + OperationLockTimeout;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(
                    lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 1,
                    FileOptions.Asynchronous);
            }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken).ConfigureAwait(false);
            }
            catch (IOException)
            {
                throw new TimeoutException(
                    $"Timed out after {OperationLockTimeout.TotalSeconds:0} seconds waiting for the plugin operation lock '{pluginId}'. Another installer may be active.");
            }
        }
    }

    /// <summary>
    /// Enforces current catalog revocation against package ID, trusted publisher ID, and the
    /// manifest's author label. The publisher ID is never inferred from package-controlled data.
    /// </summary>
    private static void EnforceRevocationPolicy(
        string manifestId,
        string author,
        string? expectedPublisherId,
        IReadOnlyCollection<string>? revokedPackageIds,
        IReadOnlyCollection<string>? revokedPublishers)
    {
        var revokedPkgs = new HashSet<string>(revokedPackageIds ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        var revokedPubs = new HashSet<string>(revokedPublishers ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);

        if (revokedPkgs.Contains(manifestId))
        {
            throw new InvalidOperationException($"Package admission rejected: Plugin '{manifestId}' is listed on the security revocation advisory.");
        }

        if (!string.IsNullOrWhiteSpace(expectedPublisherId) && revokedPubs.Contains(expectedPublisherId))
        {
            throw new InvalidOperationException($"Package admission rejected: Publisher '{expectedPublisherId}' is listed on the security revocation advisory.");
        }

        if (!string.IsNullOrWhiteSpace(author) && revokedPubs.Contains(author))
        {
            throw new InvalidOperationException($"Package admission rejected: Publisher '{author}' is listed on the security revocation advisory.");
        }
    }

    /// <summary>
    /// Enforces per-capability user consent at admission time. When
    /// <paramref name="consentedCapabilities"/> is non-null, any manifest capability outside
    /// the consented set rejects the package. When it is null, the gate is skipped (used by
    /// internal/test callers that do not route through the store consent flow).
    /// </summary>
    private static void EnforceCapabilityConsent(JsonElement manifestRoot, IReadOnlyCollection<string>? consentedCapabilities)
    {
        if (consentedCapabilities == null)
        {
            return;
        }

        var consented = new HashSet<string>(consentedCapabilities, StringComparer.OrdinalIgnoreCase);
        if (!manifestRoot.TryGetProperty("declaredCapabilities", out var declaredElement) ||
            declaredElement.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var unconsented = new List<string>();
        foreach (JsonElement capability in declaredElement.EnumerateArray())
        {
            if (capability.ValueKind == JsonValueKind.String)
            {
                var value = capability.GetString();
                if (!string.IsNullOrWhiteSpace(value) && !consented.Contains(value))
                {
                    unconsented.Add(value);
                }
            }
        }

        if (unconsented.Count > 0)
        {
            throw new InvalidOperationException(
                "Package admission rejected: Plugin declares capabilities that were not consented to: " +
                string.Join(", ", unconsented) + ".");
        }
    }

    private static async Task CopyBoundedAsync(Stream source, Stream destination, long maximumBytes, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[81920];
        long totalBytes = 0;
        while (true)
        {
            int bytesRead = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (bytesRead == 0) return;
            totalBytes += bytesRead;
            if (totalBytes > maximumBytes)
            {
                throw new InvalidOperationException($"Package stream exceeds maximum allowed size of {maximumBytes} bytes.");
            }
            await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
        }
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

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PluginInstaller] Failed cleaning directory '{path}': {ex.GetType().Name} - {ex.Message}");
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PluginInstaller] Failed cleaning file '{path}': {ex.GetType().Name} - {ex.Message}");
        }
    }
}
