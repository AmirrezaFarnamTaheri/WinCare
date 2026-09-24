namespace WinCare.Application.Commands;

/// <summary>
/// Result of a bounded native process execution.
/// </summary>
/// <param name="ExitCode">Process exit code.</param>
/// <param name="StandardOutput">Captured standard output text.</param>
/// <param name="StandardError">Captured standard error text.</param>
/// <param name="OutputTruncated">Whether output exceeded the character limit.</param>
/// <param name="Duration">Total execution duration.</param>
/// <param name="ResolvedExecutablePath">The absolute path actually launched, when the caller passed a bare tool name; empty when the caller supplied the path.</param>
public sealed record ProcessExecutionResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    bool OutputTruncated,
    TimeSpan Duration,
    string ResolvedExecutablePath = "");

/// <summary>
/// Executes a native child process without a shell, with bounded output, cancellation, and a hard
/// deadline. Never invokes a shell and always passes host data as separate argument-list entries.
/// </summary>
public interface IBoundedProcessRunner
{
    /// <summary>Default upper bound on captured process output, in characters.</summary>
    public const int DefaultMaxCharacters = 512 * 1024;

    /// <summary>
    /// Runs a process asynchronously with bounded output and a hard timeout.
    /// </summary>
    Task<ProcessExecutionResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        CancellationToken cancellationToken,
        TimeSpan? timeout = null,
        int maxCharacters = DefaultMaxCharacters,
        string? workingDirectory = null);
}

/// <summary>
/// Thrown when a required native tool dependency is unavailable or cannot be started on the host.
/// </summary>
/// <param name="dependency">Name of the missing dependency.</param>
/// <param name="message">Error description.</param>
/// <param name="inner">Optional inner exception.</param>
public sealed class CommandDependencyException(string dependency, string message, Exception? inner = null) : Exception(message, inner)
{
    /// <summary>
    /// Gets the name of the missing dependency.
    /// </summary>
    public string Dependency { get; } = dependency;
}
