using CommunityToolkit.Mvvm.ComponentModel;
using WinCare.CommandCatalog.Models;

namespace WinCare.App.ViewModels.Pages;

public sealed class ToolRowViewModel : ObservableObject
{
    private bool _isCompact;

    public ToolRowViewModel(CommandDefinition definition)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
    }

    public CommandDefinition Definition { get; }
    public string Id => Definition.Id;
    public string Title => Definition.Title;
    public string Summary => Definition.Summary;
    public string Area => Definition.Area;
    public string Section => Definition.Section;
    public string Risk => Definition.Risk switch
    {
        CommandRisk.ReadOnly => "Read-only",
        CommandRisk.Low => "Low",
        CommandRisk.Moderate => "Moderate",
        CommandRisk.High => "High",
        CommandRisk.Critical => "Critical",
        _ => Definition.Risk.ToString(),
    };
    public string AdministratorAccess => Definition.AdministratorAccess switch
    {
        WinCare.CommandCatalog.Models.AdministratorAccess.No => "No",
        WinCare.CommandCatalog.Models.AdministratorAccess.MayBeRequired => "May be required",
        WinCare.CommandCatalog.Models.AdministratorAccess.Required => "Required",
        _ => "Unknown",
    };
    public string Restart => Definition.Restart switch
    {
        RestartExpectation.No => "No",
        RestartExpectation.MayBeRequired => "May be required",
        RestartExpectation.Required => "Required",
        _ => "Unknown",
    };
    public string MigrationState => Definition.MigrationStatus switch
    {
        MigrationStatus.Cataloged => "Cataloged",
        MigrationStatus.ContractVerified => "Contract verified",
        MigrationStatus.Implemented => "Implemented",
        MigrationStatus.BehaviorVerified => "Behavior verified",
        _ => "Unknown",
    };

    public bool IsCompact
    {
        get => _isCompact;
        set => SetProperty(ref _isCompact, value);
    }
}
