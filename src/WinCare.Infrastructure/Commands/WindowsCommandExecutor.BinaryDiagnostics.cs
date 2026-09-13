namespace WinCare.Infrastructure.Commands;

internal sealed partial class WindowsCommandExecutor
{
    internal static class BinaryDiagnosticsHelper
    {
        public sealed record PeSecurityReport(
            bool IsValidPe,
            string FilePath,
            string Architecture,
            bool Is64Bit,
            bool HasAslr,
            bool HasDep,
            bool HasControlFlowGuard,
            string Summary);

        public sealed record CrashDumpItem(
            string FileName,
            string DirectoryPath,
            string ProcessName,
            long FileSizeBytes,
            DateTime CreationTimeUtc,
            string FormattedSize);

        public sealed record CrashDumpScanReport(
            int TotalDumps,
            ulong TotalSizeBytes,
            string FormattedTotalSize,
            IReadOnlyList<CrashDumpItem> Dumps,
            string Summary);

        public static PeSecurityReport InspectPeHeaders(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                return new PeSecurityReport(
                    IsValidPe: false,
                    FilePath: filePath ?? string.Empty,
                    Architecture: "Unknown",
                    Is64Bit: false,
                    HasAslr: false,
                    HasDep: false,
                    HasControlFlowGuard: false,
                    Summary: "Target executable file not found.");
            }

            try
            {
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new BinaryReader(fs);

                if (fs.Length < 64)
                {
                    return new PeSecurityReport(false, filePath, "Unknown", false, false, false, false, "File too small for DOS header.");
                }

                // Check MZ signature
                ushort mz = reader.ReadUInt16();
                if (mz != 0x5A4D)
                {
                    return new PeSecurityReport(false, filePath, "Non-PE", false, false, false, false, "Invalid DOS header signature.");
                }

                fs.Seek(0x3C, SeekOrigin.Begin);
                int peOffset = reader.ReadInt32();

                if (peOffset <= 0 || peOffset + 24 > fs.Length)
                {
                    return new PeSecurityReport(false, filePath, "Non-PE", false, false, false, false, "Corrupted e_lfanew offset.");
                }

                fs.Seek(peOffset, SeekOrigin.Begin);
                uint peSignature = reader.ReadUInt32();
                if (peSignature != 0x00004550) // "PE\0\0"
                {
                    return new PeSecurityReport(false, filePath, "Non-PE", false, false, false, false, "Invalid PE signature.");
                }

                ushort machine = reader.ReadUInt16();
                ushort numberOfSections = reader.ReadUInt16();
                uint timeDateStamp = reader.ReadUInt32();
                uint pointerToSymbolTable = reader.ReadUInt32();
                uint numberOfSymbols = reader.ReadUInt32();
                ushort sizeOfOptionalHeader = reader.ReadUInt16();
                ushort characteristics = reader.ReadUInt16();

                string arch = machine switch
                {
                    0x014C => "x86 (32-bit)",
                    0x8664 => "x64 (AMD64)",
                    0xAA64 => "ARM64",
                    0x0200 => "IA64",
                    _ => $"Machine: 0x{machine:X4}"
                };

                bool is64Bit = machine == 0x8664 || machine == 0xAA64;
                bool aslr = false;
                bool dep = false;
                bool cfg = false;

                if (sizeOfOptionalHeader > 0)
                {
                    ushort magic = reader.ReadUInt16();
                    // Offset of DllCharacteristics from start of OptionalHeader:
                    // PE32 (magic 0x10b): offset 70
                    // PE32+ (magic 0x20b): offset 70
                    int dllCharOffset = peOffset + 24 + 70;
                    if (fs.Length >= dllCharOffset + 2)
                    {
                        fs.Seek(dllCharOffset, SeekOrigin.Begin);
                        ushort dllCharacteristics = reader.ReadUInt16();

                        aslr = (dllCharacteristics & 0x0040) != 0; // IMAGE_DLLCHARACTERISTICS_DYNAMIC_BASE
                        dep = (dllCharacteristics & 0x0100) != 0;  // IMAGE_DLLCHARACTERISTICS_NX_COMPAT
                        cfg = (dllCharacteristics & 0x4000) != 0;  // IMAGE_DLLCHARACTERISTICS_GUARD_CF
                    }
                }

                string summary = $"{arch} · ASLR: {(aslr ? "Yes" : "No")} · DEP: {(dep ? "Yes" : "No")} · CFG: {(cfg ? "Yes" : "No")}";
                return new PeSecurityReport(
                    IsValidPe: true,
                    FilePath: filePath,
                    Architecture: arch,
                    Is64Bit: is64Bit,
                    HasAslr: aslr,
                    HasDep: dep,
                    HasControlFlowGuard: cfg,
                    Summary: summary);
            }
            catch (Exception ex)
            {
                return new PeSecurityReport(false, filePath, "Error", false, false, false, false, $"PE parse failed: {ex.Message}");
            }
        }

        public static CrashDumpScanReport ScanCrashDumps()
        {
            var dumps = new List<CrashDumpItem>();
            ulong totalBytes = 0;

            string localAppCrash = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CrashDumps");

            string systemMinidump = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                "Minidump");

            ScanDumpDirectory(localAppCrash, dumps, ref totalBytes);
            ScanDumpDirectory(systemMinidump, dumps, ref totalBytes);

            string summary = $"{dumps.Count} crash dump files found ({FormatBytes(totalBytes)})";

            return new CrashDumpScanReport(
                TotalDumps: dumps.Count,
                TotalSizeBytes: totalBytes,
                FormattedTotalSize: FormatBytes(totalBytes),
                Dumps: dumps.AsReadOnly(),
                Summary: summary);
        }

        private static void ScanDumpDirectory(string dirPath, List<CrashDumpItem> results, ref ulong totalBytes)
        {
            if (!Directory.Exists(dirPath)) return;

            try
            {
                var dir = new DirectoryInfo(dirPath);
                foreach (var file in dir.EnumerateFiles("*.dmp", SearchOption.TopDirectoryOnly))
                {
                    totalBytes += (ulong)file.Length;
                    string procName = file.Name.Split('.')[0];

                    results.Add(new CrashDumpItem(
                        FileName: file.Name,
                        DirectoryPath: dirPath,
                        ProcessName: procName,
                        FileSizeBytes: file.Length,
                        CreationTimeUtc: file.CreationTimeUtc,
                        FormattedSize: FormatBytes((ulong)file.Length)));
                }
            }
            catch
            {
                // Inaccessible directory skipped
            }
        }

        private static string FormatBytes(ulong bytes)
        {
            if (bytes >= 1024UL * 1024 * 1024) return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
            if (bytes >= 1024UL * 1024) return $"{bytes / (1024.0 * 1024):F2} MB";
            if (bytes >= 1024UL) return $"{bytes / 1024.0:F2} KB";
            return $"{bytes} B";
        }
    }
}
