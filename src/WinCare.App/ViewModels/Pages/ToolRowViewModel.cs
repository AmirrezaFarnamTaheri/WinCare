using CommunityToolkit.Mvvm.ComponentModel;
using WinCare.CommandCatalog.Models;
using WinCare.Domain.Commands;

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
    public override string ToString() => Title;
    public string Summary => Definition.Summary;
    public string Area => Definition.Area;
    public string Section => Definition.Section;
    public string CategoryText => $"{Area} · {Section}";
    public string Risk => Definition.RiskTier switch
    {
        RiskTier.Safe => "Safe",
        RiskTier.Moderate => "Moderate",
        RiskTier.Destructive => "Destructive",
        _ => "Unknown",
    };
    public string AdministratorAccess => Definition.AdministratorAccess switch
    {
        WinCare.CommandCatalog.Models.AdministratorAccess.No => "No",
        WinCare.CommandCatalog.Models.AdministratorAccess.MayBeRequired => "May be required",
        WinCare.CommandCatalog.Models.AdministratorAccess.Required => "Required",
        _ => "Unknown",
    };
    public string AdministratorText => $"Administrator: {AdministratorAccess}";
    public string Restart => Definition.Restart switch
    {
        RestartExpectation.No => "No",
        RestartExpectation.MayBeRequired => "May be required",
        RestartExpectation.Required => "Required",
        _ => "Unknown",
    };
    public string RestartText => $"Restart: {Restart}";
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

    public string RiskPillLabel => Definition.ReadOnly ? "Read-only" : Risk;

    public string StatusPillLabel => MigrationState switch
    {
        "Behavior verified" => "Verified",
        "Implemented" => "Ready",
        _ => "Not ready",
    };

    public string StatusPillBackgroundResourceKey
    {
        get
        {
            if (MigrationState is not "Behavior verified" and not "Implemented")
                return "PillNotReadyBgBrush";
            if (Definition.ReadOnly)
                return "PillReadOnlyBgBrush";

            return Definition.RiskTier switch
            {
                RiskTier.Safe or RiskTier.Moderate => "PillElevatedBgBrush",
                RiskTier.Destructive => "PillMutatingBgBrush",
                _ => "PillMutatingBgBrush",
            };
        }
    }

    public string StatusPillForegroundResourceKey
    {
        get
        {
            if (MigrationState is not "Behavior verified" and not "Implemented")
                return "PillAltTextBrush";

            return Definition.RiskTier switch
            {
                RiskTier.Safe when !Definition.ReadOnly => "PillAltTextBrush",
                RiskTier.Moderate => "PillAltTextBrush",
                _ => "PillTextBrush",
            };
        }
    }

    /// <summary>
    /// Concise accessible name for the selectable row (title, area, and product-facing safety tier).
    /// </summary>
    public string ToolAccessibleName => Definition.ReadOnly
        ? $"{Title}, {Definition.Area}, {Risk} tier, read-only"
        : $"{Title}, {Definition.Area}, {Risk} tier";
}
