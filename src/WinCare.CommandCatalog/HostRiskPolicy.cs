using WinCare.CommandCatalog.Models;
using WinCare.Domain.Commands;

namespace WinCare.CommandCatalog;

/// <summary>
/// Host-owned admission floor applied to every command definition that third-party code
/// supplies, whether it arrives through an extension pack or a plugin manifest.
/// </summary>
/// <remarks>
/// <para>
/// The catalog may declare a command's own risk, but the host owns the admission floor. A
/// mutating command that third-party code declares as low risk must still be admitted through
/// the Moderate tier, which requires an explicit user confirmation before it runs.
/// </para>
/// <para>
/// The rule lives in the catalog layer so every consumer sees the same tier: the dispatcher
/// that enforces admission, and the catalog views that display it. Enforcing the floor in one
/// place only would let the displayed tier and the enforced tier disagree.
/// </para>
/// </remarks>
public static class HostRiskPolicy
{
    /// <summary>
    /// Returns the definition with the host-owned floor applied.
    /// </summary>
    /// <param name="command">The declared command definition.</param>
    /// <returns>
    /// The definition unchanged when it is read-only, already at or above the Moderate tier, or
    /// declares a tier the dispatcher must reject explicitly; otherwise a copy whose explicit
    /// risk tier is <see cref="RiskTier.Moderate" />.
    /// </returns>
    public static CommandDefinition ApplyFloor(CommandDefinition command)
    {
        ArgumentNullException.ThrowIfNull(command);

        // An unrecognized tier is preserved so the dispatcher rejects it explicitly. Rewriting it
        // into the Moderate tier would silently admit a command whose declared risk the host
        // could not read.
        //
        // Mutability is decided by the ReadOnly flag alone. A third-party definition that claims
        // a read-only risk tier while declaring readOnly=false is contradictory metadata, and
        // trusting the risk tier here would let that declaration derive the Safe tier and
        // mutate on one click with no confirmation. The flag is the authoritative answer, so the
        // floor applies and the contradiction can never buy a bypass.
        return !command.ReadOnly &&
            Enum.IsDefined(command.RiskTier) && command.RiskTier < RiskTier.Moderate
            ? command with { ExplicitRiskTier = RiskTier.Moderate }
            : command;
    }
}
