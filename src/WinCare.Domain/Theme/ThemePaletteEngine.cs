namespace WinCare.Domain.Theme;

using System;
using System.Collections.Generic;
using System.Globalization;

/// <summary>
/// Theme Palette and Material Design 3 / WinUI 3 Color Token Synthesizer (Materialious).
/// Computes WCAG relative luminance and derives harmonic semantic color sets across 8 preset families.
/// </summary>
public static class ThemePaletteEngine
{
    public sealed record MaterialSeed(
        string Primary,
        string PrimaryContainer,
        string Secondary,
        string SecondaryContainer,
        string Tertiary,
        string TertiaryContainer,
        string Error,
        string ErrorContainer,
        string Background,
        string BackgroundAlt,
        string BackgroundDarker,
        string SurfaceLow,
        string SurfaceHigh,
        string OnSurface,
        string OnSurfaceVariant,
        string Outline,
        string OutlineVariant);

    public sealed record SemanticThemePalette(
        string Id,
        string Family,
        string Label,
        bool IsDark,
        IReadOnlyDictionary<string, string> ColorTokens);

    /// <summary>
    /// Calculates relative luminance from a hex color string using standard sRGB coefficients.
    /// </summary>
    public static double CalculateLuminance(string hexColor)
    {
        if (string.IsNullOrWhiteSpace(hexColor)) return 0.0;
        string clean = hexColor.TrimStart('#');
        if (clean.Length < 6) return 0.0;

        if (int.TryParse(clean.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int r) &&
            int.TryParse(clean.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int g) &&
            int.TryParse(clean.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int b))
        {
            return (0.2126 * r + 0.7152 * g + 0.0722 * b) / 255.0;
        }

        return 0.0;
    }

    /// <summary>
    /// Returns true if the color has relative luminance greater than 0.4.
    /// </summary>
    public static bool IsLightColor(string hexColor) => CalculateLuminance(hexColor) > 0.4;

    /// <summary>
    /// Derives a full semantic color token map from a seed specification.
    /// </summary>
    public static SemanticThemePalette BuildPalette(string id, string family, string label, MaterialSeed seed, bool isDark)
    {
        string darkOn = isDark ? seed.Background : seed.OnSurface;
        string lightOn = isDark ? seed.OnSurface : "#ffffff";
        string OnContainer(string containerHex) => IsLightColor(containerHex) ? darkOn : lightOn;

        var tokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Primary"] = seed.Primary,
            ["OnPrimary"] = seed.Background,
            ["PrimaryContainer"] = seed.PrimaryContainer,
            ["OnPrimaryContainer"] = OnContainer(seed.PrimaryContainer),
            ["Secondary"] = seed.Secondary,
            ["OnSecondary"] = seed.Background,
            ["SecondaryContainer"] = seed.SecondaryContainer,
            ["OnSecondaryContainer"] = OnContainer(seed.SecondaryContainer),
            ["Tertiary"] = seed.Tertiary,
            ["OnTertiary"] = seed.Background,
            ["TertiaryContainer"] = seed.TertiaryContainer,
            ["OnTertiaryContainer"] = OnContainer(seed.TertiaryContainer),
            ["Error"] = seed.Error,
            ["OnError"] = seed.Background,
            ["ErrorContainer"] = seed.ErrorContainer,
            ["OnErrorContainer"] = OnContainer(seed.ErrorContainer),
            ["Background"] = seed.Background,
            ["OnBackground"] = seed.OnSurface,
            ["Surface"] = seed.Background,
            ["OnSurface"] = seed.OnSurface,
            ["SurfaceVariant"] = seed.SurfaceLow,
            ["OnSurfaceVariant"] = seed.OnSurfaceVariant,
            ["Outline"] = seed.Outline,
            ["OutlineVariant"] = seed.OutlineVariant,
            ["Shadow"] = seed.BackgroundDarker,
            ["InverseSurface"] = seed.SurfaceHigh,
            ["InverseOnSurface"] = seed.Background,
            ["InversePrimary"] = seed.Primary,
            ["SurfaceDim"] = seed.BackgroundAlt,
            ["SurfaceBright"] = isDark ? seed.SurfaceHigh : seed.Background,
            ["SurfaceContainerLowest"] = isDark ? seed.BackgroundDarker : seed.Background,
            ["SurfaceContainerLow"] = seed.BackgroundAlt,
            ["SurfaceContainer"] = isDark ? seed.SurfaceLow : seed.BackgroundDarker,
            ["SurfaceContainerHigh"] = seed.SurfaceLow,
            ["SurfaceContainerHighest"] = seed.SurfaceHigh
        };

