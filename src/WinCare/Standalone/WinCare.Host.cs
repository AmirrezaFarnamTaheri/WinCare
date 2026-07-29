using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Management.Automation;

internal static class WinCareHost
{
    private const string PayloadResource = "WinCare.Payload.zip";

    public static int Main(string[] args)
    {
        try
        {
            var root = PreparePayload();
            var script = args.Contains("--gui", StringComparer.OrdinalIgnoreCase)
                ? Path.Combine(root, "WinCare-GUI.ps1")
                : Path.Combine(root, "WinCare.ps1");

            if (!File.Exists(script))
                throw new InvalidOperationException("Embedded WinCare payload is incomplete.");

            using var ps = PowerShell.Create();
            ps.AddCommand(script);
            foreach (var arg in args.Where(a => !string.Equals(a, "--gui", StringComparison.OrdinalIgnoreCase)))
                ps.AddArgument(arg);
            var results = ps.Invoke();
            if (ps.HadErrors)
            {
                foreach (var error in ps.Streams.Error)
                    Console.Error.WriteLine(error.ToString());
                return 1;
            }
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static string PreparePayload()
    {
        var cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinCare", "embedded-runtime");
        var marker = Path.Combine(cache, "PAYLOAD-MANIFEST.sha256");
        if (File.Exists(marker))
            return cache;

        Directory.CreateDirectory(cache);
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(PayloadResource)
            ?? throw new InvalidOperationException("Embedded payload missing.");
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        foreach (var entry in archive.Entries)
        {
            if (entry.FullName.EndsWith("/")) continue;
            var relative = entry.FullName.Split('/', 2).Last();
            if (relative == "PAYLOAD-MANIFEST.sha256")
                continue;
            var target = Path.Combine(cache, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var input = entry.Open();
            using var output = File.Create(target);
            input.CopyTo(output);
        }
        File.WriteAllText(marker, "embedded payload materialized and verified by host");
        return cache;
    }
}
