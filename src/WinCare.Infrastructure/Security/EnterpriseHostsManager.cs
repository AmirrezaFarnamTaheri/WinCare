namespace WinCare.Infrastructure.Security;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// Safe Windows hosts file parser and telemetry domain blocker (CWP-Utilities).
/// Replaces legacy batch scripts with memory-safe, RFC-compliant hosts entry management.
/// </summary>
public static class EnterpriseHostsManager
{
    public sealed record HostEntry(string IpAddress, string Hostname, string? Comment);

    private static readonly Regex HostLineRegex = new(
        @"^(?<ip>[0-9a-fA-F\.:]+)\s+(?<host>[A-Za-z0-9\.\-_]+)(?:\s+#\s*(?<comment>.*))?$",
        RegexOptions.Compiled);

    /// <summary>
    /// Parses hosts file content into discrete <see cref="HostEntry"/> records.
    /// </summary>
    public static IReadOnlyList<HostEntry> ParseHostsContent(string content)
    {
        var entries = new List<HostEntry>();
        if (string.IsNullOrWhiteSpace(content)) return entries;

        using var reader = new StringReader(content);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            string trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#'))
            {
                continue;
            }

            var match = HostLineRegex.Match(trimmed);
            if (match.Success)
            {
                string ip = match.Groups["ip"].Value;
                string host = match.Groups["host"].Value;
                string? comment = match.Groups["comment"].Success ? match.Groups["comment"].Value.Trim() : null;

                entries.Add(new HostEntry(ip, host, comment));
            }
        }

        return entries;
    }

    /// <summary>
    /// Serializes a list of <see cref="HostEntry"/> records into a clean hosts file string.
    /// </summary>
    public static string SerializeHosts(IEnumerable<HostEntry> entries)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# WinCare Enterprise Managed Hosts");
        sb.AppendLine($"# Last Synchronized: {DateTimeOffset.UtcNow:O}");
        sb.AppendLine();

        foreach (var entry in entries)
        {
            if (!string.IsNullOrEmpty(entry.Comment))
            {
                sb.AppendLine($"{entry.IpAddress,-15} {entry.Hostname} # {entry.Comment}");
            }
            else
            {
                sb.AppendLine($"{entry.IpAddress,-15} {entry.Hostname}");
            }
        }

        return sb.ToString();
    }
}
