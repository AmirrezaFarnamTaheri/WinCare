using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Microsoft.PowerShell;

internal static class WinCareHost
{
    private const string PayloadResource = "WinCare.Payload.zip";
    private const long MaximumPayloadBytes = 1024L * 1024L * 1024L;
    private const long MaximumMemberBytes = 256L * 1024L * 1024L;
    private const int MaximumMembers = 20_000;
    private static readonly StringComparer PathComparer = StringComparer.OrdinalIgnoreCase;
    private static readonly HashSet<string> WindowsReservedNames = new(PathComparer)
    {
        "con", "prn", "aux", "nul",
        "com1", "com2", "com3", "com4", "com5", "com6", "com7", "com8", "com9",
        "lpt1", "lpt2", "lpt3", "lpt4", "lpt5", "lpt6", "lpt7", "lpt8", "lpt9",
    };

    private sealed record PayloadManifest(Dictionary<string, string> Files, string Sha256);
    private sealed record PreparedPayload(string Root, IReadOnlyDictionary<string, string> Files);

#if WINCARE_GUI
    [STAThread]
#endif
    public static int Main(string[] args)
    {
        try
        {
            var prepared = PreparePayload();
            var root = prepared.Root;
            var selfTest = args.Length > 0 && string.Equals(args[0], "--wincare-self-test", StringComparison.OrdinalIgnoreCase);
#if WINCARE_GUI
            var scriptName = selfTest ? "WinCare.ps1" : "WinCare-GUI.ps1";
            var useSta = !selfTest;
#elif WINCARE_TUI
            var scriptName = "WinCare.ps1";
            var useSta = false;
#else
            var scriptName = "WinCare.ps1";
            var useSta = false;
#endif
            var script = Path.Combine(root, scriptName);
            if (!File.Exists(script) || IsReparsePoint(script))
                throw new InvalidOperationException("Embedded WinCare payload is incomplete or unsafe.");

            var forwarded = args
                .Where(argument => !string.Equals(argument, "--gui", StringComparison.OrdinalIgnoreCase))
                .Skip(selfTest ? 1 : 0)
                .ToList();
            if (selfTest)
                forwarded.AddRange(["-Command", "system", "-Json"]);

            if (!prepared.Files.TryGetValue(scriptName, out var expectedScriptHash))
                throw new InvalidDataException("Embedded WinCare entry script is not present in the verified payload manifest.");
            var scriptText = ReadVerifiedScript(script, expectedScriptHash);
            var invocation = BuildInvocation(scriptText, forwarded);

            var previousDirectory = Environment.CurrentDirectory;
            var previousHostMarker = Environment.GetEnvironmentVariable("WINCARE_STANDALONE_HOST");
            var previousRootMarker = Environment.GetEnvironmentVariable("WINCARE_STANDALONE_ROOT");
            try
            {
                Environment.CurrentDirectory = root;
                Environment.SetEnvironmentVariable("WINCARE_STANDALONE_HOST", "1");
                Environment.SetEnvironmentVariable("WINCARE_STANDALONE_ROOT", root);
#if WINCARE_GUI
                return InvokeGuiPowerShell(invocation);
#else
                // The host admits only the cryptographically bound embedded closure. The process-local
                // policy override permits that verified closure to import its verified script modules; it
                // does not weaken machine/user policy or admit an arbitrary external script path.
                var shellArguments = new List<string> { "-NoLogo", "-NoProfile", "-ExecutionPolicy", "Bypass" };
                if (selfTest)
                    shellArguments.Add("-NonInteractive");
                if (useSta)
                    shellArguments.Add("-STA");
                shellArguments.Add("-Command");
                shellArguments.Add(invocation);
                return ConsoleShell.Start(null, null, shellArguments.ToArray());
#endif
            }
            finally
            {
                Environment.CurrentDirectory = previousDirectory;
                Environment.SetEnvironmentVariable("WINCARE_STANDALONE_HOST", previousHostMarker);
                Environment.SetEnvironmentVariable("WINCARE_STANDALONE_ROOT", previousRootMarker);
            }
        }
        catch (Exception error)
        {
            Trace.TraceError(error.ToString());
            try
            {
                Console.Error.WriteLine(error.Message);
            }
            catch
            {
                // GUI-subsystem executables intentionally have no console error handle.
            }
            return 1;
        }
    }

#if WINCARE_GUI
    private static int InvokeGuiPowerShell(string invocation)
    {
        var initialSessionState = InitialSessionState.CreateDefault();
        initialSessionState.ExecutionPolicy = ExecutionPolicy.Bypass;
        using var runspace = RunspaceFactory.CreateRunspace(initialSessionState);
        runspace.ApartmentState = ApartmentState.STA;
        runspace.ThreadOptions = PSThreadOptions.UseCurrentThread;
        runspace.Open();
        using var powershell = PowerShell.Create();
        powershell.Runspace = runspace;
        powershell.AddScript(invocation, useLocalScope: false);
        var results = powershell.Invoke();
        foreach (var result in results)
        {
            if (result is not null)
                Trace.WriteLine(result.ToString());
        }
        if (!powershell.HadErrors)
            return 0;
        foreach (var error in powershell.Streams.Error)
            Trace.TraceError(error.ToString());
        return 1;
    }
#endif

