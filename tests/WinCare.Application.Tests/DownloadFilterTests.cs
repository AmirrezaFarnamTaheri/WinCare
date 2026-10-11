using System.Text.Json;
using WinCare.App.ViewModels.Pages;
using WinCare.Application.Downloads;
using WinCare.Application.Commands;
using WinCare.Domain.Commands;
using WinCare.CommandCatalog.Models;

namespace WinCare.Application.Tests;

public sealed class DownloadFilterTests
{
    [Fact]
    public void Search_and_state_filters_intersect_without_discarding_queue_rows()
    {
        var dispatcher = new NoOpDispatcher();
        var vm = new DownloadsPageViewModel(new DownloadCenterService(dispatcher));
        using var payload = JsonDocument.Parse("""
        [
          {"id":"1","state":"Pending","destination":"alpha.zip","url":"https://cdn.example.com/alpha.zip"},
          {"id":"2","state":"Completed","destination":"report.pdf","url":"https://docs.example.org/report.pdf"},
          {"id":"3","state":"Failed","destination":"beta.zip","url":"https://downloads.example.org/beta.zip"}
        ]
        """);
        foreach (DownloadJobRow row in DownloadJobProjection.Project(payload.RootElement))
            vm.Jobs.Add(row);

        vm.SetSearch("ZIP");
        Assert.Equal(2, vm.VisibleJobs.Count);
        vm.SetFilter("Finished");
        Assert.Equal("beta.zip", Assert.Single(vm.VisibleJobs).Name);
        vm.SetSearch("docs.example.org");
        Assert.Equal("report.pdf", Assert.Single(vm.VisibleJobs).Name);
        vm.SetSearch("");
        Assert.Equal(2, vm.VisibleJobs.Count);
        Assert.Equal(3, vm.Jobs.Count);
    }

    private sealed class NoOpDispatcher : ICommandDispatcher
    {
        public Task<CommandResult> ExecuteAsync(CommandRequest request, CommandExecutionOptions options, CancellationToken token) =>
            throw new NotSupportedException("This test only exercises local projection.");
        public bool RegisterDynamicCommand(CommandDefinition definition, ICommandHandler handler) => false;
        public bool UnregisterDynamicCommand(string commandId) => false;
    }
}
