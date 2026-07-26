using System;
using System.Collections.Generic;
using Microsoft.Win32;

namespace WinCare.Native
{
    public enum AuditState { Unknown = 0, Disabled = 1, Enabled = 2 }

    public sealed class CodeIntegrityState
    {
        public AuditState Hvci { get; set; } = AuditState.Unknown;
        public AuditState DriverSignatureEnforcement { get; set; } = AuditState.Unknown;
        public AuditState VulnerableDriverBlocklist { get; set; } = AuditState.Unknown;
        public List<string> Evidence { get; } = new();
        public List<string> Errors { get; } = new();
    }

    public sealed class KerberosHardeningState
    {
        public AuditState SmbSigningRequired { get; set; } = AuditState.Unknown;
        public AuditState LdapSigningRequired { get; set; } = AuditState.Unknown;
        public AuditState EpaProtection { get; set; } = AuditState.Unknown;
        public List<string> Evidence { get; } = new();
        public List<string> Errors { get; } = new();
    }

    public static class SecurityAuditor
    {
        private static AuditState RegistryDword(
            string path,
            string name,
            int enabledValue,
            List<string> evidence,
            List<string> errors)
        {
            try
            {
                using RegistryKey? key = Registry.LocalMachine.OpenSubKey(path, false);
                if (key is null)
                {
                    errors.Add($"Registry key unavailable: HKLM\\{path}");
                    return AuditState.Unknown;
                }

                object? value = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                if (value is null)
                {
                    errors.Add($"Registry value unavailable: HKLM\\{path}\\{name}");
                    return AuditState.Unknown;
                }

                int observed = Convert.ToInt32(value);
                evidence.Add($"HKLM\\{path}\\{name}={observed}");
                return observed == enabledValue ? AuditState.Enabled : AuditState.Disabled;
            }
            catch (Exception exception)
            {
                errors.Add($"HKLM\\{path}\\{name}: {exception.Message}");
                return AuditState.Unknown;
            }
        }

        public static CodeIntegrityState GetCodeIntegrityState()
        {
            var state = new CodeIntegrityState();
            state.Hvci = RegistryDword(
                @"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity",
                "Enabled",
                1,
                state.Evidence,
                state.Errors);
            state.VulnerableDriverBlocklist = RegistryDword(
                @"SYSTEM\CurrentControlSet\Control\CI\Config",
                "VulnerableDriverBlocklistEnable",
                1,
                state.Evidence,
                state.Errors);

            // No single supported registry value establishes the live kernel's driver-signature
            // enforcement state. In particular, CI\Policy\UpgradedSystem is migration metadata,
            // not a runtime enforcement oracle. Fail closed rather than report a false positive.
            state.DriverSignatureEnforcement = AuditState.Unknown;
            state.Errors.Add("Driver-signature enforcement requires an authoritative runtime code-integrity source and was not inferred from registry migration metadata.");
            return state;
        }

        public static KerberosHardeningState GetKerberosHardeningState()
        {
            var state = new KerberosHardeningState();
            state.SmbSigningRequired = RegistryDword(
                @"SYSTEM\CurrentControlSet\Services\LanmanServer\Parameters",
                "RequireSecuritySignature",
                1,
                state.Evidence,
                state.Errors);
            state.LdapSigningRequired = RegistryDword(
                @"SYSTEM\CurrentControlSet\Services\NTDS\Parameters",
                "LDAPServerIntegrity",
                2,
                state.Evidence,
                state.Errors);
            state.EpaProtection = RegistryDword(
                @"SYSTEM\CurrentControlSet\Services\HTTP\Parameters",
                "EnableChannelBinding",
                1,
                state.Evidence,
                state.Errors);
            return state;
        }
    }
}
