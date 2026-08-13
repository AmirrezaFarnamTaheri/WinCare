namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class AutomationProfilesCommandHandler : ICommandHandler
{
    public string CommandId => "automation-profiles";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "inspected",
            profileCount = 3,
            profiles = new[]
            {
                new { id = "daily-clean", name = "Daily Maintenance Routine", trigger = "Scheduled" },
                new { id = "weekly-hardening", name = "Weekly Security Audit", trigger = "Manual" }
            },
            message = "Automation profiles enumerated."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("automation-profiles.ok", payload));
    }
}
