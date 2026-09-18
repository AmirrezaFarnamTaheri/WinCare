using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using WinCare.Application.Plugins;
using Xunit;

namespace WinCare.Application.Tests;

/// <summary>
/// Verifies that the machine trust store is a real security boundary rather than a path. The
/// shared program data root grants every local account container-inherit write access, so a store
/// merely created there inherits that access and its records are as rewritable as the plugin bytes
/// they are meant to bind. These tests pin the actual property the loader relies on: the store's
/// ACL, not its location.
/// </summary>
public sealed class PluginTrustStoreBoundaryTests
{
    [Fact]
    public void AdminOnlyStore_IsRecognizedAsSecured()
    {
        string root = CreateTempTrustRoot();
        Directory.CreateDirectory(root);
        try
        {
            Assert.False(PluginAdmissionTrustStore.IsMachineTrustStoreSecured(root));

            HardenDirectory(root);
            Assert.True(PluginAdmissionTrustStore.IsMachineTrustStoreSecured(root));
        }
        finally
        {
            RelaxAndDelete(root);
        }
    }

    [Fact]
    public void UserWritableStore_IsRejectedEvenThoughItExists()
    {
        string root = CreateTempTrustRoot();
        Directory.CreateDirectory(root);
        try
        {
            // A store created by an unprivileged account inherits write access for that account, so
            // a record written into it can be rewritten by the same account that can swap the
            // plugin bytes. Existence alone must never count as a boundary.
            Assert.True(Directory.Exists(root));
            Assert.False(PluginAdmissionTrustStore.IsMachineTrustStoreSecured(root));

            string recordPath = WriteRecord(root, "record-one");
            Assert.False(PluginAdmissionTrustStore.IsMachineRecordTrustworthy(recordPath, root));
        }
        finally
        {
            RelaxAndDelete(root);
        }
    }

    [Fact]
    public void RecordWithExplicitUserGrant_IsRejectedEvenInASecuredStore()
    {
        string root = CreateTempTrustRoot();
        Directory.CreateDirectory(root);
        try
        {
            // The real squatting attack: an account that reached the store while it was loose pins
            // an explicit grant for itself on the record and disables inheritance, so hardening the
            // store afterwards leaves the file still within that account's reach. A file that
            // inherits nothing and grants only the attacker is not a privileged anchor.
            string recordPath = WriteRecord(root, "record-two");
            ProtectWithUserGrant(recordPath);
            HardenDirectory(root);

            Assert.True(PluginAdmissionTrustStore.IsMachineTrustStoreSecured(root));
            Assert.False(PluginAdmissionTrustStore.IsMachineRecordTrustworthy(recordPath, root));
        }
        finally
        {
            RelaxAndDelete(root);
        }
    }

    [Fact]
    public void RecordInHardenedStore_IsTrusted()
    {
        string root = CreateTempTrustRoot();
        Directory.CreateDirectory(root);
        try
        {
            // The end state of an elevated install: a record is written while the session owns the
            // store, then the store is hardened. A file that still inherits from its parent picks up
            // the admin-only rules, so it becomes a genuine anchor.
            string recordPath = WriteRecord(root, "record-three");
            HardenDirectory(root);

            Assert.True(PluginAdmissionTrustStore.IsMachineTrustStoreSecured(root));
            Assert.True(PluginAdmissionTrustStore.IsMachineRecordTrustworthy(recordPath, root));
        }
        finally
        {
            RelaxAndDelete(root);
        }
    }

