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

    public string RiskPillLabel => Risk switch
    {
        "Read-only" => "[ READ-ONLY ]",
        "Low" => "[ LOW      ]",
        "Moderate" => "[ MODERATE ]",
        "High" => "[ HIGH RISK]",
        "Critical" => "[ CRITICAL ]",
        _ => $"[ {Risk?.ToUpperInvariant()?.PadRight(8) ?? "UNKNOWN ",8} ]",
    };

    public string StatusPillLabel => MigrationState switch
    {
        "Behavior verified" => "[ VERIFIED ]",
        "Implemented" => "[ READY    ]",
        _ => "[ NOT READY]",
    };

    public Microsoft.UI.Xaml.Media.Brush StatusPillBackgroundBrush
    {
        get
        {
            string key = Risk switch
            {
                "Mutating" or "High Risk" or "High" or "Critical" => "PillMutatingBgBrush",
                "Elevated" or "Moderate" or "Low" => "PillElevatedBgBrush",
                _ => "PillReadOnlyBgBrush",
            };
            return ResolveBrush(key);
        }
    }

    public Microsoft.UI.Xaml.Media.Brush StatusPillForegroundBrush
    {
        get
        {
            string key = Risk switch
            {
                "Elevated" or "Moderate" or "Low" => "PillAltTextBrush",
                _ => "PillTextBrush",
            };
            return ResolveBrush(key);
        }
    }

    private static Microsoft.UI.Xaml.Media.Brush ResolveBrush(string resourceKey)
    {
        if (Microsoft.UI.Xaml.Application.Current?.Resources?.TryGetValue(resourceKey, out var resource) == true && resource is Microsoft.UI.Xaml.Media.Brush brush)
        {
            return brush;
        }
        return new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
    }
}


