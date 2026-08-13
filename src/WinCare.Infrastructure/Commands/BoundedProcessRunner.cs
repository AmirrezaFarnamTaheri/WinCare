using System.Diagnostics;
using System.Text;

namespace WinCare.Infrastructure.Commands;

public sealed record ProcessExecutionResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    bool OutputTruncated,
    TimeSpan Duration);

/// <summary>
/// Executes a native child process without a shell, with bounded output, cancellation and a hard deadline.
/// </summary>
public sealed class BoundedProcessRunner
{
    private const int DefaultMaxCharacters = 512 * 1024;
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    public async Task<ProcessExecutionResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        CancellationToken cancellationToken,
        TimeSpan? timeout = null,
        int maxCharacters = DefaultMaxCharacters,
        string? workingDirectory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(arguments);
        if (maxCharacters is < 1024 or > 4 * 1024 * 1024)
        {
            throw new ArgumentOutOfRangeException(nameof(maxCharacters));
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = ResolveExecutable(fileName),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var started = Stopwatch.StartNew();
        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException($"Failed to start '{fileName}'.");
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            throw new CommandDependencyException(fileName, $"Required executable '{fileName}' could not be started.", ex);
        }

        using CancellationTokenSource timeoutCts = new(timeout ?? DefaultTimeout);
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        Task<(string Text, bool Truncated)> stdout = ReadBoundedAsync(process.StandardOutput, maxCharacters, linked.Token);
        Task<(string Text, bool Truncated)> stderr = ReadBoundedAsync(process.StandardError, maxCharacters, linked.Token);

        try
        {
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
            var output = await stdout.ConfigureAwait(false);
            var error = await stderr.ConfigureAwait(false);
            started.Stop();
            return new ProcessExecutionResult(
                process.ExitCode,
                output.Text,
                error.Text,
                output.Truncated || error.Truncated,
                started.Elapsed);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            await ObserveReaderCompletionAsync(stdout, stderr).ConfigureAwait(false);
            if (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"'{fileName}' exceeded the {(timeout ?? DefaultTimeout).TotalSeconds:0.#}-second limit.");
            }
            throw;
        }
    }

    private static string ResolveExecutable(string fileName)
    {
        if (Path.IsPathRooted(fileName))
        {
            return Path.GetFullPath(fileName);
        }
        if (fileName.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) >= 0)
        {
            return Path.GetFullPath(fileName);
        }
        if (OperatingSystem.IsWindows())
        {
            string systemDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System);
            string systemCandidate = Path.Combine(systemDirectory, fileName);
            if (File.Exists(systemCandidate))
            {
                return systemCandidate;
            }
        }
        return fileName;
    }

    private static async Task ObserveReaderCompletionAsync(Task stdout, Task stderr)
    {
        try
        {
            await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or IOException)
        {
            // Cancellation already owns the command outcome; this only observes reader completion/faults.
        }
    }

    private static async Task<(string Text, bool Truncated)> ReadBoundedAsync(
        StreamReader reader,
        int maxCharacters,
        CancellationToken cancellationToken)
    {
        char[] buffer = new char[4096];
        var text = new StringBuilder(Math.Min(maxCharacters, 64 * 1024));
        bool truncated = false;
        while (true)
        {
            int read = await reader.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }
            int remaining = maxCharacters - text.Length;
            if (remaining > 0)
            {
                int copy = Math.Min(remaining, read);
                text.Append(buffer, 0, copy);
                truncated |= copy < read;
            }
            else
            {
                truncated = true;
            }
        }
        return (text.ToString(), truncated);
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
    }
}

public sealed class CommandDependencyException(string dependency, string message, Exception? inner = null) : Exception(message, inner)
{
    public string Dependency { get; } = dependency;
}
