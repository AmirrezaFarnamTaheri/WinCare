using WinCare.Application.Commands;
using WinCare.Domain.Commands;

namespace WinCare.Infrastructure.Commands;

/// <summary>
/// Telemetry and privacy endpoint auditing and hardening engine.
/// Inspects and manages host-level telemetry endpoint blacklisting with non-destructive verification.
/// </summary>
internal sealed partial class WindowsCommandExecutor
{
    internal static class PrivacyHelper
    {
        public static readonly IReadOnlyList<string> WellKnownTelemetryDomains =
        [
            "vortex.data.microsoft.com",
            "vortex-win.data.microsoft.com",
            "telecommand.telemetry.microsoft.com",
            "telecommand.telemetry.microsoft.com.nsatc.net",
            "oca.telemetry.microsoft.com",
            "oca.telemetry.microsoft.com.nsatc.net",
            "sqm.telemetry.microsoft.com",
            "sqm.telemetry.microsoft.com.nsatc.net",
            "watson.telemetry.microsoft.com",
            "watson.telemetry.microsoft.com.nsatc.net",
            "redir.metaservices.microsoft.com",
            "choice.microsoft.com",
            "choice.microsoft.com.nsatc.net",
            "df.telemetry.microsoft.com",
            "reports.wes.df.telemetry.microsoft.com",
            "wes.df.telemetry.microsoft.com",
            "services.wes.df.telemetry.microsoft.com",
            "sqm.df.telemetry.microsoft.com",
            "telemetry.microsoft.com",
            "watson.ppe.telemetry.microsoft.com",
            "telemetry.appex.bing.net",
            "telemetry.urs.microsoft.com",
            "settings-sandbox.data.microsoft.com",
            "survey.watson.microsoft.com",
            "watson.live.com",
            "watson.microsoft.com",
            "statsfe2.ws.microsoft.com",
            "corpext.msitadfs.glbdns2.microsoft.com",
            "compatex.frontdoor.bigclouddot.com",
            "diagnostics.support.microsoft.com",
            "feedback.microsoft-hohm.com",
            "feedback.search.microsoft.com",
            "feedback.windows.com",
            "a-0001.a-msedge.net",
            "aschannel.trafficmanager.net",
        ];

        public static string GetHostsFilePath() =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers", "etc", "hosts");

        /// <summary>
        /// Audits the system hosts file for blocked telemetry endpoints.
        /// </summary>
        public static TelemetryAuditResult AuditHostsFile(string? overrideHostsPath = null)
        {
            string hostsPath = overrideHostsPath ?? GetHostsFilePath();
            if (!File.Exists(hostsPath))
            {
                return new TelemetryAuditResult(hostsPath, false, 0, WellKnownTelemetryDomains.Count, [], WellKnownTelemetryDomains);
            }

            try
            {
                string[] lines = File.ReadAllLines(hostsPath);
                var blockedSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (string rawLine in lines)
                {
                    string line = rawLine.Trim();
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
                        continue;

                    string[] tokens = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
                    if (tokens.Length >= 2 && (tokens[0] == "0.0.0.0" || tokens[0] == "127.0.0.1"))
                    {
                        for (int i = 1; i < tokens.Length; i++)
                        {
                            blockedSet.Add(tokens[i].Trim());
                        }
                    }
                }

                var blocked = new List<string>();
                var unblocked = new List<string>();

                foreach (string domain in WellKnownTelemetryDomains)
                {
                    if (blockedSet.Contains(domain))
                    {
                        blocked.Add(domain);
                    }
                    else
                    {
                        unblocked.Add(domain);
                    }
                }

                return new TelemetryAuditResult(hostsPath, true, blocked.Count, unblocked.Count, blocked, unblocked);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return new TelemetryAuditResult(hostsPath, false, 0, WellKnownTelemetryDomains.Count, [], WellKnownTelemetryDomains);
            }
        }
    }

    public sealed record TelemetryAuditResult(
        string HostsPath,
        bool FileExists,
        int BlockedCount,
        int UnblockedCount,
        IReadOnlyList<string> BlockedDomains,
        IReadOnlyList<string> UnblockedDomains);

    public CommandHandlerOutcome AuditTelemetryEndpoints(CommandParameters p)
    {
        TelemetryAuditResult audit = PrivacyHelper.AuditHostsFile();

        return Success("privacy-telemetry-audit", $"Telemetry audit completed: {audit.BlockedCount} blocked, {audit.UnblockedCount} unblocked.", new
        {
            audit.HostsPath,
            audit.FileExists,
            audit.BlockedCount,
            audit.UnblockedCount,
            audit.BlockedDomains,
            audit.UnblockedDomains
        });
    }
}
