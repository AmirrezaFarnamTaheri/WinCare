using WinCare.CommandCatalog.Models;
using WinCare.Domain.Commands;

namespace WinCare.Application.Plugins;

/// <summary>Host-owned admission floor shared by plugin catalogs and runtime registration.</summary>
internal static class PluginCommandPolicy
{
    internal static CommandDefinition Normalize(CommandDefinition command)
    {
        // Preserve unknown tiers for explicit rejection by the dispatcher, not silent repair.
        return !command.ReadOnly && command.Risk != CommandRisk.ReadOnly &&
            Enum.IsDefined(command.RiskTier) && command.RiskTier < RiskTier.Moderate
            ? command with { ExplicitRiskTier = RiskTier.Moderate }
            : command;
    }
}
