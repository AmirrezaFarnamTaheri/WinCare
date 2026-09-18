namespace WinCare.Infrastructure.Diagnostics;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Diagnostic log tailing and streaming service (Log-Viewer-Utility).
/// Provides high-throughput sliding window log file ingestion, regex filtering, and level parsing.
/// </summary>
public static class DiagnosticLogTailerService
{
    public enum LogSeverity
    {
        Verbose,
        Debug,
        Information,
        Warning,
        Error,
        Critical
    }

    public sealed record ParsedLogEntry(
        DateTimeOffset Timestamp,
        LogSeverity Severity,
        string Category,
        string Message,
        string RawText);

    private static readonly Regex LogPatternRegex = new(
        @"^\[?(?<timestamp>\d{4}-\d{2}-\d{2}[T\s]\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:?\d{2})?)\]?\s*(?:\[(?<level>[A-Za-z]+)\]|(?<level>[A-Za-z]+):)?\s*(?:\[(?<category>[^\]]+)\])?\s*(?<message>.*)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Parses a single raw log text line into a structured <see cref="ParsedLogEntry"/>.
    /// </summary>
    public static ParsedLogEntry ParseLine(string rawLine)
    {
        if (string.IsNullOrWhiteSpace(rawLine))
        {
            return new ParsedLogEntry(DateTimeOffset.UtcNow, LogSeverity.Information, "General", string.Empty, string.Empty);
        }

        var match = LogPatternRegex.Match(rawLine.Trim());
        if (match.Success)
        {
            DateTimeOffset ts = DateTimeOffset.UtcNow;
            if (match.Groups["timestamp"].Success && DateTimeOffset.TryParse(match.Groups["timestamp"].Value, out var parsedTs))
            {
                ts = parsedTs;
            }

            LogSeverity severity = LogSeverity.Information;
            if (match.Groups["level"].Success)
            {
                severity = ParseSeverity(match.Groups["level"].Value);
            }

            string category = match.Groups["category"].Success && !string.IsNullOrWhiteSpace(match.Groups["category"].Value)
                ? match.Groups["category"].Value.Trim()
                : "General";

            string msg = match.Groups["message"].Success
                ? match.Groups["message"].Value.Trim()
                : rawLine;

            return new ParsedLogEntry(ts, severity, category, msg, rawLine);
        }

        // Fallback for unstructured lines
        LogSeverity fallbackSeverity = LogSeverity.Information;
        if (rawLine.Contains("error", StringComparison.OrdinalIgnoreCase) || rawLine.Contains("fail", StringComparison.OrdinalIgnoreCase))
        {
            fallbackSeverity = LogSeverity.Error;
        }
        else if (rawLine.Contains("warn", StringComparison.OrdinalIgnoreCase))
        {
            fallbackSeverity = LogSeverity.Warning;
        }

        return new ParsedLogEntry(DateTimeOffset.UtcNow, fallbackSeverity, "General", rawLine.Trim(), rawLine);
    }

    /// <summary>
    /// Reads the trailing lines of a log file up to maxLines, filtering by minimum severity and optional search pattern.
    /// </summary>
    public static async Task<IReadOnlyList<ParsedLogEntry>> TailFileAsync(
        string filePath,
        int maxLines = 500,
        LogSeverity minSeverity = LogSeverity.Verbose,
        string? searchPattern = null,
        CancellationToken ct = default)
    {
        var entries = new List<ParsedLogEntry>();
        if (!File.Exists(filePath))
        {
            return entries;
        }

        Regex? filterRegex = null;
        if (!string.IsNullOrWhiteSpace(searchPattern))
        {
            try
            {
                // A user-supplied pattern is matched against every tail line; bound it so an
                // adversarial pattern (catastrophic backtracking) cannot pin a thread for minutes.
                filterRegex = new Regex(
                    searchPattern,
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                    TimeSpan.FromSeconds(2));
            }
            catch (ArgumentOutOfRangeException)
            {
                // A pattern the runtime will not accept falls back to literal matching below.
            }
            catch (ArgumentException)
            {
                // Fallback to literal search if regex is invalid
            }
        }

        // Read lines with FileShare.ReadWrite to allow tailing open active logs
        await using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(fs, Encoding.UTF8);

        // Stream forward through the whole file but keep only a bounded window of the most recent
        // lines: peer-log-tail is user-pointable at an arbitrarily large file, and buffering every
        // line first would load the entire log into managed memory instead of tailing it.
        int windowCapacity = Math.Max(maxLines, 1) * 2;
        string[] window = new string[windowCapacity];
        int totalLines = 0;
        int writeSlot = 0;
        string? line;
        while ((line = await reader.ReadLineAsync(ct).ConfigureAwait(false)) != null)
        {
            window[writeSlot] = line;
            writeSlot = (writeSlot + 1) % windowCapacity;
            totalLines++;
        }

        int liveCount = Math.Min(totalLines, windowCapacity);
        var rawLines = new List<string>(liveCount);
        int firstSlot = ((writeSlot - liveCount) % windowCapacity + windowCapacity) % windowCapacity;
        for (int i = 0; i < liveCount; i++)
        {
            rawLines.Add(window[(firstSlot + i) % windowCapacity]);
        }

        int startIndex = Math.Max(0, rawLines.Count - (maxLines * 2));
        for (int i = rawLines.Count - 1; i >= startIndex && entries.Count < maxLines; i--)
        {
            var parsed = ParseLine(rawLines[i]);
            if (parsed.Severity < minSeverity)
            {
                continue;
            }

            if (filterRegex != null)
            {
                bool matched;
                try
                {
                    matched = filterRegex.IsMatch(parsed.RawText);
                }
                catch (RegexMatchTimeoutException)
                {
                    // The pattern exceeded its match budget; treat it as a non-match rather than
                    // letting a hostile pattern abort the whole read.
                    matched = false;
                }
                if (!matched)
                {
                    continue;
                }
            }
            else if (!string.IsNullOrWhiteSpace(searchPattern) && !parsed.RawText.Contains(searchPattern, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            entries.Add(parsed);
        }

        entries.Reverse();
        return entries;
    }

    private static LogSeverity ParseSeverity(string levelStr)
    {
        string upper = levelStr.Trim().ToUpperInvariant();
        return upper switch
        {
            "VERB" or "VERBOSE" or "TRACE" => LogSeverity.Verbose,
            "DBG" or "DEBUG" => LogSeverity.Debug,
            "INF" or "INFO" or "INFORMATION" => LogSeverity.Information,
            "WRN" or "WARN" or "WARNING" => LogSeverity.Warning,
            "ERR" or "ERROR" or "FAIL" or "FAILED" => LogSeverity.Error,
            "CRT" or "CRIT" or "CRITICAL" or "FATAL" => LogSeverity.Critical,
            _ => LogSeverity.Information
        };
    }
}
