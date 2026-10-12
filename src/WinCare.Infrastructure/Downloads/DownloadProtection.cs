using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace WinCare.Infrastructure.Downloads;

/// <summary>Private transfer URLs and public queue projections have separate representations.</summary>
internal static class DownloadProtection
{
    internal static string DisplayUrl(Uri uri) => uri.GetComponents(UriComponents.SchemeAndServer, UriFormat.UriEscaped) + "/";

    internal static JsonElement ProtectRecords(JsonElement records)
    {
        var array = JsonNode.Parse(records.GetRawText())!.AsArray();
        foreach (JsonObject item in array.OfType<JsonObject>())
        {
            string? url = item["url"]?.GetValue<string>();
            bool terminal = item["state"]?.GetValue<string>() is "Completed" or "Cancelled";
            if (!terminal && item["protectedUrl"] is null && !string.IsNullOrEmpty(url))
                item["protectedUrl"] = Protect(url);
            item["url"] = Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) ? DisplayUrl(uri) : string.Empty;
            if (terminal) item.Remove("protectedUrl");
        }
        return JsonSerializer.SerializeToElement(array);
    }

    internal static JsonElement PublicRecords(JsonElement records)
    {
        JsonNode node = JsonNode.Parse(records.GetRawText())!;
        IEnumerable<JsonObject> items = node is JsonArray array ? array.OfType<JsonObject>() : [node.AsObject()];
        foreach (JsonObject item in items) item.Remove("protectedUrl");
        return JsonSerializer.SerializeToElement(node);
    }

    internal static Uri TransferUri(JsonElement job)
    {
        if (!job.TryGetProperty("protectedUrl", out JsonElement encrypted))
            throw new InvalidDataException("The private download link is unavailable. Add the download again.");
        string url = Unprotect(encrypted.GetString()!);
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) || uri.Scheme is not ("http" or "https") || uri.UserInfo.Length != 0)
            throw new InvalidDataException("The private download link is invalid. Add the download again.");
        return uri;
    }

    internal static string Protect(string value) => Convert.ToBase64String(Transform(Encoding.UTF8.GetBytes(value), true));
    internal static string Unprotect(string value)
    {
        byte[] bytes = Transform(Convert.FromBase64String(value), false);
        try { return Encoding.UTF8.GetString(bytes); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    private static byte[] Transform(byte[] bytes, bool protect)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Download links require Windows user-scoped DPAPI.");
        IntPtr input = Marshal.AllocHGlobal(bytes.Length);
        var output = new Blob();
        try
        {
            Marshal.Copy(bytes, 0, input, bytes.Length);
            var blob = new Blob { Length = bytes.Length, Data = input };
            // No LOCAL_MACHINE flag: only the Windows user's DPAPI context may decrypt.
            bool ok = protect
                ? CryptProtectData(ref blob, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output)
                : CryptUnprotectData(ref blob, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!ok) throw new CryptographicException("Windows could not protect or recover this download link. Add the download again under the original Windows account.");
            byte[] result = new byte[output.Length];
            Marshal.Copy(output.Data, result, 0, result.Length);
            return result;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
            for (int i = 0; i < bytes.Length; i++) Marshal.WriteByte(input, i, 0);
            Marshal.FreeHGlobal(input);
            if (output.Data != IntPtr.Zero)
            {
                for (int i = 0; i < output.Length; i++) Marshal.WriteByte(output.Data, i, 0);
                _ = LocalFree(output.Data);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Blob { public int Length; public IntPtr Data; }
    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref Blob input, string? description, IntPtr entropy, IntPtr reserved, IntPtr prompt, uint flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, uint flags, out Blob output);
    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}

/// <summary>Crash-released exclusive handle plus a durable destination reservation. Lock files are never unlinked.</summary>
internal sealed class DownloadDestinationLease : IDisposable
{
    private readonly FileStream _handle;
    private DownloadDestinationLease(FileStream handle) => _handle = handle;

    internal static DownloadDestinationLease? TryAcquire(string destination)
    {
        string path = Path.GetFullPath(destination);
        if (path.EndsWith(".wincare.download", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".wincare.lock", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Download destination uses a name reserved for transfer metadata.");
        if (path[Path.GetPathRoot(path)!.Length..].Contains(':') || Path.GetFileName(path).EndsWith('.') || Path.GetFileName(path).EndsWith(' '))
            throw new InvalidDataException("Download destination cannot contain streams or ambiguous names.");
        string stem = Path.GetFileName(path).Split('.')[0].TrimEnd(' ', '.').ToUpperInvariant();
        if (stem is "CON" or "PRN" or "AUX" or "NUL" ||
            (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) && stem[3] is >= '1' and <= '9'))
            throw new InvalidDataException("Download destination cannot be a Windows device name.");
        for (string? current = path; current is not null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Download destination cannot traverse a reparse point.");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string lockPath = path + ".wincare.lock";
        if (File.Exists(lockPath) && (File.GetAttributes(lockPath) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Download reservation cannot be a reparse point.");
        try { return new(new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)); }
        catch (IOException) { return null; }
    }

    internal bool Reserve(string owner)
    {
        _handle.Position = 0;
        if (_handle.Length > 4096) throw new InvalidDataException("Invalid download reservation.");
        using var reader = new StreamReader(_handle, Encoding.UTF8, false, 1024, leaveOpen: true);
        string previous = reader.ReadToEnd();
        if (previous.Length > 0) return previous == owner;
        byte[] bytes = Encoding.UTF8.GetBytes(owner);
        _handle.Position = 0;
        _handle.Write(bytes);
        _handle.Flush(flushToDisk: true);
        return true;
    }

    internal void ReleaseReservation() { _handle.SetLength(0); _handle.Flush(flushToDisk: true); }
    public void Dispose() => _handle.Dispose();
}

internal static class DownloadFileOrigin
{
    internal static void MarkInternet(string path)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows file origin metadata is required.");
        // Intentionally omit HostUrl/ReferrerUrl: these can contain signed credentials.
        const string zone = "[ZoneTransfer]\r\nZoneId=3\r\n";
        using (var stream = new FileStream(path + ":Zone.Identifier", FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.Write(Encoding.ASCII.GetBytes(zone));
            stream.Flush(flushToDisk: true);
        }
        if (File.ReadAllText(path + ":Zone.Identifier") != zone)
            throw new IOException("Windows Internet-origin metadata could not be verified. The file was not published.");
    }
}
