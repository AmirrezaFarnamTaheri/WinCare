using System.Threading;
using System.Threading.Tasks;
using WinCare.CommandCatalog.Models;
using WinCare.Domain.Commands;

namespace WinCare.Application.Commands
{
    public interface ISubsystemCommandExecutor
    {
        string Subsystem { get; }
        bool CanHandle(string commandId);
        Task<CommandHandlerOutcome> ExecuteAsync(
            CommandDefinition definition, 
            CommandRequest request, 
            CancellationToken cancellationToken);
    }
}
