using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using WinCare.Application.Commands;
using WinCare.Domain.Commands;

namespace WinCare.Infrastructure.Commands;

/// <summary>
/// Display, GPU, and audio driver residual cleanup and service diagnostic routines.
/// </summary>
internal sealed partial class WindowsCommandExecutor
{
    private static readonly string[] NvidiaServices =
    [
        "nvsvc",
        "NVHDA",
        "nvpciflt",
        "nvwmi",
        "Stereo Service",
        "nvkflt",
        "nvlddmkm",
        "nv",
        "NVDisplay.ContainerLocalSystem",
        "nvpcf",
        "NvTelemetryContainer"
    ];

    private static readonly string[] AmdServices =
    [
        "AMD Crash Defender Service",
        "AMD External Events Utility",
        "amdfendr",
        "amdfendrmgr",
        "AMDXE",
        "Ati HotKey Poller",
        "ATI Smart",
        "ati2mtag",
        "AMD FUEL Service",
        "amdkmdag",
        "atikmdag",
        "atikmpag"
    ];

    private static readonly string[] IntelServices =
    [
        "igfxCUIService2.0.0.0",
        "cphs",
        "cplspcon",
        "Intel(R) Content Protection HECI Service",
        "igfx",
        "IntcDAud"
    ];

    private static readonly string[] RealtekAudioServices =
    [
        "RtkAudioService",
        "Realtek Audio Universal Service",
        "RtkAudioUniversalService",
        "IntcAzAudAddService",
        "ICEsoundService"
    ];


    internal sealed record DriverCacheCleanupResult(
        string Vendor,
        long BytesFreed,
        int FilesDeleted,
        int FilesSkipped,
        IReadOnlyList<string> CleanedPaths);

    /// <summary>
    /// Audits installed and active display and audio driver services against the known catalog.
    /// </summary>
    private CommandHandlerOutcome DriverServiceAudit()
    {
        var services = new List<object>();

        void AuditVendorServices(string vendor, string[] serviceNames)
        {
            foreach (string name in serviceNames)
            {
                using RegistryKey? key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{name}", writable: false);
                if (key is not null)
                {
                    string? imagePath = key.GetValue("ImagePath") as string;
                    int? startType = key.GetValue("Start") as int?;
                    string? displayName = key.GetValue("DisplayName") as string;

                    services.Add(new
                    {
                        vendor,
                        serviceName = name,
                        displayName = displayName ?? name,
                        imagePath,
                        startType,
                        installed = true
                    });
                }
            }
        }

        AuditVendorServices("NVIDIA", NvidiaServices);
        AuditVendorServices("AMD", AmdServices);
        AuditVendorServices("Intel", IntelServices);
        AuditVendorServices("Realtek", RealtekAudioServices);

        return Success("driver-service-audit", $"Driver service audit identified {services.Count} installed driver services.", new
        {
            totalObserved = services.Count,
            services
        });
    }

    /// <summary>
    /// Safely cleans GPU shader cache directories for NVIDIA, AMD, and Intel to resolve
    /// micro-stuttering, shader corruption, and reclaim disk space.
    /// </summary>
    private CommandHandlerOutcome DriverCacheCleanup(CommandParameters p)
    {
        string vendorFilter = p.String("Vendor", "all").ToLowerInvariant();
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

        var targetDirs = new List<(string Vendor, string Path)>();

        if (vendorFilter is "all" or "nvidia")
        {
            targetDirs.Add(("NVIDIA", Path.Combine(localAppData, "NVIDIA", "DXCache")));
            targetDirs.Add(("NVIDIA", Path.Combine(localAppData, "NVIDIA", "GLCache")));
            targetDirs.Add(("NVIDIA", Path.Combine(programData, "NVIDIA Corporation", "NV_Cache")));
        }

        if (vendorFilter is "all" or "amd")
        {
            targetDirs.Add(("AMD", Path.Combine(localAppData, "AMD", "DxCache")));
            targetDirs.Add(("AMD", Path.Combine(localAppData, "AMD", "GLCache")));
        }

        if (vendorFilter is "all" or "intel")
        {
            targetDirs.Add(("Intel", Path.Combine(localAppData, "Intel", "ShaderCache")));
            targetDirs.Add(("Intel", Path.Combine(localAppData, "Intel", "ComputeShaderCache")));
        }

        long totalBytesFreed = 0;
        int totalFilesDeleted = 0;
        int totalFilesSkipped = 0;
        var cleanedPaths = new List<string>();

        foreach (var (vendor, dirPath) in targetDirs)
        {
            if (!Directory.Exists(dirPath))
            {
                continue;
            }

            try
            {
                foreach (string filePath in Directory.EnumerateFiles(dirPath, "*", SafeRecursiveEnumeration))
                {
                    try
                    {
                        var fileInfo = new FileInfo(filePath);
                        long size = fileInfo.Length;
                        fileInfo.Delete();
                        totalBytesFreed += size;
                        totalFilesDeleted++;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        totalFilesSkipped++;
                    }
                }

                cleanedPaths.Add(dirPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                totalFilesSkipped++;
            }
        }

        return Success("driver-cache-cleanup", $"Driver cache cleanup completed: freed {totalBytesFreed:N0} bytes across {totalFilesDeleted} files ({totalFilesSkipped} in-use files retained).", new
        {
            vendorFilter,
            bytesFreed = totalBytesFreed,
            filesDeleted = totalFilesDeleted,
            filesSkipped = totalFilesSkipped,
            cleanedDirectories = cleanedPaths
        });
    }
}