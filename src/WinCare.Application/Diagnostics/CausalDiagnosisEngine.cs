using System;
using System.Collections.Generic;
using System.Linq;

namespace WinCare.Application.Diagnostics;

public sealed record DiagnosisNode(
    string IssueId,
    string RootCause,
    string Explanation,
    double Confidence,
    IReadOnlyList<string> AutomatedRemediationSequence,
    IReadOnlyList<string> DiagnosticEvidenceKeys
);

public sealed class CausalDiagnosisEngine
{
    private static readonly Dictionary<string[], DiagnosisNode> KnowledgeGraph = new(new TokenArrayComparer())
    {
        [new[] { "slow", "high", "ram", "memory" }] = new(
            "SYS_MEMORY_PRESSURE",
            "Active Working Set Overcommit & Standby List Saturation",
            "Processes have saturated physical RAM, forcing heavy pagefile swapping and cache stalls.",
            0.94,
            new[] { "mem.purge_standby", "system.trim_workingset", "diag.top_ram_consumers" },
            new[] { "system.memory.available_mb", "system.memory.commit_total_mb" }
        ),
        [new[] { "dns", "cannot", "connect", "internet", "ping" }] = new(
            "NET_DNS_SUBSYSTEM_STALL",
            "Corrupt Resolver Cache / Unresponsive Gateway Socket",
            "Local DNS resolver service cache contains stale negative responses or interface routing metric is misconfigured.",
            0.96,
            new[] { "net.flush_dns", "net.reset_winsock", "net.renew_lease" },
            new[] { "network.adapter.status", "network.gateway.ping_ms" }
        ),
        [new[] { "disk", "full", "space", "c drive" }] = new(
            "STORAGE_COMPONENT_BLOAT",
            "WinSxS Component Store Fragmentation & Stale Crash Dumps",
            "System volume free space degraded by accumulated Windows Update leftovers, Delivery Optimization cache, and shadow volume snapshots.",
            0.91,
            new[] { "disk.dism_clean", "disk.purge_temp", "disk.trim_shadows" },
            new[] { "storage.c_drive.free_bytes", "storage.winsxs.reclaimable_mb" }
        )
    };

    public DiagnosisNode EvaluateSymptoms(string userInput)
    {
        if (string.IsNullOrWhiteSpace(userInput))
        {
            return new DiagnosisNode(
                "GENERIC_SYSTEM_AUDIT",
                "Environmental System Baseline",
                "Baseline system check.",
                1.0,
                Array.Empty<string>(),
                Array.Empty<string>()
            );
        }

        var tokens = userInput.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        DiagnosisNode? bestMatch = null;
        int maxIntersection = 0;

        foreach (var (keys, node) in KnowledgeGraph)
        {
            int matches = keys.Count(k => tokens.Contains(k));
            if (matches > maxIntersection && matches >= 2)
            {
                maxIntersection = matches;
                bestMatch = node;
            }
        }

        return bestMatch ?? new DiagnosisNode(
            "GENERIC_SYSTEM_AUDIT",
            "Multi-Factor Environmental Drift",
            "The observed symptoms indicate background service degradation. Running full diagnostic triage.",
            0.70,
            new[] { "system.probe_all", "sec.audit_tamper" },
            Array.Empty<string>()
        );
    }

    private sealed class TokenArrayComparer : IEqualityComparer<string[]>
    {
        public bool Equals(string[]? x, string[]? y) => x != null && y != null && x.SequenceEqual(y);
        public int GetHashCode(string[] obj) => obj.Aggregate(0, (acc, item) => acc ^ item.GetHashCode());
    }
}
