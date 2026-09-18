using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WinCare.Application.Plugins;

namespace WinCare.Infrastructure.Tests;

/// <summary>
/// Writes an external admission record for a test plugin directory, mirroring what the real
/// installer records at install time. Discovery now fails closed for a package in a user-writable
/// directory that has no record, so a test that wants its manifest loaded must establish the same
/// trust anchor installation would.
/// </summary>
internal static class TestAdmissionRecordWriter
{
    /// <summary>
    /// Records a valid unsigned admission binding for the manifest at <paramref name="manifestPath"/>
    /// and returns the bytes the record's digest was computed over. Unsigned is the honest model for
    /// a test package: a real one that ships no publisher key has no signature either, and both
    /// being null is the accepted "no publisher assertion" state.
    /// </summary>
    public static byte[] Write(string manifestPath)
    {
        byte[] manifestBytes = File.ReadAllBytes(manifestPath);
        string digest = Convert.ToHexString(SHA256.HashData(manifestBytes)).ToLowerInvariant();
        string pluginDirectory = Path.GetDirectoryName(manifestPath)!;

        // The record's plugin id must equal the manifest's own id, not the directory name.
        using var doc = JsonDocument.Parse(manifestBytes);
        string pluginId = doc.RootElement.GetProperty("id").GetString()!;

        string recordPath = PluginAdmissionTrustStore.GetUserScopedRecordPath(pluginDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(recordPath)!);

        var record = new PluginAdmissionRecord
        {
            SchemaVersion = 1,
            PluginId = pluginId,
            ManifestSha256 = digest,
            PublisherPublicKeyPem = null,
            PublisherSignature = null,
        };

        File.WriteAllText(recordPath, JsonSerializer.Serialize(record));
        return manifestBytes;
    }
}
