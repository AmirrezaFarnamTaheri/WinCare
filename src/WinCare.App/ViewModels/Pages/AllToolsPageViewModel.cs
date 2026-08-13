using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Threading.Tasks;
using WinCare.Application.Commands;
using WinCare.Application.Tools;
using WinCare.App.Services;
using WinCare.CommandCatalog.Models;


namespace WinCare.App.ViewModels.Pages;

public sealed class AllToolsPageViewModel : ObservableObject
{
    private readonly ToolCatalogService _catalog;
    private readonly HashSet<string> _favoriteIds = new(StringComparer.Ordinal);
    private readonly List<string> _recentIds = [];
    private string _searchText = string.Empty;
    private AreaFilterOption _selectedAreaOption;
    private RiskFilterOption _selectedRiskOption;
    private bool _readOnlyOnly;
    private string _selectedTab = "Commands";
    private ToolRowViewModel? _selectedTool;
    private bool _isDetailsOpen;
    private bool _isCompactLayout;

    public AllToolsPageViewModel()
        : this(new ToolCatalogService(), AppRuntime.Current.Dispatcher)
    {
    }

    public AllToolsPageViewModel(ToolCatalogService catalog)
        : this(catalog, AppRuntime.Current.Dispatcher)
    {
    }

    public AllToolsPageViewModel(ToolCatalogService catalog, CommandDispatcher dispatcher)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        AreaOptions = [
            new AreaFilterOption("All areas", null),
            .. _catalog.All.Select(command => command.Area)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(area => area, StringComparer.OrdinalIgnoreCase)
                .Select(area => new AreaFilterOption(area, area))
        ];
        RiskOptions = [
            new RiskFilterOption("All risk levels", null),
            new RiskFilterOption("Read-only", CommandRisk.ReadOnly),
            new RiskFilterOption("Low", CommandRisk.Low),
            new RiskFilterOption("Moderate", CommandRisk.Moderate),
            new RiskFilterOption("High", CommandRisk.High),
            new RiskFilterOption("Critical", CommandRisk.Critical)
        ];
        _selectedAreaOption = AreaOptions[0];
        _selectedRiskOption = RiskOptions[0];
        Execution = new ToolExecutionViewModel(dispatcher, RecordRecent);
        Refresh();
    }

    public ObservableCollection<ToolRowViewModel> VisibleTools { get; } = new();
    public IReadOnlyList<AreaFilterOption> AreaOptions { get; }
    public IReadOnlyList<RiskFilterOption> RiskOptions { get; }
    public ToolExecutionViewModel Execution { get; }

    private CancellationTokenSource? _searchCts;

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value ?? string.Empty))
            {
                DebounceSearch();
            }
        }
    }

    private void DebounceSearch()
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = new CancellationTokenSource();
        _ = DebounceSearchAsync(_searchCts.Token);
    }

    private async Task DebounceSearchAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(250, token);
            Refresh();
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer keystroke — expected.
        }
        catch (Exception ex)
        {
            // Task is deliberately fire-and-forget; observe every non-cancellation fault here.
            System.Diagnostics.Debug.WriteLine($"[AllToolsPageViewModel] DebounceSearch fault: {ex.Message}");
        }
    }


    public AreaFilterOption SelectedAreaOption
    {
        get => _selectedAreaOption;
        set
        {
            if (value is not null && SetProperty(ref _selectedAreaOption, value))
            {
                Refresh();
            }
        }
    }

    public RiskFilterOption SelectedRiskOption
    {
        get => _selectedRiskOption;
        set
        {
            if (value is not null && SetProperty(ref _selectedRiskOption, value))
            {
                Refresh();
            }
        }
    }

    public bool ReadOnlyOnly
    {
        get => _readOnlyOnly;
        set
        {
            if (SetProperty(ref _readOnlyOnly, value))
            {
                Refresh();
            }
        }
    }

    public ToolRowViewModel? SelectedTool
    {
        get => _selectedTool;
        set
        {
            if (SetProperty(ref _selectedTool, value))
            {
                IsDetailsOpen = value is not null;
                Execution.SelectTool(value);
                NotifySelectedToolChanged();
            }
        }
    }

    public bool IsDetailsOpen
    {
        get => _isDetailsOpen;
        set => SetProperty(ref _isDetailsOpen, value);
    }

    public bool IsCompactLayout
    {
        get => _isCompactLayout;
        private set => SetProperty(ref _isCompactLayout, value);
    }

    public string ResultCountText => VisibleTools.Count == 1 ? "1 tool" : $"{VisibleTools.Count} tools";

    public bool IsEmpty => VisibleTools.Count == 0;

    public string EmptyMessage => _selectedTab switch
    {
        "Favorites" => "No favorites yet. Select a tool and add it to Favorites.",
        "Recent" => "Tools you run will appear here.",
        "Presets" => "No preset catalog matches the current filters.",
        _ => "No tools match the current search and filters.",
    };

    public string SelectedToolTitle => SelectedTool?.Title ?? "Select a tool";

    public string SelectedToolSummary => SelectedTool?.Summary ??
        "Choose a row to review requirements, risk, and migration status.";

    public string SelectedToolMetadata => SelectedTool is null
        ? string.Empty
        : $"{SelectedTool.Area} | {SelectedTool.Section} | Risk: {SelectedTool.Risk}";

    public string SelectedToolTechnicalDetails => SelectedTool is null
        ? string.Empty
        : $"Command ID: {SelectedTool.Id}\nAdministrator access: {SelectedTool.AdministratorAccess}\nRestart: {SelectedTool.Restart}\nMigration: {SelectedTool.MigrationState}\nLegacy source: {SelectedTool.Definition.LegacySource}";

    public bool IsSelectedToolFavorite => SelectedTool is not null && _favoriteIds.Contains(SelectedTool.Id);

    public void SelectTab(string tab)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tab);
        if (string.Equals(_selectedTab, tab, StringComparison.Ordinal))
        {
            return;
        }
        _selectedTab = tab;
        Refresh();
    }

    public void SetCompactLayout(bool isCompact)
    {
        if (IsCompactLayout == isCompact)
        {
            return;
        }
        IsCompactLayout = isCompact;
        foreach (ToolRowViewModel row in VisibleTools)
        {
            row.IsCompact = isCompact;
        }
    }

    public void ToggleFavorite()
    {
        if (SelectedTool is null)
        {
            return;
        }
        if (!_favoriteIds.Add(SelectedTool.Id))
        {
            _favoriteIds.Remove(SelectedTool.Id);
        }
        OnPropertyChanged(nameof(IsSelectedToolFavorite));
        if (string.Equals(_selectedTab, "Favorites", StringComparison.Ordinal))
        {
            Refresh();
        }
    }

    private void RecordRecent(string commandId)
    {
        _recentIds.Remove(commandId);
        _recentIds.Insert(0, commandId);
        if (_recentIds.Count > 20)
        {
            _recentIds.RemoveRange(20, _recentIds.Count - 20);
        }
    }

    private void NotifySelectedToolChanged()
    {
        OnPropertyChanged(nameof(SelectedToolTitle));
        OnPropertyChanged(nameof(SelectedToolSummary));
        OnPropertyChanged(nameof(SelectedToolMetadata));
        OnPropertyChanged(nameof(SelectedToolTechnicalDetails));
        OnPropertyChanged(nameof(IsSelectedToolFavorite));
    }

    private void Refresh()
    {
        ToolFilter filter = new(
            Area: SelectedAreaOption.Value,
            Risk: SelectedRiskOption.Value,
            ReadOnly: ReadOnlyOnly ? true : null);

        IEnumerable<CommandDefinition> commands = _catalog.Search(SearchText, filter);
        commands = _selectedTab switch
        {
            "Favorites" => commands.Where(command => _favoriteIds.Contains(command.Id)),
            "Recent" => commands
                .Where(command => _recentIds.Contains(command.Id))
                .OrderBy(command => _recentIds.IndexOf(command.Id)),
            "Presets" => commands.Where(command => command.Id == "presets"),
            _ => commands,
        };

        if (string.Equals(_selectedTab, "Categories", StringComparison.Ordinal))
        {
            commands = commands.OrderBy(command => command.Area, StringComparer.OrdinalIgnoreCase)
                .ThenBy(command => command.Section, StringComparer.OrdinalIgnoreCase)
                .ThenBy(command => command.Title, StringComparer.OrdinalIgnoreCase);
        }

        string? previousSelectedId = SelectedTool?.Id;
        VisibleTools.Clear();
        ToolRowViewModel? newSelectedTool = null;
        foreach (CommandDefinition command in commands)
        {
            var row = new ToolRowViewModel(command) { IsCompact = IsCompactLayout };
            VisibleTools.Add(row);
            if (previousSelectedId != null && string.Equals(command.Id, previousSelectedId, StringComparison.Ordinal))
            {
                newSelectedTool = row;
            }
        }
        SelectedTool = newSelectedTool;
        OnPropertyChanged(nameof(ResultCountText));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EmptyMessage));
    }
}

