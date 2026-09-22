namespace WinCare.Application.Storage;

/// <summary>
/// Resolves the per-user WinCare data root. A documentation capture session overrides it with an
/// isolated temporary directory, so the rendered images can never show a specific machine's
/// activity journal, saved preferences, or installed-extension state. The CI smoke test and the
/// capture run would otherwise share <c>%LOCALAPPDATA%\WinCare</c> and the capture would render
/// the smoke run's leftover results.
/// </summary>
public static class AppDataRoot
{
    /// <summary>Environment variable a caller can set to redirect the data root for one run.</summary>
    public const string OverrideVariable = "WINCARE_DATA_ROOT";

    /// <summary>
    /// In-process override, set by capture startup before any service reads the root. Preferred
    /// over the environment variable because it cannot leak into a child process.
    /// </summary>
    public static string? Override { get; set; }

    /// <summary>The effective data root for this process.</summary>
    public static string Current
    {
        get
        {
            string? processOverride = Override;
            if (!string.IsNullOrWhiteSpace(processOverride))
            {
                return processOverride;
            }

            try
            {
                string? envOverride = Environment.GetEnvironmentVariable(OverrideVariable);
                if (!string.IsNullOrWhiteSpace(envOverride))
                {
                    return envOverride;
                }
            }
            catch
            {
                // Environment access must not be a startup failure; fall back to the default.
            }

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WinCare");
        }
    }

    /// <summary>The activity journal path under the effective root.</summary>
    public static string ActivityJournalPath => Path.Combine(Current, "activity.json");

    /// <summary>The saved-settings path under the effective root.</summary>
    public static string PreferencesPath => Path.Combine(Current, "settings.json");
}
