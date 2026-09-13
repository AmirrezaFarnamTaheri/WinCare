using Microsoft.Win32;

namespace WinCare.Infrastructure.Commands;

internal sealed partial class WindowsCommandExecutor
{
    internal static class ShellExtensibilityHelper
    {
        public sealed record ContextMenuHandlerInfo(
            string Scope,
            string HandlerName,
            string Clsid,
            string ServerDllPath,
            bool IsThirdParty);

        public sealed record ContextMenuAuditReport(
            int TotalHandlers,
            int ThirdPartyHandlers,
            IReadOnlyList<ContextMenuHandlerInfo> Handlers,
            string Summary);

        public sealed record ClassicContextMenuPolicy(
            bool IsClassicEnabled,
            string PolicyKeyPath,
            string Summary);

        public static ContextMenuAuditReport AuditContextMenuHandlers()
        {
            var handlers = new List<ContextMenuHandlerInfo>();

            AuditScope(@"*\shellex\ContextMenuHandlers", "File Context Menu", handlers);
            AuditScope(@"Directory\shellex\ContextMenuHandlers", "Folder Context Menu", handlers);
            AuditScope(@"Directory\Background\shellex\ContextMenuHandlers", "Desktop / Folder Background", handlers);

            int thirdPartyCount = handlers.Count(h => h.IsThirdParty);
            string summary = $"{handlers.Count} shell context menu handlers found · {thirdPartyCount} third-party extensions";

            return new ContextMenuAuditReport(handlers.Count, thirdPartyCount, handlers.AsReadOnly(), summary);
        }

        public static ClassicContextMenuPolicy QueryWindows11ClassicContextMenuState()
        {
            const string clsidKey = @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32";
            using var key = Registry.CurrentUser.OpenSubKey(clsidKey);

            bool isClassic = false;
            if (key != null)
            {
                var val = key.GetValue(string.Empty) as string;
                isClassic = val == string.Empty || val == null;
            }

            string summary = isClassic
                ? "Windows 11 Classic (Full) Context Menu is ENABLED via CLSID override."
                : "Windows 11 Modern (Compact / Show More Options) Context Menu is ACTIVE.";

            return new ClassicContextMenuPolicy(isClassic, @"HKCU\" + clsidKey, summary);
        }

        private static void AuditScope(string subKeyPath, string scopeName, List<ContextMenuHandlerInfo> results)
        {
            try
            {
                using var key = Registry.ClassesRoot.OpenSubKey(subKeyPath);
                if (key == null) return;

                foreach (var name in key.GetSubKeyNames())
                {
                    using var sub = key.OpenSubKey(name);
                    string clsid = sub?.GetValue(string.Empty) as string ?? name;
                    string serverPath = ResolveServerPath(clsid);
                    bool isThirdParty = IsThirdPartyServer(serverPath, name);

                    results.Add(new ContextMenuHandlerInfo(
                        Scope: scopeName,
                        HandlerName: name,
                        Clsid: clsid,
                        ServerDllPath: serverPath,
                        IsThirdParty: isThirdParty));
                }
            }
            catch
            {
                // Fail-safe graceful degradation
            }
        }

        private static string ResolveServerPath(string clsid)
        {
            if (string.IsNullOrWhiteSpace(clsid)) return string.Empty;
            if (!clsid.StartsWith("{", StringComparison.Ordinal)) return string.Empty;

            try
            {
                using var inprocKey = Registry.ClassesRoot.OpenSubKey($@"CLSID\{clsid}\InprocServer32");
                return inprocKey?.GetValue(string.Empty) as string ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static bool IsThirdPartyServer(string serverPath, string handlerName)
        {
            if (string.IsNullOrWhiteSpace(serverPath))
            {
                return !handlerName.StartsWith("EPP", StringComparison.OrdinalIgnoreCase) &&
                       !handlerName.StartsWith("{", StringComparison.Ordinal);
            }

            string windir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            return !serverPath.StartsWith(windir, StringComparison.OrdinalIgnoreCase);
        }
    }
}