    [Fact]
    public void RecordOutsideTheStore_IsNotAMachineAnchor()
    {
        string root = CreateTempTrustRoot();
        Directory.CreateDirectory(root);
        try
        {
            string elsewhere = Path.Combine(Path.GetTempPath(), "wincare-trust-elsewhere-" + Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(elsewhere, "{}");
            try
            {
                // A record that is not inside the machine store cannot be anchored there, no matter
                // how well-protected the file itself is.
                Assert.False(PluginAdmissionTrustStore.IsMachineRecordTrustworthy(elsewhere, root));
            }
            finally
            {
                File.Delete(elsewhere);
            }
        }
        finally
        {
            RelaxAndDelete(root);
        }
    }

    [Fact]
    public void EnsureMachineTrustStore_HardensAnExistingLooseStore()
    {
        string root = CreateTempTrustRoot();
        Directory.CreateDirectory(root);
        try
        {
            // A session that reports elevation establishes the boundary on a store an unprivileged
            // account created, so admission recorded afterwards lands inside a real boundary.
            Assert.True(PluginAdmissionTrustStore.TryEnsureMachineTrustStore(root, static () => true));
            Assert.True(PluginAdmissionTrustStore.IsMachineTrustStoreSecured(root));
        }
        finally
        {
            RelaxAndDelete(root);
        }
    }

    [Fact]
    public void EnsureMachineTrustStore_DoesNothingWhenNotElevated()
    {
        string root = CreateTempTrustRoot();
        try
        {
            // A standard-account session must not create the store: it would own it, and its records
            // would be within its own reach. The store stays absent so per-user trust is recorded.
            Assert.False(PluginAdmissionTrustStore.TryEnsureMachineTrustStore(root, static () => false));
            Assert.False(Directory.Exists(root));
        }
        finally
        {
            RelaxAndDelete(root);
        }
    }

    private static string CreateTempTrustRoot()
        => Path.Combine(Path.GetTempPath(), "wincare-trust-boundary-" + Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Applies the same admin-only ACL the production code uses, so the tests exercise the real
    /// verification logic rather than an approximation of it.
    /// </summary>
    private static void HardenDirectory(string directory)
    {
        var security = new DirectoryInfo(directory).GetAccessControl();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        PluginAdmissionTrustStore.ApplyAdminOnlyAccessRules(security);
        new DirectoryInfo(directory).SetAccessControl(security);
    }

    /// <summary>
    /// Pins an explicit full-control grant for the current account on a record file and disables
    /// inheritance, simulating an account that reached the store while it was still loosely ACLed.
    /// </summary>
    private static void ProtectWithUserGrant(string path)
    {
        var security = new FileInfo(path).GetAccessControl();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(
            WindowsIdentity.GetCurrent().User ?? throw new InvalidOperationException("Current account has no identity."),
            FileSystemRights.FullControl,
            InheritanceFlags.None,
            PropagationFlags.None,
            AccessControlType.Allow));
        new FileInfo(path).SetAccessControl(security);
    }

    private static string WriteRecord(string root, string name)
    {
        string path = Path.Combine(root, name + PluginAdmissionTrustStore.RecordSuffix);
        File.WriteAllText(path, "{}");
        return path;
    }

    /// <summary>
    /// Restores full control for the current account before deletion. A hardened store denies the
    /// test account write and delete access, so cleanup would otherwise fail and leak temp
    /// directories. The owner retains implicit write-DACL rights, so it can always relax the rules.
    /// </summary>
    private static void RelaxAndDelete(string directory)
    {
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            return;
        }

        Relax(new DirectoryInfo(directory));
        foreach (string file in Directory.GetFiles(directory, "*", SearchOption.TopDirectoryOnly))
        {
            try { Relax(new FileInfo(file)); }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { }
        }

        try { Directory.Delete(directory, recursive: true); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { }
    }

    private static void Relax(DirectoryInfo info)
    {
        try
        {
            var security = info.GetAccessControl();
            security.SetAccessRuleProtection(isProtected: false, preserveInheritance: true);
            security.AddAccessRule(new FileSystemAccessRule(
                WindowsIdentity.GetCurrent().User ?? throw new InvalidOperationException("Current account has no identity."),
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
            info.SetAccessControl(security);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            // Best effort: a store owned by another test run may not be relaxable here.
        }
    }

    private static void Relax(FileInfo info)
    {
        try
        {
            var security = info.GetAccessControl();
            security.SetAccessRuleProtection(isProtected: false, preserveInheritance: true);
            security.AddAccessRule(new FileSystemAccessRule(
                WindowsIdentity.GetCurrent().User ?? throw new InvalidOperationException("Current account has no identity."),
                FileSystemRights.FullControl,
                InheritanceFlags.None,
                PropagationFlags.None,
                AccessControlType.Allow));
            info.SetAccessControl(security);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            // Best effort: a store owned by another test run may not be relaxable here.
        }
    }
}
