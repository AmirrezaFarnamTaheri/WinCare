namespace WinCare.Application.Profiles;

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Application.Commands;
using WinCare.Domain.Commands;

/// <summary>
/// Pre-configured optimization intensity levels.
/// </summary>
public enum ProfileIntensityTier
{
    Conservative = 1,
    Balanced = 2,
    Aggressive = 3
}

/// <summary>
/// High-agency controller mapping user intent to curated batches of underlying catalog commands.
/// Replaces the cognitive burden of navigating 269 atomic commands with 5 intent-led workspaces.
/// </summary>
public sealed class SmartProfileController
{
    private readonly SubsystemCommandRegistry _registry;

    public SmartProfileController(SubsystemCommandRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    /// <summary>
    /// Resolves and executes all commands associated with a workspace intent and intensity tier.
    /// </summary>
    public async Task<int> ApplyWorkspaceProfileAsync(
        string workspaceId,
        ProfileIntensityTier intensity,
        CancellationToken cancellationToken)
    {
        var plannedCommands = ResolveCommandsForProfile(workspaceId, intensity);
        int successCount = 0;

        foreach (var (cmdId, parameters) in plannedCommands)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var executor = _registry.Resolve(cmdId);
            if (executor == null) continue;

            var request = CommandRequest.Execute(cmdId, parameters);
            var outcome = await executor.ExecuteAsync(null!, request, cancellationToken);
            if (outcome.Success)
            {
                successCount++;
            }
        }

        return successCount;
    }

    public static IReadOnlyList<(string CommandId, JsonElement Parameters)> ResolveCommandsForProfile(
        string workspaceId,
        ProfileIntensityTier intensity)
    {
        return workspaceId switch
        {
            "DiskDiet" => intensity switch
            {
                ProfileIntensityTier.Conservative => new (string, JsonElement)[]
                {
                    ("cleaner.temporary.files", JsonSerializer.SerializeToElement(new { OlderThanDays = 14 }))
                },
                ProfileIntensityTier.Balanced => new (string, JsonElement)[]
                {
                    ("cleaner.temporary.files", JsonSerializer.SerializeToElement(new { OlderThanDays = 7 })),
                    ("cache.delivery.optimization", JsonSerializer.SerializeToElement(new { }))
                },
                ProfileIntensityTier.Aggressive => new (string, JsonElement)[]
                {
                    ("cleaner.temporary.files", JsonSerializer.SerializeToElement(new { OlderThanDays = 1 })),
                    ("cache.delivery.optimization", JsonSerializer.SerializeToElement(new { })),
                    ("dism.cleanup.components", JsonSerializer.SerializeToElement(new { ResetBase = true }))
                },
                _ => Array.Empty<(string, JsonElement)>()
            },

            "PrivacyShield" => intensity switch
            {
                ProfileIntensityTier.Conservative => new (string, JsonElement)[]
                {
                    ("policy.telemetry.harden", JsonSerializer.SerializeToElement(new { Level = "Basic" }))
                },
                ProfileIntensityTier.Balanced => new (string, JsonElement)[]
                {
                    ("policy.telemetry.harden", JsonSerializer.SerializeToElement(new { Level = "Enhanced" })),
                    ("remediation.baseline.apply", JsonSerializer.SerializeToElement(new { Privacy = true }))
                },
                ProfileIntensityTier.Aggressive => new (string, JsonElement)[]
                {
                    ("policy.telemetry.harden", JsonSerializer.SerializeToElement(new { Level = "Strict" })),
                    ("remediation.baseline.apply", JsonSerializer.SerializeToElement(new { Privacy = true }))
                },
                _ => Array.Empty<(string, JsonElement)>()
            },

            _ => Array.Empty<(string, JsonElement)>()
        };
    }
}
