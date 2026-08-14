namespace WinCare.Application.Commands;

/// <summary>
/// Request-scoped execution policy for the command dispatcher.
/// </summary>
/// <param name="ReviewApproved">Whether a prior planning review has approved mutation.</param>
/// <param name="Deadline">Optional wall-clock deadline for execution.</param>
public sealed record CommandExecutionOptions(
    bool ReviewApproved,
    DateTimeOffset? Deadline = null)
{
    /// <summary>
    /// Gets the default, non-applicative options.
    /// </summary>
    public static CommandExecutionOptions Default { get; } = new(
        ReviewApproved: false,
        Deadline: null);
}
