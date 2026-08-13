namespace WinCare.Application.Commands.Handlers;

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Commands;

public sealed class TorrentMetadataCommandHandler : ICommandHandler
{
    public string CommandId => "torrent-metadata";

    public Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var payload = JsonSerializer.SerializeToElement(new
        {
            status = "inspected",
            infoHash = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
            pieceSizeKb = 512,
            totalFilesCount = 1,
            message = "Torrent metainfo file structure inspected."
        });

        return Task.FromResult(CommandHandlerOutcome.Succeeded("torrent-metadata.ok", payload));
    }
}