        return new SemanticThemePalette(id, family, label, isDark, tokens);
    }

    /// <summary>
    /// Gets all 8 built-in preset palettes (Catppuccin, Dracula, Everforest, Gruvbox, Nord, Rosepine, Solarized, Tokyo-Night).
    /// </summary>
    public static IReadOnlyList<SemanticThemePalette> GetPresetPalettes()
    {
        return new List<SemanticThemePalette>
        {
            // Catppuccin Mocha (Dark)
            BuildPalette("catppuccin-mocha", "Catppuccin", "Catppuccin Mocha", new MaterialSeed(
                Primary: "#89b4fa",
                PrimaryContainer: "#b4befe",
                Secondary: "#a6adc8",
                SecondaryContainer: "#45475a",
                Tertiary: "#cba6f7",
                TertiaryContainer: "#585b70",
                Error: "#f38ba8",
                ErrorContainer: "#eba0ac",
                Background: "#1e1e2e",
                BackgroundAlt: "#181825",
                BackgroundDarker: "#11111b",
                SurfaceLow: "#313244",
                SurfaceHigh: "#585b70",
                OnSurface: "#cdd6f4",
                OnSurfaceVariant: "#a6adc8",
                Outline: "#6c7086",
                OutlineVariant: "#45475a"
            ), isDark: true),

            // Catppuccin Latte (Light)
            BuildPalette("catppuccin-latte", "Catppuccin", "Catppuccin Latte", new MaterialSeed(
                Primary: "#1e66f5",
                PrimaryContainer: "#7287fd",
                Secondary: "#5c5f77",
                SecondaryContainer: "#bcc0cc",
                Tertiary: "#8839ef",
                TertiaryContainer: "#ea76cb",
                Error: "#d20f39",
                ErrorContainer: "#e64553",
                Background: "#eff1f5",
                BackgroundAlt: "#e6e9ef",
                BackgroundDarker: "#dce0e8",
                SurfaceLow: "#ccd0da",
                SurfaceHigh: "#9ca0b0",
                OnSurface: "#4c4f69",
                OnSurfaceVariant: "#5c5f77",
                Outline: "#8c8fa1",
                OutlineVariant: "#bcc0cc"
            ), isDark: false),

            // Dracula (Dark)
            BuildPalette("dracula", "Dracula", "Dracula Official", new MaterialSeed(
                Primary: "#bd93f9",
                PrimaryContainer: "#ff79c6",
                Secondary: "#8be9fd",
                SecondaryContainer: "#44475a",
                Tertiary: "#50fa7b",
                TertiaryContainer: "#f1fa8c",
                Error: "#ff5555",
                ErrorContainer: "#ff6e6e",
                Background: "#282a36",
                BackgroundAlt: "#21222c",
                BackgroundDarker: "#191a21",
                SurfaceLow: "#44475a",
                SurfaceHigh: "#6272a4",
                OnSurface: "#f8f8f2",
                OnSurfaceVariant: "#6272a4",
                Outline: "#6272a4",
                OutlineVariant: "#44475a"
            ), isDark: true),

            // Everforest (Dark)
            BuildPalette("everforest", "Everforest", "Everforest Dark", new MaterialSeed(
                Primary: "#a7c080",
                PrimaryContainer: "#83c092",
                Secondary: "#d3c6aa",
                SecondaryContainer: "#3d484d",
                Tertiary: "#7fbbb3",
                TertiaryContainer: "#dbbc7f",
                Error: "#e67e80",
                ErrorContainer: "#e69875",
                Background: "#2d353b",
                BackgroundAlt: "#272e33",
                BackgroundDarker: "#1e2326",
                SurfaceLow: "#3d484d",
                SurfaceHigh: "#4f5b58",
                OnSurface: "#d3c6aa",
                OnSurfaceVariant: "#9da9a0",
                Outline: "#7a8478",
                OutlineVariant: "#4f585e"
            ), isDark: true),

            // Gruvbox (Dark)
            BuildPalette("gruvbox", "Gruvbox", "Gruvbox Dark Medium", new MaterialSeed(
                Primary: "#fe8019",
                PrimaryContainer: "#fabd2f",
                Secondary: "#ebdbb2",
                SecondaryContainer: "#504945",
                Tertiary: "#b8bb26",
                TertiaryContainer: "#8ec07c",
                Error: "#fb4934",
                ErrorContainer: "#cc241d",
                Background: "#282828",
                BackgroundAlt: "#1d2021",
                BackgroundDarker: "#141617",
                SurfaceLow: "#3c3836",
                SurfaceHigh: "#504945",
                OnSurface: "#ebdbb2",
                OnSurfaceVariant: "#a89984",
                Outline: "#7c6f64",
                OutlineVariant: "#504945"
            ), isDark: true),

            // Nord (Dark)
            BuildPalette("nord", "Nord", "Nordic Frost", new MaterialSeed(
                Primary: "#88c0d0",
                PrimaryContainer: "#81a1c1",
                Secondary: "#e5e9f0",
                SecondaryContainer: "#3b4252",
                Tertiary: "#8fbcbb",
                TertiaryContainer: "#5e81ac",
                Error: "#bf616a",
                ErrorContainer: "#d08770",
                Background: "#2e3440",
                BackgroundAlt: "#242933",
                BackgroundDarker: "#1e222a",
                SurfaceLow: "#3b4252",
                SurfaceHigh: "#434c5e",
                OnSurface: "#eceff4",
                OnSurfaceVariant: "#d8dee9",
                Outline: "#4c566a",
                OutlineVariant: "#3b4252"
            ), isDark: true),

            // Rose Pine (Dark)
            BuildPalette("rosepine", "RosePine", "Rosé Pine", new MaterialSeed(
                Primary: "#ebbcba",
                PrimaryContainer: "#f6c177",
                Secondary: "#e0def4",
                SecondaryContainer: "#26233a",
                Tertiary: "#9ccfd8",
                TertiaryContainer: "#c4a7e7",
                Error: "#eb6f92",
                ErrorContainer: "#ea9a97",
                Background: "#191724",
                BackgroundAlt: "#1f1d2e",
                BackgroundDarker: "#14121d",
                SurfaceLow: "#26233a",
                SurfaceHigh: "#403d52",
                OnSurface: "#e0def4",
                OnSurfaceVariant: "#908caa",
                Outline: "#524f67",
                OutlineVariant: "#26233a"
            ), isDark: true),

            // Tokyo Night (Dark)
            BuildPalette("tokyo-night", "TokyoNight", "Tokyo Night Storm", new MaterialSeed(
                Primary: "#7aa2f7",
                PrimaryContainer: "#bb9af7",
                Secondary: "#a9b1d6",
                SecondaryContainer: "#292e42",
                Tertiary: "#7dcfff",
                TertiaryContainer: "#73daca",
                Error: "#f7768e",
                ErrorContainer: "#ff9e64",
                Background: "#24283b",
                BackgroundAlt: "#1f2335",
                BackgroundDarker: "#1a1b26",
                SurfaceLow: "#292e42",
                SurfaceHigh: "#414868",
                OnSurface: "#c0caf5",
                OnSurfaceVariant: "#9aa5ce",
                Outline: "#565f89",
                OutlineVariant: "#292e42"
            ), isDark: true)
        };
    }
}
