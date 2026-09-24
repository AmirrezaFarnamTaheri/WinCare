using System.Diagnostics;
using System.Text;
using WinCare.Application.Commands;

namespace WinCare.Infrastructure.Commands;

/// <summary>
/// Executes a native child process without a shell, with bounded output, cancellation and a hard deadline.
/// </summary>
public sealed class BoundedProcessRunner : IBoundedProcessRunner
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Runs a process asynchronously with bounded output and hard timeout.
    /// </summary>
    public async Task<ProcessExecutionResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        CancellationToken cancellationToken,
        TimeSpan? timeout = null,
        int maxCharacters = IBoundedProcessRunner.DefaultMaxCharacters,
        string? workingDirectory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(arguments);
        if (maxCharacters is < 1024 or > 4 * 1024 * 1024)
        {
            throw new ArgumentOutOfRangeException(nameof(maxCharacters));
        }

        cancellationToken.ThrowIfCancellationRequested();
        // Validate and establish the deadline before starting a potentially mutating child.
        using CancellationTokenSource timeoutCts = new(timeout ?? DefaultTimeout);
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        linked.Token.ThrowIfCancellationRequested();

        // Resolve the tool to an absolute path before launching so the exact binary an elevated
        // WinCare runs is knowable and can be reported in the execution receipt.
        string resolvedExecutable = ResolveExecutable(fileName);
        var startInfo = new ProcessStartInfo
        {
            FileName = resolvedExecutable,
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
                started.Elapsed,
                string.Equals(resolvedExecutable, fileName, StringComparison.Ordinal) ? string.Empty : resolvedExecutable);
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

    private static readonly string? CachedSystemDirectory = OperatingSystem.IsWindows()
        ? Environment.GetFolderPath(Environment.SpecialFolder.System)
        : null;

    /// <summary>
    /// Resolves a tool name to an absolute path. Tools that do not live in System32 (winget,
    /// sysmon, wg, adb, ...) are resolved by walking PATH <em>explicitly</em> rather than letting
    /// CreateProcess search it: the implicit search also covers this process's own directory and
    /// the working directory, so a user-writable early PATH entry would otherwise silently supply
    /// the binary an elevated WinCare executes. Callers that need a proven binary should pass an
    /// absolute path plus an expected digest, the way the studio-adb/studio-xbox-fse commands do.
    /// </summary>
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
        if (CachedSystemDirectory != null)
        {
            string systemCandidate = Path.Combine(CachedSystemDirectory, fileName);
            if (File.Exists(systemCandidate))
            {
                return systemCandidate;
            }
        }

        string? searchPath = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(searchPath))
        {
            foreach (string directory in searchPath.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                try
                {
                    string candidate = Path.Combine(directory.Trim('"'), fileName);
                    if (File.Exists(candidate))
                    {
                        return Path.GetFullPath(candidate);
                    }
                }
                catch (ArgumentException) { }
            }
        }

        // Nothing could be resolved to an absolute path; let the OS attempt the launch so the
        // existing missing-dependency failure mode is preserved.
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
        char[] rented = System.Buffers.ArrayPool<char>.Shared.Rent(4096);
        try
        {
            var text = new StringBuilder(Math.Min(maxCharacters, 64 * 1024));
            bool truncated = false;
            while (true)
            {
                int read = await reader.ReadAsync(rented.AsMemory(0, rented.Length), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }
                int remaining = maxCharacters - text.Length;
                if (remaining > 0)
                {
                    int copy = Math.Min(remaining, read);
                    text.Append(rented, 0, copy);
                    truncated |= copy < read;
                }
                else
                {
                    truncated = true;
                }
            }
            return (text.ToString(), truncated);
        }
        finally
        {
            System.Buffers.ArrayPool<char>.Shared.Return(rented);
        }
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
