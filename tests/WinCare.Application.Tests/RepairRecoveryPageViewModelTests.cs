using WinCare.App.ViewModels.Pages;

namespace WinCare.Application.Tests;

public sealed class RepairRecoveryPageViewModelTests
{
    [Fact]
    public void Portable_playbook_review_projects_steps_without_execution_authority()
    {
        var vm = new RepairRecoveryPageViewModel();
        vm.SelectSection(5);
        vm.PortablePlaybookJson = """
            {"schemaVersion":1,"name":"Storage review","steps":[{"commandId":"storage-report","parameters":{"RootPath":"C:\\Data"}}]}
            """;

        vm.ReviewPortablePlaybookCommand.Execute(null);

        Assert.True(vm.IsPlaybookSection);
        PageRow row = Assert.Single(vm.ImportedPlaybookSteps);
        Assert.Equal("storage-report", row.CommandId);
        Assert.Contains("Fresh preview required", row.Detail);
        Assert.Contains("must be opened, previewed, and approved", vm.PlaybookStatus);
    }

    [Fact]
    public void Invalid_portable_playbook_clears_prior_review_and_reports_error()
    {
        var vm = new RepairRecoveryPageViewModel { PortablePlaybookJson = "{}" };
        vm.ReviewPortablePlaybookCommand.Execute(null);
        Assert.Empty(vm.ImportedPlaybookSteps);
        Assert.Contains("schemaVersion", vm.PlaybookStatus);
    }
}
