using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace WinCare.TuiLauncher
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            try
            {
                string? root = FindWinCareRoot();
                if (root is null) { Console.Error.WriteLine("WinCare platform files were not found."); return 2; }
                string? pwsh = FindPwsh();
                if (pwsh is null) { Console.Error.WriteLine("PowerShell 7.2 or later is required."); return 3; }

                var psi = new ProcessStartInfo { FileName = pwsh, UseShellExecute = false, WorkingDirectory = root };
                psi.ArgumentList.Add("-NoLogo");
                psi.ArgumentList.Add("-NoProfile");
                psi.ArgumentList.Add("-File");
                psi.ArgumentList.Add(Path.Combine(root, "WinCare.ps1"));
                foreach (string argument in args ?? Array.Empty<string>()) psi.ArgumentList.Add(argument);

                using Process? process = Process.Start(psi);
                if (process is null) throw new InvalidOperationException("PowerShell did not start.");
                process.WaitForExit();
                return process.ExitCode;
            }
            catch (Exception ex) { Console.Error.WriteLine("WinCare TUI launcher error: " + ex); return 1; }
        }

        private static string? FindWinCareRoot()
        {
            string baseDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? AppContext.BaseDirectory;
            var candidates = new List<string>
            {
                baseDirectory,
                Directory.GetParent(baseDirectory)?.FullName ?? baseDirectory,
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "WinCare")
            };
            foreach (string candidate in candidates)
            {
                if (!IsRegularDirectory(candidate)) continue;
                if (IsRegularFile(Path.Combine(candidate, "WinCare.ps1")) &&
                    IsRegularFile(Path.Combine(candidate, "src", "WinCare", "WinCare.psd1")))
                    return Path.GetFullPath(candidate);
            }
            return null;
        }

        private static bool IsRegularDirectory(string path)
        {
            var item = new DirectoryInfo(path);
            return item.Exists && (item.Attributes & FileAttributes.ReparsePoint) == 0;
        }

        private static bool IsRegularFile(string path)
        {
            var item = new FileInfo(path);
            return item.Exists && (item.Attributes & FileAttributes.ReparsePoint) == 0;
        }

        private static string? FindPwsh()
        {
            string fixedPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PowerShell", "7", "pwsh.exe");
            if (!File.Exists(fixedPath)) return null;
            var item = new FileInfo(fixedPath);
            return (item.Attributes & FileAttributes.ReparsePoint) == 0 ? item.FullName : null;
        }
    }
}
