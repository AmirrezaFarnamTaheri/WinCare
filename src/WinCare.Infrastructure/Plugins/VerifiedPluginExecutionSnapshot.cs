using WinCare.Application.Plugins;
using WinCare.Application.Storage;

namespace WinCare.Infrastructure.Plugins;

/// <summary>
/// An execution-time copy of a previously admitted package. The exact copied bytes
/// are checked against the external admission digest before the script starts, then
/// file handles are held read-only until the child process exits. This prevents a
/// mutation to the original plugin directory from changing the code we launch.
/// It does not grant unprivileged scripts an OS sandbox or enable elevated scripts.
/// </summary>
internal sealed class VerifiedPluginExecutionSnapshot : IDisposable
{
    private const int MaximumFiles = 10_000;
    private const int MaximumDepth = 64;
    private const long MaximumBytes = 200L * 1024 * 1024;
    private readonly List<FileStream> _readLeases = [];
    private bool _disposed;

    public string Root { get; }
    public string ScriptFullPath { get; }

    private VerifiedPluginExecutionSnapshot(string root, string scriptPath)
    {
        Root = root;
        ScriptFullPath = scriptPath;
    }

    public static VerifiedPluginExecutionSnapshot Create(
        string pluginDirectory, string scriptFullPath, string expectedDigest,
        string? stagingParent = null, CancellationToken cancellationToken = default)
    {
        if (expectedDigest.Length != 64 || !expectedDigest.All(Uri.IsHexDigit))
            throw new InvalidDataException("Admitted plugin payload digest is invalid.");
        string sourceRoot = Path.GetFullPath(pluginDirectory);
        string relativeScript = Path.GetRelativePath(sourceRoot, Path.GetFullPath(scriptFullPath));
        if (relativeScript is "." or ".." || Path.IsPathRooted(relativeScript) ||
            relativeScript.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidDataException("Executable script path escapes the admitted package.");

        string parent = stagingParent ??
            Path.Combine(AppDataRoot.Current, "plugin-execution-snapshots");
        Directory.CreateDirectory(parent);
        string snapshotRoot = Path.Combine(parent, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(snapshotRoot);
        var snapshot = new VerifiedPluginExecutionSnapshot(
            snapshotRoot, Path.Combine(snapshotRoot, relativeScript));

        try
        {
            // Never use a recursive search before checking links and bounded size.
            var stack = new Stack<(string Path, int Depth)>();
            var copiedFiles = new List<string>();
            stack.Push((sourceRoot, 0));
            int count = 0;
            long copiedBytes = 0;
            while (stack.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                (string directory, int depth) = stack.Pop();
                if (depth > MaximumDepth)
                    throw new InvalidDataException("Plugin package nesting is excessive.");

                foreach (string entry in Directory.EnumerateFileSystemEntries(
                             directory, "*", SearchOption.TopDirectoryOnly))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (++count > MaximumFiles)
                        throw new InvalidDataException("Plugin package has too many entries.");
                    FileAttributes attrs = File.GetAttributes(entry);
                    if ((attrs & FileAttributes.ReparsePoint) != 0)
                        throw new InvalidDataException("Plugin package contains a reparse point.");
                    string relative = Path.GetRelativePath(sourceRoot, entry);
                    if (relative is "." or ".." || Path.IsPathRooted(relative) ||
                        relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                        throw new InvalidDataException("Plugin package path escaped its source directory.");
                    string stagedPath = Path.Combine(snapshotRoot, relative);
                    if ((attrs & FileAttributes.Directory) != 0)
                    {
                        Directory.CreateDirectory(stagedPath);
                        stack.Push((entry, depth + 1));
                        continue;
                    }

                    using FileStream source = new(entry, FileMode.Open, FileAccess.Read, FileShare.Read);
                    copiedBytes = checked(copiedBytes + source.Length);
                    if (copiedBytes > MaximumBytes)
                        throw new InvalidDataException("Plugin package exceeds the admission size limit.");
                    using (FileStream output = new(stagedPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        source.CopyTo(output);
                        output.Flush(flushToDisk: true);
                    }
                    copiedFiles.Add(stagedPath);
                }
            }

            string digest = PluginAdmissionTrustStore.ComputePluginPayloadDigest(snapshotRoot);
            if (!string.Equals(digest, expectedDigest, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Staged plugin package bytes differ from admitted payload.");

            // Acquire all file handles before validating the digest again. Read-only
            // sharing denies concurrent write/delete opens on Windows for these files
            // during execution; the final digest closes the acquisition window.
            foreach (string stagedFile in copiedFiles)
                snapshot._readLeases.Add(new FileStream(
                    stagedFile, FileMode.Open, FileAccess.Read, FileShare.Read));
            digest = PluginAdmissionTrustStore.ComputePluginPayloadDigest(snapshotRoot);
            if (!string.Equals(digest, expectedDigest, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Staged plugin changed while locking execution files.");
            if (!File.Exists(snapshot.ScriptFullPath))
                throw new InvalidDataException("The admitted executable is missing from the staged package.");

            return snapshot;
        }
        catch
        {
            snapshot.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (FileStream stream in _readLeases)
            stream.Dispose();
        _readLeases.Clear();
        try
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine($"[PluginExecutionSnapshot] Cleanup deferred: {ex}");
        }
    }
}
