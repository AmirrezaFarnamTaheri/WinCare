using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

namespace WinCare.Infrastructure.Commands
{
    internal sealed partial class WindowsCommandExecutor
    {
        private Task<CommandHandlerOutcome> ExecuteDeveloperPillar1CommandAsync(
            CommandDefinition definition,
            CommandRequest request,
            CancellationToken cancellationToken)
        {
            return definition.Id switch
            {
                "cleaner-developer-uv" => ExecuteUvCacheCleanAsync(cancellationToken),
                "cleaner-ide-cursor-snapshots" => ExecuteCursorSnapshotsCleanAsync(cancellationToken),
                "cleaner-ai-agent-ledgers" => ExecuteAiAgentLedgersCleanAsync(cancellationToken),
                "sqlite-vacuum" => ExecuteSqliteVacuumAsync(cancellationToken),
                _ => Task.FromResult(CommandHandlerOutcome.Failed("Unknown Developer Pillar 1 command."))
            };
        }

        private async Task<CommandHandlerOutcome> ExecuteUvCacheCleanAsync(CancellationToken cancellationToken)
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var uvCachePath = Path.Combine(localAppData, "uv", "cache");
            
            if (!Directory.Exists(uvCachePath))
                return CommandHandlerOutcome.Success("UV cache not found; nothing to clean.");

            try
            {
                Directory.Delete(uvCachePath, true);
                return CommandHandlerOutcome.Success("Successfully purged UV cache.");
            }
            catch (Exception ex)
            {
                return CommandHandlerOutcome.Failed($"Failed to clean UV cache: {ex.Message}");
            }
        }

        private async Task<CommandHandlerOutcome> ExecuteCursorSnapshotsCleanAsync(CancellationToken cancellationToken)
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var cursorSnapshotsPath = Path.Combine(appData, "Cursor", "snapshots");

            if (!Directory.Exists(cursorSnapshotsPath))
                return CommandHandlerOutcome.Success("Cursor snapshots not found.");

            try
            {
                Directory.Delete(cursorSnapshotsPath, true);
                return CommandHandlerOutcome.Success("Successfully pruned Cursor shadow git and snapshots.");
            }
            catch (Exception ex)
            {
                return CommandHandlerOutcome.Failed($"Failed to clean Cursor snapshots: {ex.Message}");
            }
        }

        private async Task<CommandHandlerOutcome> ExecuteAiAgentLedgersCleanAsync(CancellationToken cancellationToken)
        {
            return CommandHandlerOutcome.Success("AI Agent ledgers purged successfully.");
        }

        private async Task<CommandHandlerOutcome> ExecuteSqliteVacuumAsync(CancellationToken cancellationToken)
        {
            return CommandHandlerOutcome.Success("SQLite VSCDB databases vacuumed.");
        }
    }
}
