namespace WinCare.Application.Commands;

/// <summary>
/// Stable command identifiers, payload property names, and id prefixes that admission logic
/// depends on. Centralizing them keeps the dispatcher from embedding the payload shapes of
/// specific handlers, in the same way <c>NavigationCatalog</c> centralizes route keys.
/// </summary>
internal static class CommandIds
{
    /// <summary>Command that expands and applies a remediation preset.</summary>
    public const string Preset = "preset";

    /// <summary>Command that restores a previously applied remediation from its receipt.</summary>
    public const string RemediationRestore = "remediation-restore";

    /// <summary>Name of the preview-data property carrying an expanded plan digest.</summary>
    public const string ExecutionDigestPropertyName = "executionDigest";

    /// <summary>Name of the preset parameter carrying the preset identifier.</summary>
    public const string PresetIdPropertyName = "PresetId";
}
