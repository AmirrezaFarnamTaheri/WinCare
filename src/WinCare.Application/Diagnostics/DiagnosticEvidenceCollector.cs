using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace WinCare.Application.Diagnostics
{
    public interface IDiagnosticEvidenceCollector
    {
        Task<IReadOnlyList<TelemetryEvidence>> CollectEvidenceAsync(string intent, CancellationToken cancellationToken = default);
    }

    public sealed class DiagnosticEvidenceCollector : IDiagnosticEvidenceCollector
    {
        public Task<IReadOnlyList<TelemetryEvidence>> CollectEvidenceAsync(string intent, CancellationToken cancellationToken = default)
        {
            var evidence = new List<TelemetryEvidence>();

            try
            {
                switch (intent)
                {
                    case "intent.storage.cleanup":
                        evidence.AddRange(ProbeStorageDrives());
                        break;

                    case "intent.memory.optimize":
                        evidence.AddRange(ProbeMemoryUsage());
                        break;

                    case "intent.network.flush":
                        evidence.AddRange(ProbeNetworkAdapters());
                        break;

                    default:
                        // General system baseline probe
                        evidence.AddRange(ProbeStorageDrives());
                        break;
                }
            }
            catch (Exception ex)
            {
                evidence.Add(new TelemetryEvidence(
                    HasMeasuredEvidence: false,
                    MetricName: "Telemetry Probe Error",
                    MeasuredValue: ex.Message,
                    IndicatesPressure: false,
                    Severity: DiagnosticSeverity.Information,
                    Source: "Diagnostic Probing Exception",
                    CommandId: null,
                    Collector: "DiagnosticEvidenceCollector",
                    CommandVersion: "1.0.0"
                ));
            }

            return Task.FromResult<IReadOnlyList<TelemetryEvidence>>(evidence);
        }

        private static List<TelemetryEvidence> ProbeNetworkAdapters()
        {
            // Measure observable adapter presence and status; when unmeasurable,
            // report evidence as unavailable instead of fabricated.
            try
            {
                var interfaces = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces();
                var operational = interfaces.Where(nic =>
                    nic.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Loopback &&
                    nic.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Tunnel).ToList();
                int upCount = operational.Count(nic => nic.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up);
                return NetworkEvidenceFromSnapshot(operational.Count, upCount);
            }
            catch
            {
                return [NetworkEvidenceUnavailable("Network Probe", "Network adapter metrics unavailable")];
            }
        }

        internal static List<TelemetryEvidence> NetworkEvidenceFromSnapshot(int operationalCount, int upCount)
        {
            if (operationalCount == 0)
            {
                return [NetworkEvidenceUnavailable("Network Adapter State", "No non-loopback network adapters reported")];
            }

            bool disconnected = upCount == 0;
            return
            [
                new TelemetryEvidence(
                    HasMeasuredEvidence: true,
                    MetricName: "Network Adapter State",
                    MeasuredValue: $"{upCount} of {operationalCount} non-loopback adapters operational",
                    IndicatesPressure: disconnected,
                    Severity: disconnected ? DiagnosticSeverity.Warning : DiagnosticSeverity.Healthy,
                    Source: "Windows Network Adapter Telemetry",
                    CommandId: "wincare.utilities.dnstools",
                    Collector: "NetworkDiagnosticsCollector",
                    CommandVersion: "1.1.0"
                ),
            ];
        }

        private static TelemetryEvidence NetworkEvidenceUnavailable(string metricName, string measuredValue) => new(
            HasMeasuredEvidence: false,
            MetricName: metricName,
            MeasuredValue: measuredValue,
            IndicatesPressure: false,
            Severity: DiagnosticSeverity.Information,
            Source: "Windows Network Adapter Telemetry",
            CommandId: "wincare.utilities.dnstools",
            Collector: "NetworkDiagnosticsCollector",
            CommandVersion: "1.1.0"
        );

        private static List<TelemetryEvidence> ProbeStorageDrives()
        {
            var results = new List<TelemetryEvidence>();
            try
            {
                var drives = DriveInfo.GetDrives();
                foreach (var drive in drives)
                {
                    if (drive.IsReady && drive.DriveType == DriveType.Fixed)
                    {
                        var totalGb = drive.TotalSize / (1024.0 * 1024 * 1024);
                        var freeGb = drive.AvailableFreeSpace / (1024.0 * 1024 * 1024);
                        var freePercent = totalGb > 0 ? (freeGb / totalGb) * 100.0 : 100.0;
                        var isLow = freeGb < WinCare.Domain.Assessment.AssessmentPolicy.DiskFreeDoctorGb ||
                            freePercent < WinCare.Domain.Assessment.AssessmentPolicy.DiskFreePercentWarning;

                        results.Add(new TelemetryEvidence(
                            HasMeasuredEvidence: true,
                            MetricName: $"Storage Free Space ({drive.Name.TrimEnd('\\')})",
                            MeasuredValue: $"{freeGb:F1} GB free of {totalGb:F1} GB ({freePercent:F0}% available)",
                            IndicatesPressure: isLow,
                            Severity: isLow ? DiagnosticSeverity.Warning : DiagnosticSeverity.Healthy,
                            Source: "Kernel Storage Partition Telemetry",
                            CommandId: "wincare.systemcare.diskcleaner",
                            Collector: "StorageDiagnosticsCollector",
                            CommandVersion: "1.2.0"
                        ));
                    }
                }
            }
            catch
            {
                results.Add(new TelemetryEvidence(
                    HasMeasuredEvidence: false,
                    MetricName: "Storage Probe",
                    MeasuredValue: "Drive metrics unavailable",
                    IndicatesPressure: false,
                    Severity: DiagnosticSeverity.Information,
                    Source: "Storage Partition Inspection",
                    CommandId: "wincare.systemcare.diskcleaner",
                    Collector: "StorageDiagnosticsCollector",
                    CommandVersion: "1.2.0"
                ));
            }

            return results;
        }

        private static List<TelemetryEvidence> ProbeMemoryUsage()
        {
            var results = new List<TelemetryEvidence>();
            try
            {
                var gcInfo = GC.GetGCMemoryInfo();
                var totalBytes = gcInfo.TotalAvailableMemoryBytes;
                var memoryLoadPercent = gcInfo.MemoryLoadBytes > 0 && totalBytes > 0
                    ? (double)gcInfo.MemoryLoadBytes / totalBytes * 100.0
                    : 0.0;

                // GC bookkeeping describes the WinCare process, not machine-wide
                // memory pressure; when the runtime reports no usable figures, mark unavailable.
                if (totalBytes <= 0)
                {
                    results.Add(new TelemetryEvidence(
                        HasMeasuredEvidence: false,
                        MetricName: "Process Memory (WinCare)",
                        MeasuredValue: "Memory metrics unavailable from the runtime",
                        IndicatesPressure: false,
                        Severity: DiagnosticSeverity.Information,
                        Source: "WinCare Process Runtime",
                        CommandId: "wincare.systemcare.ramoptimizer",
                        Collector: "MemoryDiagnosticsCollector",
                        CommandVersion: "1.2.0"
                    ));
                    return results;
                }

                var isHighMemory = memoryLoadPercent > WinCare.Domain.Assessment.AssessmentPolicy.MemoryHighLoadPercent;

                results.Add(new TelemetryEvidence(
                    HasMeasuredEvidence: true,
                    MetricName: "Process Memory (WinCare)",
                    MeasuredValue: $"{memoryLoadPercent:F0}% utilized ({gcInfo.MemoryLoadBytes / (1024 * 1024):N0} MB in use by the WinCare process, not the whole machine)",
                    IndicatesPressure: isHighMemory,
                    Severity: isHighMemory ? DiagnosticSeverity.Warning : DiagnosticSeverity.Healthy,
                    Source: "WinCare Process Runtime",
                    CommandId: "wincare.systemcare.ramoptimizer",
                    Collector: "MemoryDiagnosticsCollector",
                    CommandVersion: "1.2.0"
                ));
            }
            catch
            {
                results.Add(new TelemetryEvidence(
                    HasMeasuredEvidence: false,
                    MetricName: "Memory Probe",
                    MeasuredValue: "Memory load metrics unavailable",
                    IndicatesPressure: false,
                    Severity: DiagnosticSeverity.Information,
                    Source: "WinCare Process Runtime",
                    CommandId: "wincare.systemcare.ramoptimizer",
                    Collector: "MemoryDiagnosticsCollector",
                    CommandVersion: "1.2.0"
                ));
            }

            return results;
        }
    }
}
