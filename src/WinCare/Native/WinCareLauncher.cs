using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace WinCare.Launcher
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                string? root = FindWinCareRoot();
                if (root is null)
                {
                    MessageBox.Show(
                        "WinCare-GUI.ps1 and src\\WinCare\\WinCare.psd1 were not found beside the launcher or in the per-user installation directory.",
                        "WinCare files not found",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    return 2;
                }

                string? pwsh = FindPwsh();
                if (pwsh is null)
                {
                    MessageBox.Show(
                        "PowerShell 7.2 or later is required. Install Microsoft.PowerShell from a trusted source and retry.",
                        "PowerShell required",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    return 3;
                }

                var psi = CreatePowerShellStartInfo(pwsh, root, Path.Combine(root, "WinCare-GUI.ps1"), args, sta: true);
                using Process? process = Process.Start(psi);
                if (process is null) throw new InvalidOperationException("PowerShell did not start.");
                process.WaitForExit();
                return process.ExitCode;
            }
            catch (Exception ex)
            {
                MessageBox.Show("WinCare launcher error:\n" + ex, "WinCare Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }

        private static ProcessStartInfo CreatePowerShellStartInfo(string pwsh, string root, string script, IEnumerable<string> arguments, bool sta)
        {
            var psi = new ProcessStartInfo
            {
                FileName = pwsh,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = root
            };
            psi.ArgumentList.Add("-NoLogo");
            psi.ArgumentList.Add("-NoProfile");
            if (sta) psi.ArgumentList.Add("-STA");
            psi.ArgumentList.Add("-File");
            psi.ArgumentList.Add(script);
            foreach (string argument in arguments) psi.ArgumentList.Add(argument);
            return psi;
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
                if (IsWinCareRoot(candidate)) return Path.GetFullPath(candidate);
            }
            return null;
        }

        private static bool IsWinCareRoot(string candidate)
        {
            if (!IsRegularDirectory(candidate)) return false;
            return IsRegularFile(Path.Combine(candidate, "WinCare-GUI.ps1")) &&
                IsRegularFile(Path.Combine(candidate, "src", "WinCare", "WinCare.psd1"));
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
