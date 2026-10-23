using WinCare.CommandCatalog;
using WinCare.CommandCatalog.Models;

namespace WinCare.Application.Plugins;

/// <summary>Host-owned admission floor shared by plugin catalogs and runtime registration.</summary>
internal static class PluginCommandPolicy
{
    /// <summary>
    /// Applies the host-owned admission floor to a plugin-supplied command definition.
    /// </summary>
    /// <remarks>
    /// The floor itself lives in <see cref="HostRiskPolicy" /> so extension packs and plugins
    /// share one implementation. Unknown tiers are preserved for explicit rejection by the
    /// dispatcher, not silent repair.
    /// </remarks>
    internal static CommandDefinition Normalize(CommandDefinition command) => HostRiskPolicy.ApplyFloor(command);
}
