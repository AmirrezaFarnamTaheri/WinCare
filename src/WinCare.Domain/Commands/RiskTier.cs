namespace WinCare.Domain.Commands;

/// <summary>
/// Operational admission risk tier for command execution.
/// </summary>
public enum RiskTier
{
    /// <summary>
    /// Safe / idempotent command. Admitted directly without preflight review plan ceremony.
    /// Includes read-only queries, telemetry exports, and safe routine maintenance cleanups.
    /// </summary>
    Safe = 0,

    /// <summary>
    /// Moderate operational risk. Admitted with user review confirmation (<c>ReviewApproved = true</c>),
    /// but does not strictly require a two-phase cryptographic preflight plan.
    /// </summary>
    Moderate = 1,

    /// <summary>
    /// Destructive / catastrophic operational risk (e.g. registry wipes, disk format, driver removals, maximum legacy profiles).
    /// Strictly requires a preflight preview, SHA-256 parameter digest verification, and an active <see cref="ApprovedMutationPlan"/>.
    /// </summary>
    Destructive = 2,
}
