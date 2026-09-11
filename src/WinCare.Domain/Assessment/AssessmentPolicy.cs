namespace WinCare.Domain.Assessment;

/// <summary>
/// F-020: centralized, versioned assessment thresholds. Every entry point (Checkup page,
/// AI Doctor evidence collector, executor health command) must read its boundaries from
/// this policy instead of re-declaring divergent local constants, so the same measurement
/// cannot be classified differently by different surfaces.
/// </summary>
public static class AssessmentPolicy
{
    /// <summary>Version of the assessment rule set; bump when thresholds change.</summary>
    public const string Version = "1.0";

    /// <summary>Free space at or below which Checkup classifies a drive as critical.</summary>
    public const double DiskFreeCriticalGb = 10.0;

    /// <summary>Free space at or below which Checkup classifies a drive as needing attention.</summary>
    public const double DiskFreeWarningGb = 20.0;

    /// <summary>Free space boundary the AI Doctor uses for its storage recommendation.</summary>
    public const double DiskFreeDoctorGb = 15.0;

    /// <summary>Free-space percentage boundary shared by the Doctor and health overview.</summary>
    public const double DiskFreePercentWarning = 10.0;

    /// <summary>Memory load percentage at or above which memory pressure is reported.</summary>
    public const double MemoryHighLoadPercent = 85.0;
}