    private static readonly Dictionary<string, bool> EntrypointParameters = new(PathComparer)
    {
        ["NoLogo"] = false,
        ["Ascii"] = false,
        ["ReadOnly"] = false,
        ["Theme"] = true,
        ["Command"] = true,
        ["ArgumentsJson"] = true,
        ["Apply"] = false,
        ["Json"] = false,
        ["Gui"] = false,
        ["Verbose"] = false,
        ["Debug"] = false,
        ["ErrorAction"] = true,
        ["WarningAction"] = true,
        ["InformationAction"] = true,
        ["ProgressAction"] = true,
    };

    private static string ReadVerifiedScript(string path, string expectedHash)
    {
        const int maximumScriptBytes = 4 * 1024 * 1024;
        var item = new FileInfo(path);
        if (!item.Exists || item.Attributes.HasFlag(FileAttributes.ReparsePoint) || item.Length <= 0 || item.Length > maximumScriptBytes)
            throw new InvalidDataException("Embedded WinCare entry script is missing, unsafe, or oversized.");
        var bytes = new byte[(int)item.Length];
        try
        {
            using var stream = new FileStream(item.FullName, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.SequentialScan);
            var offset = 0;
            while (offset < bytes.Length)
            {
                var read = stream.Read(bytes, offset, bytes.Length - offset);
                if (read == 0)
                    throw new EndOfStreamException("Embedded WinCare entry script ended unexpectedly.");
                offset += read;
            }
            if (stream.ReadByte() != -1)
                throw new InvalidDataException("Embedded WinCare entry script changed while reading.");
            var observedHash = SHA256.HashData(bytes);
            try
            {
                if (!CryptographicOperations.FixedTimeEquals(observedHash, Convert.FromHexString(expectedHash)))
                    throw new InvalidDataException("Embedded WinCare entry script hash does not match the verified payload manifest.");
            }
            finally
            {
                CryptographicOperations.ZeroMemory(observedHash);
            }
            return new UTF8Encoding(false, true).GetString(bytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private static string BuildInvocation(string scriptText, IReadOnlyList<string> arguments)
    {
        var command = new StringBuilder();
        command.Append("$ErrorActionPreference='Stop';&([scriptblock]::Create(");
        AppendPowerShellLiteral(command, scriptText);
        command.Append("))");
        for (var index = 0; index < arguments.Count; index++)
        {
            var token = arguments[index];
            if (token is "-?" or "--help")
            {
                command.Append(" -?");
                continue;
            }
            if (string.IsNullOrWhiteSpace(token) || !token.StartsWith("-", StringComparison.Ordinal))
                throw new ArgumentException($"Unexpected positional standalone argument: {token}");
            var parameter = token.TrimStart('-');
            string? inlineValue = null;
            var separator = parameter.IndexOf(':');
            if (separator >= 0)
            {
                inlineValue = parameter[(separator + 1)..];
                parameter = parameter[..separator];
            }
            var canonical = EntrypointParameters.Keys.SingleOrDefault(name => PathComparer.Equals(name, parameter));
            if (canonical is null)
                throw new ArgumentException($"Unsupported standalone parameter: {token}");
            var requiresValue = EntrypointParameters[canonical];
            command.Append(" -").Append(canonical);
            if (requiresValue)
            {
                var value = inlineValue;
                if (value is null)
                {
                    if (++index >= arguments.Count)
                        throw new ArgumentException($"Standalone parameter requires a value: -{canonical}");
                    value = arguments[index];
                }
                command.Append(' ');
                AppendPowerShellLiteral(command, value);
            }
            else if (inlineValue is not null)
            {
                var normalized = inlineValue.TrimStart('$');
                if (!bool.TryParse(normalized, out var enabled))
                    throw new ArgumentException($"Standalone switch has an invalid Boolean value: {token}");
                command.Append(enabled ? ":$true" : ":$false");
            }
        }
        return command.ToString();
    }

    private static void AppendPowerShellLiteral(StringBuilder target, string value)
    {
        target.Append('\'');
        target.Append(value.Replace("'", "''", StringComparison.Ordinal));
        target.Append('\'');
    }

    private static PreparedPayload PreparePayload()
    {
        var payload = ReadEmbeddedPayload();
        var payloadHash = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
        var manifest = ReadPayloadManifest(payload);
        var expectedPayloadHash = GetAssemblyMetadata("WinCarePayloadSha256");
        var expectedManifestHash = GetAssemblyMetadata("WinCarePayloadManifestSha256");
        if (!string.Equals(payloadHash, expectedPayloadHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Embedded payload hash does not match assembly metadata.");
        if (!string.Equals(manifest.Sha256, expectedManifestHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Embedded payload manifest hash does not match assembly metadata.");
        var expected = manifest.Files;
        var runtimeRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WinCare",
            "Runtime");
        Directory.CreateDirectory(runtimeRoot);
        EnsureDirectoryIsSafe(runtimeRoot);
        var cache = Path.Combine(runtimeRoot, payloadHash);
        var mutexName = $@"Local\WinCare.Payload.{payloadHash[..32]}";
        using var mutex = new Mutex(false, mutexName);
        var ownsMutex = false;
        try
        {
            try
            {
                ownsMutex = mutex.WaitOne(TimeSpan.FromMinutes(2));
            }
            catch (AbandonedMutexException)
            {
                ownsMutex = true;
            }
            if (!ownsMutex)
                throw new TimeoutException("Timed out waiting for the WinCare payload cache lock.");

            if (ValidateCache(cache, payloadHash, expected))
                return new PreparedPayload(cache, expected);
            if (Directory.Exists(cache))
                DeleteTreeNoFollow(cache);

            var staging = Path.Combine(runtimeRoot, $".{payloadHash}.staging-{Guid.NewGuid():N}");
            Directory.CreateDirectory(staging);
            try
            {
                ExtractPayload(payload, staging, expected);
                File.WriteAllText(
                    Path.Combine(staging, ".payload-identity"),
                    payloadHash + Environment.NewLine,
                    new UTF8Encoding(false));
                if (!ValidateCache(staging, payloadHash, expected))
                    throw new InvalidDataException("Extracted WinCare payload failed verification.");
                Directory.Move(staging, cache);
            }
            catch
            {
                if (Directory.Exists(staging))
                    DeleteTreeNoFollow(staging);
                throw;
            }
            return new PreparedPayload(cache, expected);
        }
        finally
        {
            if (ownsMutex)
                mutex.ReleaseMutex();
            CryptographicOperations.ZeroMemory(payload);
        }
    }

    private static byte[] ReadEmbeddedPayload()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(PayloadResource)
            ?? throw new InvalidOperationException("Embedded WinCare payload is missing.");
        if (stream.CanSeek && (stream.Length <= 0 || stream.Length > MaximumPayloadBytes))
            throw new InvalidDataException("Embedded WinCare payload size is outside the permitted range.");
        using var memory = new MemoryStream();
        var buffer = new byte[1024 * 1024];
        long total = 0;
        try
        {
            while (true)
            {
                var read = stream.Read(buffer, 0, buffer.Length);
                if (read == 0)
                    break;
                total += read;
                if (total > MaximumPayloadBytes)
                    throw new InvalidDataException("Embedded WinCare payload exceeds the byte ceiling.");
                memory.Write(buffer, 0, read);
            }
            if (total == 0)
                throw new InvalidDataException("Embedded WinCare payload is empty.");
            return memory.ToArray();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(buffer);
        }
    }

    private static PayloadManifest ReadPayloadManifest(byte[] payload)
    {
        using var memory = new MemoryStream(payload, writable: false);
        using var archive = new ZipArchive(memory, ZipArchiveMode.Read, leaveOpen: false);
        var entries = ValidateArchiveEntries(archive);
        var manifestPair = entries.SingleOrDefault(pair => PathComparer.Equals(pair.Key, "PAYLOAD-MANIFEST.sha256"));
        if (manifestPair.Value is null)
            throw new InvalidDataException("PAYLOAD-MANIFEST.sha256 is missing from the embedded payload.");
        if (manifestPair.Value.Length <= 0 || manifestPair.Value.Length > 4 * 1024 * 1024)
            throw new InvalidDataException("Embedded payload manifest size is invalid.");
        byte[] manifestBytes;
        using (var input = manifestPair.Value.Open())
        using (var output = new MemoryStream((int)manifestPair.Value.Length))
        {
            input.CopyTo(output);
            manifestBytes = output.ToArray();
        }
        try
        {
            var manifestHash = Convert.ToHexString(SHA256.HashData(manifestBytes)).ToLowerInvariant();
            using var manifestMemory = new MemoryStream(manifestBytes, writable: false);
            using var reader = new StreamReader(manifestMemory, Encoding.ASCII, detectEncodingFromByteOrderMarks: false, bufferSize: 65536, leaveOpen: false);
            var expected = new Dictionary<string, string>(PathComparer);
            while (reader.ReadLine() is { } line)
            {
                if (line.Length < 67 || line[64] != ' ' || line[65] != ' ')
                    throw new InvalidDataException("Malformed embedded payload manifest line.");
                var hash = line[..64];
                var relative = NormalizeRelativePath(line[66..]);
                if (!hash.All(character => (character >= '0' && character <= '9') || (character >= 'a' && character <= 'f')))
                    throw new InvalidDataException("Embedded payload manifest contains an invalid SHA-256 value.");
                if (!expected.TryAdd(relative, hash))
                    throw new InvalidDataException($"Duplicate embedded payload manifest path: {relative}");
            }
            if (expected.Count == 0)
                throw new InvalidDataException("Embedded payload manifest is empty.");
            var archiveFiles = entries.Keys.Where(name => !PathComparer.Equals(name, "PAYLOAD-MANIFEST.sha256")).ToHashSet(PathComparer);
            if (!archiveFiles.SetEquals(expected.Keys))
                throw new InvalidDataException("Embedded payload manifest membership does not match the ZIP.");
            return new PayloadManifest(expected, manifestHash);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(manifestBytes);
        }
    }

    private static string GetAssemblyMetadata(string key)
    {
        var values = Assembly.GetExecutingAssembly()
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .Where(attribute => string.Equals(attribute.Key, key, StringComparison.Ordinal))
            .Select(attribute => attribute.Value)
            .ToArray();
        if (values.Length != 1 || string.IsNullOrWhiteSpace(values[0]))
            throw new InvalidDataException($"Required assembly metadata is missing or ambiguous: {key}");
        var value = values[0]!;
        if (value.Length != 64 || !value.All(character => Uri.IsHexDigit(character)))
            throw new InvalidDataException($"Assembly metadata is not a SHA-256 value: {key}");
        return value.ToLowerInvariant();
    }

    private static Dictionary<string, ZipArchiveEntry> ValidateArchiveEntries(ZipArchive archive)
    {
        if (archive.Entries.Count == 0 || archive.Entries.Count > MaximumMembers)
            throw new InvalidDataException("Embedded payload member count is outside the permitted range.");
        var roots = new HashSet<string>(PathComparer);
        var entries = new Dictionary<string, ZipArchiveEntry>(PathComparer);
        long totalBytes = 0;
        foreach (var entry in archive.Entries)
        {
            if (entry.FullName.Contains('\\') || entry.FullName.Contains('\0'))
                throw new InvalidDataException($"Unsafe embedded payload path: {entry.FullName}");
            var isDirectory = entry.FullName.EndsWith("/", StringComparison.Ordinal);
            var full = NormalizeRelativePath(entry.FullName.TrimEnd('/'));
            var components = full.Split('/');
            roots.Add(components[0]);
            if (isDirectory)
                continue;
            if (components.Length < 2)
                throw new InvalidDataException($"Embedded payload member is outside the release root: {entry.FullName}");
            var relative = NormalizeRelativePath(string.Join('/', components.Skip(1)));
            var unixMode = (entry.ExternalAttributes >> 16) & 0xFFFF;
            var fileType = unixMode & 0xF000;
            if (fileType != 0 && fileType != 0x8000)
                throw new InvalidDataException($"Embedded payload contains a non-regular member: {relative}");
            if (entry.Length < 0 || entry.Length > MaximumMemberBytes)
                throw new InvalidDataException($"Embedded payload member exceeds the size ceiling: {relative}");
            totalBytes += entry.Length;
            if (totalBytes > MaximumPayloadBytes)
                throw new InvalidDataException("Embedded payload expanded size exceeds the aggregate ceiling.");
            var ratio = entry.Length / Math.Max(1d, entry.CompressedLength);
            if (entry.Length > 1024 * 1024 && ratio > 250d)
                throw new InvalidDataException($"Embedded payload member has a suspicious compression ratio: {relative}");
            if (!entries.TryAdd(relative, entry))
                throw new InvalidDataException($"Duplicate or case-colliding embedded payload path: {relative}");
        }
        if (roots.Count != 1)
            throw new InvalidDataException("Embedded payload must contain exactly one top-level directory.");
        return entries;
    }

    private static void ExtractPayload(byte[] payload, string staging, IReadOnlyDictionary<string, string> expected)
    {
        using var memory = new MemoryStream(payload, writable: false);
        using var archive = new ZipArchive(memory, ZipArchiveMode.Read, leaveOpen: false);
        var entries = ValidateArchiveEntries(archive);
        var stagingRoot = Path.GetFullPath(staging).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (var pair in expected.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (!entries.TryGetValue(pair.Key, out var entry))
                throw new InvalidDataException($"Manifested payload member is missing: {pair.Key}");
            var target = Path.GetFullPath(Path.Combine(staging, pair.Key.Replace('/', Path.DirectorySeparatorChar)));
            if (!target.StartsWith(stagingRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Payload extraction path escapes staging: {pair.Key}");
            var parent = Path.GetDirectoryName(target) ?? throw new InvalidDataException("Payload target has no parent directory.");
            Directory.CreateDirectory(parent);
            EnsureDirectoryIsSafe(parent);
            using var input = entry.Open();
            using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024, FileOptions.WriteThrough);
            using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[1024 * 1024];
            long total = 0;
            try
            {
                while (true)
                {
                    var read = input.Read(buffer, 0, buffer.Length);
                    if (read == 0)
                        break;
                    total += read;
                    if (total > MaximumMemberBytes)
                        throw new InvalidDataException($"Payload member grew beyond its ceiling: {pair.Key}");
                    output.Write(buffer, 0, read);
                    hasher.AppendData(buffer, 0, read);
                }
                output.Flush(flushToDisk: true);
                var observed = Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
                if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(observed), Convert.FromHexString(pair.Value)))
                    throw new InvalidDataException($"Payload member hash mismatch: {pair.Key}");
            }
            finally
            {
                CryptographicOperations.ZeroMemory(buffer);
            }
        }
    }

    private static bool ValidateCache(string cache, string payloadHash, IReadOnlyDictionary<string, string> expected)
    {
        try
        {
            if (!Directory.Exists(cache))
                return false;
            EnsureDirectoryIsSafe(cache);
            var marker = Path.Combine(cache, ".payload-identity");
            if (!File.Exists(marker) || IsReparsePoint(marker))
                return false;
            if (!string.Equals(File.ReadAllText(marker, Encoding.UTF8).Trim(), payloadHash, StringComparison.Ordinal))
                return false;
            var actual = new HashSet<string>(PathComparer);
            foreach (var path in Directory.EnumerateFileSystemEntries(cache, "*", SearchOption.AllDirectories))
            {
                if (IsReparsePoint(path))
                    return false;
                if (Directory.Exists(path))
                    continue;
                var relative = NormalizeRelativePath(Path.GetRelativePath(cache, path).Replace(Path.DirectorySeparatorChar, '/'));
                if (PathComparer.Equals(relative, ".payload-identity"))
                    continue;
                if (!expected.TryGetValue(relative, out var expectedHash) || !actual.Add(relative))
                    return false;
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan);
                var observed = SHA256.HashData(stream);
                try
                {
                    if (!CryptographicOperations.FixedTimeEquals(observed, Convert.FromHexString(expectedHash)))
                        return false;
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(observed);
                }
            }
            return actual.SetEquals(expected.Keys);
        }
        catch
        {
            return false;
        }
    }

    private static string NormalizeRelativePath(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.StartsWith("/", StringComparison.Ordinal) || value.Contains('\\') || value.Contains(':'))
            throw new InvalidDataException($"Unsafe relative path: {value}");
        var components = value.Split('/');
        if (components.Any(component => component.Length == 0 || component is "." or ".." || component.EndsWith(' ') || component.EndsWith('.')))
            throw new InvalidDataException($"Unsafe relative path: {value}");
        if (components.Any(component => component.Any(character => char.IsControl(character) || "<>\"|?*".Contains(character))))
            throw new InvalidDataException($"Unsupported relative path: {value}");
        if (components.Any(component => WindowsReservedNames.Contains(component.Split('.', 2)[0])))
            throw new InvalidDataException($"Windows-reserved relative path: {value}");
        return string.Join('/', components);
    }

    private static void EnsureDirectoryIsSafe(string path)
    {
        var item = new DirectoryInfo(path);
        if (!item.Exists || item.Attributes.HasFlag(FileAttributes.ReparsePoint))
            throw new InvalidDataException($"Unsafe payload directory: {path}");
    }

    private static bool IsReparsePoint(string path) => File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint);

    private static void DeleteTreeNoFollow(string root)
    {
        if (!Directory.Exists(root))
            return;
        if (IsReparsePoint(root))
            throw new InvalidDataException($"Refusing to recursively delete a reparse-point directory: {root}");
        foreach (var entry in Directory.EnumerateFileSystemEntries(root))
        {
            var attributes = File.GetAttributes(entry);
            if (attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                if (attributes.HasFlag(FileAttributes.Directory))
                    Directory.Delete(entry, recursive: false);
                else
                    File.Delete(entry);
            }
            else if (attributes.HasFlag(FileAttributes.Directory))
            {
                DeleteTreeNoFollow(entry);
            }
            else
            {
                File.SetAttributes(entry, FileAttributes.Normal);
                File.Delete(entry);
            }
        }
        Directory.Delete(root, recursive: false);
    }
}
