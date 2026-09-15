using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinCare.Application.Commands;
using WinCare.Application.Tools;

namespace WinCare.App.ViewModels.Pages;

public sealed class RepairRecoveryPageViewModel : TabbedPageViewModel
{
    private string _portablePlaybookJson = string.Empty;
    private string _playbookStatus = "Paste a WinCare schema-v1 portable playbook to review its steps. Import never carries approval or execution authority.";

    public CareAreaSelection ToolSelection => SelectedIndex switch
    {
        0 => new("Repair & recovery", "Repair"),
        1 => new("Repair & recovery", "Restore"),
        2 => new("Repair & recovery", "Backup"),
        _ => new("Repair & recovery", "Reset & media"),
    };

    public bool IsPlaybookSection => SelectedIndex == 4;
    public ObservableCollection<PageRow> ImportedPlaybookSteps { get; } = [];
    public IRelayCommand ReviewPortablePlaybookCommand { get; }
    public string PortablePlaybookJson { get => _portablePlaybookJson; set => SetProperty(ref _portablePlaybookJson, value ?? string.Empty); }
    public string PlaybookStatus { get => _playbookStatus; private set => SetProperty(ref _playbookStatus, value); }
    public bool HasImportedPlaybook => ImportedPlaybookSteps.Count > 0;

    public RepairRecoveryPageViewModel() : base([
        new PageSection("Repair", "No repair tools are available in this section.", []),
        new PageSection("Restore", "No restore tools are available in this section.", []),
        new PageSection("Backup", "No backup tools are available in this section.", []),
        new PageSection("Reset & media", "No reset or recovery-media tools are available in this section.", []),
        new PageSection("Portable playbooks", "No portable playbook has been reviewed.", [])])
    {
        ReviewPortablePlaybookCommand = new RelayCommand(ReviewPortablePlaybook);
    }

    public override void SelectSection(int index)
    {
        base.SelectSection(index);
        OnPropertyChanged(nameof(IsPlaybookSection));
    }

    private void ReviewPortablePlaybook()
    {
        ImportedPlaybookSteps.Clear();
        try
        {
            ImportedPortablePlaybook playbook = PortablePlaybookExchange.Import(
                PortablePlaybookJson,
                WinCare.CommandCatalog.CommandCatalog.Load());
            var catalog = WinCare.CommandCatalog.CommandCatalog.Load().ToDictionary(command => command.Id, StringComparer.OrdinalIgnoreCase);
            foreach (PortablePlaybookStep step in playbook.Steps)
            {
                var command = catalog[step.CommandId];
                ImportedPlaybookSteps.Add(new PageRow(command.Title, command.Summary,
                    command.ReadOnly ? "Read-only" : "Change",
                    "Fresh preview required before execution")
                {
                    CommandId = command.Id,
                    CommandParameters = step.Parameters.Clone(),
                });
            }
            PlaybookStatus = $"Reviewed “{playbook.Name}”: {playbook.Steps.Count} steps. Each step must be opened, previewed, and approved through the current catalog and policy.";
        }
        catch (PortablePlaybookValidationException ex)
        {
            PlaybookStatus = ex.Message;
        }
        OnPropertyChanged(nameof(HasImportedPlaybook));
    }
}
