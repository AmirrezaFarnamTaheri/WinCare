using WinCare.Application.Plugins;
using WinCare.Infrastructure.Plugins;

namespace WinCare.Infrastructure.Tests;

public sealed class VerifiedPluginExecutionSnapshotTests
{
    [Fact]
    public void Snapshot_executes_exact_admitted_bytes_even_when_original_package_changes()
    {
        string root = Path.Combine(Path.GetTempPath(), "WinCareScriptSnapshot_" + Guid.NewGuid().ToString("N"));
        string package = Path.Combine(root, "plugins");
        string scratch = Path.Combine(root, "scratch");
        Directory.CreateDirectory(package);
        try
        {
            Directory.CreateDirectory(Path.Combine(package, "lib"));
            string script = Path.Combine(package, "run.ps1");
            File.WriteAllText(script, "Write-Output 'admitted'");
            File.WriteAllText(Path.Combine(package, "lib", "configuration.txt"), "original");
            string digest = PluginAdmissionTrustStore.ComputePluginPayloadDigest(package);

            string stagedPath;
            using (var snapshot = VerifiedPluginExecutionSnapshot.Create(package, script, digest, scratch))
            {
                stagedPath = snapshot.Root;
                Assert.NotEqual(script, snapshot.ScriptFullPath);
                Assert.Equal("Write-Output 'admitted'", File.ReadAllText(snapshot.ScriptFullPath));
                Assert.Equal("original",
                    File.ReadAllText(Path.Combine(snapshot.Root, "lib", "configuration.txt")));

                File.WriteAllText(script, "Write-Output 'tampered'");
                Assert.Equal("Write-Output 'admitted'", File.ReadAllText(snapshot.ScriptFullPath));
                Assert.Equal(digest, PluginAdmissionTrustStore.ComputePluginPayloadDigest(snapshot.Root));
            }
            Assert.False(Directory.Exists(stagedPath));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Snapshot_rejects_tampering_before_any_script_is_launched()
    {
        string root = Path.Combine(Path.GetTempPath(), "WinCareScriptSnapshot_" + Guid.NewGuid().ToString("N"));
        string package = Path.Combine(root, "plugins");
        Directory.CreateDirectory(package);
        try
        {
            string script = Path.Combine(package, "run.ps1");
            File.WriteAllText(script, "Write-Output 'admitted'");
            string digest = PluginAdmissionTrustStore.ComputePluginPayloadDigest(package);
            File.WriteAllText(script, "Write-Output 'modified'");

            Assert.Throws<InvalidDataException>(() => VerifiedPluginExecutionSnapshot.Create(
                package, script, digest, Path.Combine(root, "scratch")));
            string[] remaining = Directory.Exists(Path.Combine(root, "scratch"))
                ? Directory.GetDirectories(Path.Combine(root, "scratch"))
                : [];
            Assert.Empty(remaining);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Snapshot_rejects_executables_outside_the_package()
    {
        string root = Path.Combine(Path.GetTempPath(), "WinCareScriptSnapshot_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "outside.ps1"), "outside");
            string package = Path.Combine(root, "plugin");
            Directory.CreateDirectory(package);
            string digest = PluginAdmissionTrustStore.ComputePluginPayloadDigest(package);
            Assert.Throws<InvalidDataException>(() => VerifiedPluginExecutionSnapshot.Create(
                package, Path.Combine(root, "outside.ps1"), digest, Path.Combine(root, "scratch")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
