namespace WinCare.Application.Diagnostics;

using System;
using System.Collections.Generic;
using System.IO;

/// <summary>
/// Redis server cache, memory fragmentation, and keyspace diagnostic inspector (AnotherRedisDesktopManager).
/// Analyzes memory footprint of local Redis instances on developer workstations.
/// </summary>
public static class RedisDiagnosticService
{
    public sealed record RedisDiagnosticReport(
        long UsedMemoryBytes,
        long UsedMemoryRssBytes,
        long UsedMemoryPeakBytes,
        double MemoryFragmentationRatio,
        long EvictedKeys,
        long ExpiredKeys,
        long ConnectedClients,
        long UptimeInSeconds,
        bool HasHighFragmentationRisk);

    /// <summary>
    /// Parses raw Redis 'INFO' command output into a strongly-typed diagnostic report.
    /// </summary>
    public static RedisDiagnosticReport ParseInfoOutput(string infoOutput)
    {
        if (string.IsNullOrWhiteSpace(infoOutput))
        {
            return new RedisDiagnosticReport(0, 0, 0, 1.0, 0, 0, 0, 0, false);
        }

        var metrics = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        using var reader = new StringReader(infoOutput);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            string trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#'))
            {
                continue;
            }

            int colonIdx = trimmed.IndexOf(':');
            if (colonIdx > 0 && colonIdx < trimmed.Length - 1)
            {
                string key = trimmed[..colonIdx].Trim();
                string val = trimmed[(colonIdx + 1)..].Trim();
                metrics[key] = val;
            }
        }

        long usedMemory = GetLong(metrics, "used_memory");
        long usedMemoryRss = GetLong(metrics, "used_memory_rss");
        long usedMemoryPeak = GetLong(metrics, "used_memory_peak");
        double fragRatio = GetDouble(metrics, "mem_fragmentation_ratio", 1.0);
        long evicted = GetLong(metrics, "evicted_keys");
        long expired = GetLong(metrics, "expired_keys");
        long clients = GetLong(metrics, "connected_clients");
        long uptime = GetLong(metrics, "uptime_in_seconds");

        // High fragmentation risk: RSS is > 1.5x of allocated memory and total RSS > 100MB
        bool highFrag = fragRatio > 1.5 && usedMemoryRss > (100 * 1024 * 1024);

        return new RedisDiagnosticReport(
            usedMemory,
            usedMemoryRss,
            usedMemoryPeak,
            fragRatio,
            evicted,
            expired,
            clients,
            uptime,
            highFrag);
    }

    private static long GetLong(Dictionary<string, string> dict, string key)
    {
        return dict.TryGetValue(key, out var s) && long.TryParse(s, out long val) ? val : 0;
    }

    private static double GetDouble(Dictionary<string, string> dict, string key, double defaultVal = 0.0)
    {
        return dict.TryGetValue(key, out var s) && double.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double val) ? val : defaultVal;
    }
}
