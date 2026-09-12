using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinCare.Application.Commands;

namespace WinCare.App.ViewModels.Pages;

public sealed class RepairRecoveryPageViewModel : TabbedPageViewModel
{
    private string _portablePlaybookJson = string.Empty;
    private string _playbookStatus = "Paste a WinCare schema-v1 portable playbook to review its steps. Import never carries approval or execution authority.";

    public string ToolSearchQuery => SelectedIndex switch
    {
        0 => "repair",
        1 => "restore",
        // Reversible changes and execution records live under reports.
        2 => "reports",
        3 => "export backup",
        4 => "recovery reset",
        _ => string.Empty,
    };

    public bool IsPlaybookSection => SelectedIndex == 5;
    public ObservableCollection<PageRow> ImportedPlaybookSteps { get; } = [];
    public IRelayCommand ReviewPortablePlaybookCommand { get; }
    public string PortablePlaybookJson { get => _portablePlaybookJson; set => SetProperty(ref _portablePlaybookJson, value ?? string.Empty); }
    public string PlaybookStatus { get => _playbookStatus; private set => SetProperty(ref _playbookStatus, value); }
    public bool HasImportedPlaybook => ImportedPlaybookSteps.Count > 0;

    public RepairRecoveryPageViewModel() : base([
        new PageSection("Repair", "No tools are available in this section.", []),
        new PageSection("Restore", "No tools are available in this section.", []),
        new PageSection("Change records", "No tools are available in this section.", []),
        new PageSection("Backup", "No tools are available in this section.", []),
        new PageSection("Reset & media", "No tools are available in this section.", []),
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
                    "Fresh preview required before execution") { CommandId = command.Id });
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
