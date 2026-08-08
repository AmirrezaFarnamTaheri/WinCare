using System.Diagnostics;
using System.Diagnostics.Tracing;

namespace WinCare.Infrastructure.Observability;

[EventSource(Name = "WinCare-Native")]
internal sealed class WinCareEventSource : EventSource
{
    internal static WinCareEventSource Log { get; } = new();

    [Event(1, Level = EventLevel.Informational, Message = "Startup stage {0} at {1} ms")]
    public void StartupStage(string stage, double elapsedMilliseconds)
    {
        if (IsEnabled())
        {
            WriteEvent(1, stage, elapsedMilliseconds);
        }
    }
}

/// <summary>
/// Out-of-process and in-process startup telemetry.
/// </summary>
public static class StartupTelemetry
{
    private static readonly long ProcessStartTimestamp = Stopwatch.GetTimestamp();

    /// <summary>
    /// Emits a named startup stage with elapsed wall-clock time since process start.
    /// </summary>
    public static void Mark(string stage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stage);
        double elapsedMilliseconds = Stopwatch.GetElapsedTime(ProcessStartTimestamp).TotalMilliseconds;
        WinCareEventSource.Log.StartupStage(stage, elapsedMilliseconds);
        Debug.WriteLine($"WinCare startup: {stage} at {elapsedMilliseconds:F1} ms");
    }
}
