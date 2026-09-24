namespace WinCare.Application.Profiles;

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Application.Commands;
using WinCare.Domain.Commands;

/// <summary>Intensity selected for a curated workspace profile.</summary>
public enum ProfileIntensityTier { Conservative = 1, Balanced = 2, Aggressive = 3 }

/// <summary>
/// Builds previews and applies explicitly approved profile steps through the command dispatcher.
/// Profiles only reference commands present in the core catalog.
/// </summary>
public sealed class SmartProfileController
{
    private readonly ICommandDispatcher _dispatcher;

    public SmartProfileController(ICommandDispatcher dispatcher) =>
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));

    public async Task<IReadOnlyList<CommandResult>> PreviewWorkspaceProfileAsync(
        string workspaceId,
        ProfileIntensityTier intensity,
        CancellationToken cancellationToken)
    {
        var planned = ResolveCommandsForProfile(workspaceId, intensity);
        if (planned.Count == 0) return Array.AsReadOnly(new[] { UnsupportedProfile(workspaceId) });
        List<CommandResult> results = new(planned.Count);
        foreach (var (commandId, parameters) in planned)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await _dispatcher.ExecuteAsync(
                CommandRequest.Preview(commandId, parameters),
                CommandExecutionOptions.Default,
                cancellationToken).ConfigureAwait(false));
        }

        return results.AsReadOnly();
    }

    /// <summary>
    /// Applies a profile only with dispatcher-issued, command-matched approvals. A failed step
    /// stops the remaining sequence; already completed steps are reported in the returned results.
    /// </summary>
    public async Task<IReadOnlyList<CommandResult>> ApplyWorkspaceProfileAsync(
        string workspaceId,
        ProfileIntensityTier intensity,
        IReadOnlyDictionary<string, ApprovedMutationPlan> approvals,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(approvals);
        var planned = ResolveCommandsForProfile(workspaceId, intensity);
        if (planned.Count == 0) return Array.AsReadOnly(new[] { UnsupportedProfile(workspaceId) });
        List<CommandResult> results = new(planned.Count);
        foreach (var (commandId, parameters) in planned)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ApprovedMutationPlan? approval = FindApproval(approvals, commandId);
            if (approval is null)
            {
                results.Add(new CommandResult(
                    commandId, Guid.NewGuid(), CommandResultStatus.Blocked, "profile.approval_required",
                    $"No reviewed approval was provided for '{commandId}'.", null,
                    DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, false));
                break;
            }

            CommandResult result = await _dispatcher.ExecuteAsync(
                CommandRequest.Execute(commandId, parameters, approval),
                new CommandExecutionOptions(ReviewApproved: true),
                cancellationToken).ConfigureAwait(false);
            results.Add(result);
            if (result.Status != CommandResultStatus.Succeeded) break;
        }

        return results.AsReadOnly();
    }

    private static ApprovedMutationPlan? FindApproval(
        IReadOnlyDictionary<string, ApprovedMutationPlan> approvals,
        string commandId)
    {
        foreach ((string key, ApprovedMutationPlan approval) in approvals)
        {
            if (string.Equals(key, commandId, StringComparison.OrdinalIgnoreCase)) return approval;
        }
        return null;
    }

    private static CommandResult UnsupportedProfile(string workspaceId)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return new CommandResult(
            string.IsNullOrWhiteSpace(workspaceId) ? "profile" : workspaceId,
            Guid.NewGuid(),
            CommandResultStatus.Blocked,
            "profile.unsupported",
            "This workspace and intensity combination has no reviewed command plan.",
            null,
            now,
            now,
            false);
    }

    public static IReadOnlyList<(string CommandId, JsonElement Parameters)> ResolveCommandsForProfile(
        string workspaceId,
        ProfileIntensityTier intensity)
    {
        if (!Enum.IsDefined(intensity)) return Array.Empty<(string, JsonElement)>();

        return workspaceId?.Trim().ToUpperInvariant() switch
        {
            "DISKDIET" => new[]
            {
                ("cleaner-disk-pressure", JsonSerializer.SerializeToElement(new
                {
                    OlderThanDays = intensity switch
                    {
                        ProfileIntensityTier.Conservative => 30,
                        ProfileIntensityTier.Balanced => 14,
                        ProfileIntensityTier.Aggressive => 7,
                        _ => throw new ArgumentOutOfRangeException(nameof(intensity))
                    }
                }))
            },
            _ => Array.Empty<(string, JsonElement)>()
        };
    }
}
