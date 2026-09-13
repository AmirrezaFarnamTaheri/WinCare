using System.Diagnostics;
using Microsoft.Win32;
using WinCare.Application.Commands;
using WinCare.Domain.Commands;
using WinCare.Infrastructure.Security;

namespace WinCare.Infrastructure.Commands;

/// <summary>
/// Windows 11 debloating, artificial intelligence telemetry disabling, DWM Multiplane Overlay (MPO) remediation,
/// and Shell usability optimizations.
/// </summary>
internal sealed partial class WindowsCommandExecutor
{
    /// <summary>
    /// Audits or applies Windows 11 AI component policy toggles (Recall, Click To Do, Notepad AI, Paint AI).
    /// </summary>
    private CommandHandlerOutcome ApplyAiTweak(CommandParameters p, bool disable)
    {
        string component = p.String("Component", "all").ToLowerInvariant();
        int dwordValue = disable ? 1 : 0;
        int allowRecall = disable ? 0 : 1;

        using var privilegeScope = new TokenPrivilegeScope(
            TokenPrivilegeScope.SeBackupPrivilege,
            TokenPrivilegeScope.SeRestorePrivilege);

        var modifiedKeys = new List<string>();

        if (component is "all" or "recall")
        {
            using RegistryKey winAiKey = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Windows\WindowsAI", writable: true);
            winAiKey.SetValue("DisableAIDataAnalysis", dwordValue, RegistryValueKind.DWord);
            winAiKey.SetValue("AllowRecallEnablement", allowRecall, RegistryValueKind.DWord);
            winAiKey.SetValue("TurnOffSavingSnapshots", dwordValue, RegistryValueKind.DWord);
            modifiedKeys.Add(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsAI");
        }

        if (component is "all" or "click-to-do")
        {
            using RegistryKey winAiKey = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Windows\WindowsAI", writable: true);
            winAiKey.SetValue("DisableClickToDo", dwordValue, RegistryValueKind.DWord);
            modifiedKeys.Add(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsAI:DisableClickToDo");
        }

        if (component is "all" or "notepad")
        {
            using RegistryKey notepadKey = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\WindowsNotepad", writable: true);
            notepadKey.SetValue("DisableAIFeatures", dwordValue, RegistryValueKind.DWord);
            modifiedKeys.Add(@"HKLM\SOFTWARE\Policies\WindowsNotepad");
        }

        if (component is "all" or "paint")
        {
            using RegistryKey paintKey = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Paint", writable: true);
            paintKey.SetValue("DisableCocreator", dwordValue, RegistryValueKind.DWord);
            paintKey.SetValue("DisableGenerativeFill", dwordValue, RegistryValueKind.DWord);
            paintKey.SetValue("DisableImageCreator", dwordValue, RegistryValueKind.DWord);
            paintKey.SetValue("DisableGenerativeErase", dwordValue, RegistryValueKind.DWord);
            paintKey.SetValue("DisableRemoveBackground", dwordValue, RegistryValueKind.DWord);
            modifiedKeys.Add(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Paint");
        }

        string action = disable ? "disabled" : "restored";
        return Success("tweak-ai", $"Windows AI components successfully {action} ({component}).", new
        {
            component,
            state = action,
            modifiedKeys
        });
    }

    /// <summary>
    /// Configures the Desktop Window Manager (DWM) Multiplane Overlay (MPO) setting.
    /// Setting OverlayTestMode to 5 disables MPO to eliminate screen flickering and black screens
    /// on dual-monitor RTX and Radeon systems.
    /// </summary>
    private CommandHandlerOutcome ApplyMpoFix(bool disableMpo)
    {
        using var privilegeScope = new TokenPrivilegeScope(TokenPrivilegeScope.SeBackupPrivilege, TokenPrivilegeScope.SeRestorePrivilege);
        using RegistryKey dwmKey = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows\Dwm", writable: true);

        if (disableMpo)
        {
            dwmKey.SetValue("OverlayTestMode", 5, RegistryValueKind.DWord);
        }
        else
        {
            dwmKey.DeleteValue("OverlayTestMode", throwOnMissingValue: false);
        }

        return Success("tweak-mpo", disableMpo
            ? "Multiplane Overlay (MPO) disabled to eliminate GPU display stutter and flicker."
            : "Multiplane Overlay (MPO) restored to default Windows behaviour.", new
        {
            overlayTestMode = disableMpo ? 5 : 0,
            restartRecommended = true
        });
    }

    /// <summary>
    /// Enables or disables modern shell productivity tweaks (End Task on taskbar right-click,
    /// Last Active Click window focusing, and detailed BSoD stop code display).
    /// </summary>
    private CommandHandlerOutcome ApplyShellTweak(CommandParameters p)
    {
        string tweak = p.RequiredString("Tweak").ToLowerInvariant();
        bool enable = p.Boolean("Enable", true);
        int val = enable ? 1 : 0;

        switch (tweak)
        {
            case "end-task":
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\TaskbarDeveloperSettings", writable: true))
                {
                    key.SetValue("TaskbarEndTask", val, RegistryValueKind.DWord);
                }
                break;

            case "last-active-click":
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", writable: true))
                {
                    key.SetValue("LastActiveClick", val, RegistryValueKind.DWord);
                }
                break;

            case "bsod-details":
                using (var priv = new TokenPrivilegeScope(TokenPrivilegeScope.SeBackupPrivilege, TokenPrivilegeScope.SeRestorePrivilege))
                using (RegistryKey key = Registry.LocalMachine.CreateSubKey(@"System\CurrentControlSet\Control\CrashControl", writable: true))
                {
                    key.SetValue("DisplayParameters", val, RegistryValueKind.DWord);
                }
                break;

            case "show-file-extensions":
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", writable: true))
                {
                    key.SetValue("HideFileExt", enable ? 0 : 1, RegistryValueKind.DWord);
                }
                break;

            case "show-hidden-files":
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", writable: true))
                {
                    key.SetValue("Hidden", enable ? 1 : 2, RegistryValueKind.DWord);
                }
                break;

            case "launch-to-this-pc":
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", writable: true))
                {
                    key.SetValue("LaunchTo", enable ? 1 : 2, RegistryValueKind.DWord);
                }
                break;

            case "hide-search-box":
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Search", writable: true))
                {
                    key.SetValue("SearchboxTaskbarMode", enable ? 0 : 1, RegistryValueKind.DWord);
                }
                break;

            case "prevent-auto-reboot":
                using (var priv = new TokenPrivilegeScope(TokenPrivilegeScope.SeBackupPrivilege, TokenPrivilegeScope.SeRestorePrivilege))
                using (RegistryKey key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", writable: true))
                {
                    key.SetValue("NoAutoRebootWithLoggedOnUsers", val, RegistryValueKind.DWord);
                }
                break;

            case "svchost-consolidation":
                using (var priv = new TokenPrivilegeScope(TokenPrivilegeScope.SeBackupPrivilege, TokenPrivilegeScope.SeRestorePrivilege))
                using (RegistryKey key = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control", writable: true))
                {
                    int thresholdKb = enable ? 0x04000000 : 380000;
                    key.SetValue("SvcHostSplitThresholdInKB", thresholdKb, RegistryValueKind.DWord);
                }
                break;

            case "win32-priority":
                using (var priv = new TokenPrivilegeScope(TokenPrivilegeScope.SeBackupPrivilege, TokenPrivilegeScope.SeRestorePrivilege))
                using (RegistryKey key = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\PriorityControl", writable: true))
                {
                    key.SetValue("Win32PrioritySeparation", enable ? 38 : 2, RegistryValueKind.DWord);
                }
                break;

            case "network-throttling":
                using (var priv = new TokenPrivilegeScope(TokenPrivilegeScope.SeBackupPrivilege, TokenPrivilegeScope.SeRestorePrivilege))
                using (RegistryKey key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", writable: true))
                {
                    int throttlingIndex = enable ? unchecked((int)0xFFFFFFFF) : 10;
                    int responsiveness = enable ? 10 : 20;
                    key.SetValue("NetworkThrottlingIndex", throttlingIndex, RegistryValueKind.DWord);
                    key.SetValue("SystemResponsiveness", responsiveness, RegistryValueKind.DWord);
                }
                break;

            case "multimedia-scheduler":
                using (var priv = new TokenPrivilegeScope(TokenPrivilegeScope.SeBackupPrivilege, TokenPrivilegeScope.SeRestorePrivilege))
                {
                    using (RegistryKey key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", writable: true))
                    {
                        key.SetValue("NoLazyMode", val, RegistryValueKind.DWord);
                        key.SetValue("AlwaysOn", val, RegistryValueKind.DWord);
                        key.SetValue("NetworkThrottlingIndex", enable ? unchecked((int)0xFFFFFFFF) : 10, RegistryValueKind.DWord);
                        key.SetValue("SystemResponsiveness", enable ? 10 : 20, RegistryValueKind.DWord);
                    }
                    using (RegistryKey tasksKey = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games", writable: true))
                    {
                        tasksKey.SetValue("Priority", enable ? 2 : 2, RegistryValueKind.DWord);
                        tasksKey.SetValue("Scheduling Category", enable ? "High" : "Medium", RegistryValueKind.String);
                        tasksKey.SetValue("SFIO Priority", enable ? "High" : "Normal", RegistryValueKind.String);
                        tasksKey.SetValue("GPU Priority", enable ? 8 : 8, RegistryValueKind.DWord);
                    }
                }
                break;

            case "background-apps":
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications", writable: true))
                {
                    key.SetValue("GlobalUserDisabled", enable ? 1 : 0, RegistryValueKind.DWord);
                }
                using (RegistryKey searchKey = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Search", writable: true))
                {
                    searchKey.SetValue("BackgroundAppGlobalToggle", enable ? 0 : 1, RegistryValueKind.DWord);
                }
                break;

            case "webdav-limit":
                using (var priv = new TokenPrivilegeScope(TokenPrivilegeScope.SeBackupPrivilege, TokenPrivilegeScope.SeRestorePrivilege))
                using (RegistryKey key = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Services\WebClient\Parameters", writable: true))
                {
                    int limit = enable ? unchecked((int)0xFFFFFFFF) : 50000000;
                    key.SetValue("FileSizeLimitInBytes", limit, RegistryValueKind.DWord);
                }
                break;

            case "keyboard-latency":
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Control Panel\Keyboard", writable: true))
                {
                    key.SetValue("KeyboardDelay", enable ? "0" : "1", RegistryValueKind.String);
                    key.SetValue("KeyboardSpeed", enable ? "31" : "31", RegistryValueKind.String);
                }
                break;

            case "power-throttling":
                using (var priv = new TokenPrivilegeScope(TokenPrivilegeScope.SeBackupPrivilege, TokenPrivilegeScope.SeRestorePrivilege))
                using (RegistryKey key = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\Power\PowerThrottling", writable: true))
                {
                    key.SetValue("PowerThrottlingOff", val, RegistryValueKind.DWord);
                }
                break;

            case "usb-power-drain":
                using (var priv = new TokenPrivilegeScope(TokenPrivilegeScope.SeBackupPrivilege, TokenPrivilegeScope.SeRestorePrivilege))
                using (RegistryKey key = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\USB\AutomaticSurpriseRemoval", writable: true))
                {
                    key.SetValue("AttemptRecoveryFromUsbPowerDrain", enable ? 0 : 1, RegistryValueKind.DWord);
                }
                break;

            case "copilot-key-remap":
                using (var priv = new TokenPrivilegeScope(TokenPrivilegeScope.SeBackupPrivilege, TokenPrivilegeScope.SeRestorePrivilege))
                using (RegistryKey key = Registry.LocalMachine.CreateSubKey(@"SYSTEM\CurrentControlSet\Control\Keyboard Layout", writable: true))
                {
                    if (enable)
                    {
                        byte[] scancodeMap =
                        [
                            0x00, 0x00, 0x00, 0x00,
                            0x00, 0x00, 0x00, 0x00,
                            0x02, 0x00, 0x00, 0x00,
                            0x5D, 0xE0, 0x6E, 0x00,
                            0x00, 0x00, 0x00, 0x00
                        ];
                        key.SetValue("Scancode Map", scancodeMap, RegistryValueKind.Binary);
                    }
                    else
                    {
                        key.DeleteValue("Scancode Map", throwOnMissingValue: false);
                    }
                }
                break;

            case "setup-hardware-bypass":
                using (var priv = new TokenPrivilegeScope(TokenPrivilegeScope.SeBackupPrivilege, TokenPrivilegeScope.SeRestorePrivilege))
                using (RegistryKey key = Registry.LocalMachine.CreateSubKey(@"SYSTEM\Setup\LabConfig", writable: true))
                {
                    if (enable)
                    {
                        key.SetValue("BypassTPMCheck", 1, RegistryValueKind.DWord);
                        key.SetValue("BypassSecureBootCheck", 1, RegistryValueKind.DWord);
                        key.SetValue("BypassRAMCheck", 1, RegistryValueKind.DWord);
                        key.SetValue("BypassCPUCheck", 1, RegistryValueKind.DWord);
                        key.SetValue("BypassStorageCheck", 1, RegistryValueKind.DWord);
                        key.SetValue("BypassDiskCheck", 1, RegistryValueKind.DWord);
                    }
                    else
                    {
                        key.DeleteValue("BypassTPMCheck", throwOnMissingValue: false);
                        key.DeleteValue("BypassSecureBootCheck", throwOnMissingValue: false);
                        key.DeleteValue("BypassRAMCheck", throwOnMissingValue: false);
                        key.DeleteValue("BypassCPUCheck", throwOnMissingValue: false);
                        key.DeleteValue("BypassStorageCheck", throwOnMissingValue: false);
                        key.DeleteValue("BypassDiskCheck", throwOnMissingValue: false);
                    }
                }
                break;

            case "oobe-network-bypass":
                using (var priv = new TokenPrivilegeScope(TokenPrivilegeScope.SeBackupPrivilege, TokenPrivilegeScope.SeRestorePrivilege))
                using (RegistryKey key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\OOBE", writable: true))
                {
                    if (enable)
                    {
                        key.SetValue("BypassNRO", 1, RegistryValueKind.DWord);
                    }
                    else
                    {
                        key.DeleteValue("BypassNRO", throwOnMissingValue: false);
                    }
                }
                break;

            default:
                throw new CommandParameterException("Tweak", $"Unknown shell tweak '{tweak}'. Valid options: 'end-task', 'last-active-click', 'bsod-details', 'show-file-extensions', 'show-hidden-files', 'launch-to-this-pc', 'hide-search-box', 'prevent-auto-reboot', 'svchost-consolidation', 'win32-priority', 'network-throttling', 'multimedia-scheduler', 'background-apps', 'webdav-limit', 'keyboard-latency', 'power-throttling', 'usb-power-drain', 'copilot-key-remap', 'setup-hardware-bypass', 'oobe-network-bypass'.");
        }

        if (tweak is "show-file-extensions" or "show-hidden-files" or "launch-to-this-pc" or "hide-search-box")
        {
            try { Native.WinCareCoreNative.WinCareCoreShellNotify(); } catch { }
        }

        return Success("tweak-shell", $"Shell tweak '{tweak}' successfully {(enable ? "enabled" : "disabled")}.", new
        {
            tweak,
            enabled = enable
        });
    }
}