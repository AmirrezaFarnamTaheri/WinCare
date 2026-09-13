using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Threading.Tasks;
using WinCare.Application.Commands;
using WinCare.Application.Tools;
using WinCare.App.Services;
using WinCare.CommandCatalog;
using WinCare.CommandCatalog.Models;

namespace WinCare.App.ViewModels.Pages;

public sealed class AllToolsPageViewModel : ObservableObject, IDisposable
{
    private readonly ToolCatalogService _catalog;
    private readonly Microsoft.UI.Dispatching.DispatcherQueue? _uiQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
    private bool _isDisposed;
    private readonly HashSet<string> _favoriteIds = new(StringComparer.Ordinal);
    private readonly List<string> _recentIds = [];
    private string _searchText = string.Empty;
    private AreaFilterOption _selectedAreaOption;
    private SectionFilterOption _selectedSectionOption;
    private RiskFilterOption _selectedRiskOption;
    private bool _readOnlyOnly;
    private string _selectedTab = "Tools";
    private ToolRowViewModel? _selectedTool;
    private bool _isDetailsOpen;
    private bool _isCompactLayout;

    public AllToolsPageViewModel() : this(AppRuntime.Current.ToolCatalog, AppRuntime.Current.Dispatcher) { }
    public AllToolsPageViewModel(ToolCatalogService catalog) : this(catalog, AppRuntime.Current.Dispatcher) { }

    public AllToolsPageViewModel(ToolCatalogService catalog, CommandDispatcher dispatcher)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        foreach (string id in AppPreferences.FavoriteCommandIds) _favoriteIds.Add(id);
        _recentIds.AddRange(AppPreferences.RecentCommandIds);
        AreaOptions = [new AreaFilterOption("All areas", null), .. _catalog.All.Select(command => command.Area).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(area => area, StringComparer.OrdinalIgnoreCase).Select(area => new AreaFilterOption(area, area))];
        SectionOptions = [new SectionFilterOption("All sections", null)];
        _selectedSectionOption = SectionOptions[0];
        CategoryCards = BuildCategoryCards();
        RiskOptions = [new RiskFilterOption("All risk levels", null), new RiskFilterOption("Read-only", CommandRisk.ReadOnly), new RiskFilterOption("Low", CommandRisk.Low), new RiskFilterOption("Moderate", CommandRisk.Moderate), new RiskFilterOption("High", CommandRisk.High), new RiskFilterOption("Critical", CommandRisk.Critical)];
        _selectedAreaOption = AreaOptions[0];
        _selectedRiskOption = RiskOptions[0];
        Execution = new ToolExecutionViewModel(dispatcher, RecordRecent);
        IReadOnlyDictionary<string, RemediationRule> rules = RemediationCatalog.LoadRules().ToDictionary(rule => rule.Id, StringComparer.OrdinalIgnoreCase);
        PresetCards = RemediationCatalog.LoadPresets().Select(preset => PresetCardViewModel.Create(preset, rules)).ToArray();
        _catalog.CatalogChanged += OnCatalogChanged;
        Refresh();
    }

    public ObservableCollection<ToolRowViewModel> VisibleTools { get; } = new();
    public IReadOnlyList<AreaFilterOption> AreaOptions { get; private set; }
    public IReadOnlyList<SectionFilterOption> SectionOptions { get; private set; }
    public IReadOnlyList<RiskFilterOption> RiskOptions { get; }
    public IReadOnlyList<ToolCategoryViewModel> CategoryCards { get; private set; }
    public ToolExecutionViewModel Execution { get; }
    public IReadOnlyList<PresetCardViewModel> PresetCards { get; }
    public bool IsPresetTab => string.Equals(_selectedTab, "Care plans", StringComparison.Ordinal);
    public bool IsCategoryTab => string.Equals(_selectedTab, "Categories", StringComparison.Ordinal);
    public bool IsCatalogTab => !IsPresetTab && !IsCategoryTab;
    private CancellationTokenSource? _searchCts;

    public void OpenSearch(string query)
    {
        _searchCts?.Cancel();
        _selectedTab = "Tools";
        _selectedAreaOption = AreaOptions[0];
        RebuildSectionOptions();
        _selectedSectionOption = SectionOptions[0];
        _selectedRiskOption = RiskOptions[0];
        _readOnlyOnly = false;
        _searchText = query;
        OnPropertyChanged(nameof(IsPresetTab)); OnPropertyChanged(nameof(IsCategoryTab)); OnPropertyChanged(nameof(IsCatalogTab));
        OnPropertyChanged(nameof(SelectedAreaOption)); OnPropertyChanged(nameof(SelectedSectionOption)); OnPropertyChanged(nameof(SelectedRiskOption)); OnPropertyChanged(nameof(ReadOnlyOnly)); OnPropertyChanged(nameof(SearchText));
        Refresh();
        SelectedTool = VisibleTools.FirstOrDefault(tool => string.Equals(tool.Id, query, StringComparison.OrdinalIgnoreCase));
    }

    public void OpenTool(string commandId, System.Text.Json.JsonElement parameters)
    {
        OpenSearch(commandId);
        if (SelectedTool is not null) { Execution.ApplyParameterValues(parameters); OnPropertyChanged(nameof(SelectedTool)); }
    }

    public string SearchText
    {
        get => _searchText;
        set { if (SetProperty(ref _searchText, value ?? string.Empty)) DebounceSearch(); }
    }

    private void DebounceSearch()
    {
        _searchCts?.Cancel(); _searchCts?.Dispose(); _searchCts = new CancellationTokenSource(); _ = DebounceSearchAsync(_searchCts.Token);
    }

    private async Task DebounceSearchAsync(CancellationToken token)
    {
        try { await Task.Delay(250, token); Refresh(); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[AllToolsPageViewModel] DebounceSearch fault: {ex.Message}"); }
    }

    public AreaFilterOption SelectedAreaOption
    {
        get => _selectedAreaOption;
        set { if (value is not null && SetProperty(ref _selectedAreaOption, value)) { RebuildSectionOptions(); _selectedSectionOption = SectionOptions[0]; OnPropertyChanged(nameof(SelectedSectionOption)); Refresh(); } }
    }
    public SectionFilterOption SelectedSectionOption { get => _selectedSectionOption; set { if (value is not null && SetProperty(ref _selectedSectionOption, value)) Refresh(); } }
    public RiskFilterOption SelectedRiskOption { get => _selectedRiskOption; set { if (value is not null && SetProperty(ref _selectedRiskOption, value)) Refresh(); } }
    public bool ReadOnlyOnly { get => _readOnlyOnly; set { if (SetProperty(ref _readOnlyOnly, value)) Refresh(); } }

    public ToolRowViewModel? SelectedTool
    {
        get => _selectedTool;
        set
        {
            if (ReferenceEquals(_selectedTool, value)) return;
            Execution.SelectTool(value);
            if (SetProperty(ref _selectedTool, value)) { IsDetailsOpen = value is not null; NotifySelectedToolChanged(); }
        }
    }

    public bool IsDetailsOpen { get => _isDetailsOpen; set => SetProperty(ref _isDetailsOpen, value); }
    public bool IsCompactLayout { get => _isCompactLayout; private set => SetProperty(ref _isCompactLayout, value); }
    public string ResultCountText => VisibleTools.Count == 1 ? "1 tool" : $"{VisibleTools.Count} tools";
    public bool IsEmpty => VisibleTools.Count == 0;
    public string EmptyMessage => _selectedTab switch { "Favorites" => "No favorites yet. Select a tool and add it to Favorites.", "Recent" => "Tools you run will appear here.", "Care plans" => "No care plans are available.", "Categories" => "No categories are available.", _ => "No tools match the current search and filters." };
    public string SelectedToolTitle => SelectedTool?.Title ?? "Select a tool";
    public string SelectedToolSummary => SelectedTool?.Summary ?? "Choose a task to understand what it does, what it needs, and what will happen before anything runs.";
    public string SelectedToolMetadata => SelectedTool is null ? string.Empty : $"{SelectedTool.Area} · {SelectedTool.Section} · {SelectedTool.Risk} impact";
    public string SelectedToolTechnicalDetails => SelectedTool is null ? string.Empty : $"Command ID: {SelectedTool.Id}\nAdministrator access: {SelectedTool.AdministratorAccess}\nRestart: {SelectedTool.Restart}\nMigration: {SelectedTool.MigrationState}\nLegacy source: {SelectedTool.Definition.LegacySource}";
    public bool IsSelectedToolFavorite => SelectedTool is not null && _favoriteIds.Contains(SelectedTool.Id);

    public void SelectTab(string tab)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tab);
        if (string.Equals(_selectedTab, tab, StringComparison.Ordinal)) return;
        _selectedTab = tab; _searchCts?.Cancel(); OnPropertyChanged(nameof(IsPresetTab)); OnPropertyChanged(nameof(IsCategoryTab)); OnPropertyChanged(nameof(IsCatalogTab)); Refresh();
    }

    public void SelectPresetForReview(string presetId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(presetId);
        if (!PresetCards.Any(preset => string.Equals(preset.Id, presetId, StringComparison.Ordinal))) throw new ArgumentException("Choose a preset from the catalog.", nameof(presetId));
        _searchCts?.Cancel();
        CommandDefinition command = _catalog.All.Single(item => string.Equals(item.Id, "preset", StringComparison.Ordinal));
        SelectedTool = new ToolRowViewModel(command) { IsCompact = IsCompactLayout };
        ToolParameterFieldViewModel field = Execution.ParameterFields.Single(item => string.Equals(item.Name, "PresetId", StringComparison.Ordinal));
        field.Value = presetId; OnPropertyChanged(nameof(SelectedTool));
    }

    public void SetCompactLayout(bool isCompact)
    {
        if (IsCompactLayout == isCompact) return;
        IsCompactLayout = isCompact; foreach (ToolRowViewModel row in VisibleTools) row.IsCompact = isCompact;
    }

    public void ToggleFavorite()
    {
        if (SelectedTool is null) return;
        if (!_favoriteIds.Add(SelectedTool.Id)) _favoriteIds.Remove(SelectedTool.Id);
        AppPreferences.SaveFavoriteCommandIds(_favoriteIds); OnPropertyChanged(nameof(IsSelectedToolFavorite));
        if (string.Equals(_selectedTab, "Favorites", StringComparison.Ordinal)) Refresh();
    }

    private void RecordRecent(string commandId)
    {
        _recentIds.Remove(commandId); _recentIds.Insert(0, commandId);
        if (_recentIds.Count > 20) _recentIds.RemoveRange(20, _recentIds.Count - 20);
        AppPreferences.SaveRecentCommandIds(_recentIds);
    }

    private void NotifySelectedToolChanged()
    {
        OnPropertyChanged(nameof(SelectedToolTitle)); OnPropertyChanged(nameof(SelectedToolSummary)); OnPropertyChanged(nameof(SelectedToolMetadata)); OnPropertyChanged(nameof(SelectedToolTechnicalDetails)); OnPropertyChanged(nameof(IsSelectedToolFavorite));
    }

    private void RebuildAreaOptions()
    {
        var currentSelectedArea = SelectedAreaOption?.Value;
        AreaOptions = [new AreaFilterOption("All areas", null), .. _catalog.All.Select(command => command.Area).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(area => area, StringComparer.OrdinalIgnoreCase).Select(area => new AreaFilterOption(area, area))];
        _selectedAreaOption = AreaOptions.FirstOrDefault(o => string.Equals(o.Value, currentSelectedArea, StringComparison.OrdinalIgnoreCase)) ?? AreaOptions[0];
        OnPropertyChanged(nameof(AreaOptions)); OnPropertyChanged(nameof(SelectedAreaOption)); RebuildSectionOptions(); CategoryCards = BuildCategoryCards(); OnPropertyChanged(nameof(CategoryCards));
    }

    private void RebuildSectionOptions()
    {
        string? area = _selectedAreaOption?.Value; string? current = _selectedSectionOption?.Value;
        IEnumerable<string> sections = _catalog.All.Where(command => string.IsNullOrWhiteSpace(area) || string.Equals(command.Area, area, StringComparison.OrdinalIgnoreCase)).Select(command => command.Section).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(section => section, StringComparer.OrdinalIgnoreCase);
        SectionOptions = [new SectionFilterOption("All sections", null), .. sections.Select(section => new SectionFilterOption(section, section))];
        _selectedSectionOption = SectionOptions.FirstOrDefault(option => string.Equals(option.Value, current, StringComparison.OrdinalIgnoreCase)) ?? SectionOptions[0];
        OnPropertyChanged(nameof(SectionOptions)); OnPropertyChanged(nameof(SelectedSectionOption));
    }

    private IReadOnlyList<ToolCategoryViewModel> BuildCategoryCards() => _catalog.All.GroupBy(command => new { command.Area, command.Section }).OrderBy(group => group.Key.Area, StringComparer.OrdinalIgnoreCase).ThenBy(group => group.Key.Section, StringComparer.OrdinalIgnoreCase).Select(group => new ToolCategoryViewModel(group.Key.Area, group.Key.Section, group.Count(), group.Select(command => command.Summary).FirstOrDefault() ?? string.Empty)).ToArray();

    public void OpenCategory(string area, string section)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(area); ArgumentException.ThrowIfNullOrWhiteSpace(section); _searchCts?.Cancel(); _selectedTab = "Tools"; _searchText = string.Empty;
        _selectedAreaOption = AreaOptions.First(option => string.Equals(option.Value, area, StringComparison.OrdinalIgnoreCase)); RebuildSectionOptions(); _selectedSectionOption = SectionOptions.First(option => string.Equals(option.Value, section, StringComparison.OrdinalIgnoreCase)); _selectedRiskOption = RiskOptions[0]; _readOnlyOnly = false;
        OnPropertyChanged(nameof(IsPresetTab)); OnPropertyChanged(nameof(IsCategoryTab)); OnPropertyChanged(nameof(IsCatalogTab)); OnPropertyChanged(nameof(SearchText)); OnPropertyChanged(nameof(SelectedAreaOption)); OnPropertyChanged(nameof(SelectedSectionOption)); OnPropertyChanged(nameof(SelectedRiskOption)); OnPropertyChanged(nameof(ReadOnlyOnly)); Refresh();
    }

    private void Refresh()
    {
        if (IsPresetTab || IsCategoryTab)
        {
            VisibleTools.Clear(); SelectedTool = null; OnPropertyChanged(nameof(ResultCountText)); OnPropertyChanged(nameof(IsEmpty)); OnPropertyChanged(nameof(EmptyMessage)); return;
        }
        ToolFilter filter = new(Area: SelectedAreaOption.Value, Section: SelectedSectionOption.Value, Risk: SelectedRiskOption.Value, ReadOnly: ReadOnlyOnly ? true : null);
        IEnumerable<CommandDefinition> commands = _catalog.Search(SearchText, filter);
        commands = _selectedTab switch { "Favorites" => commands.Where(command => _favoriteIds.Contains(command.Id)), "Recent" => commands.Where(command => _recentIds.Contains(command.Id)).OrderBy(command => _recentIds.IndexOf(command.Id)), "Care plans" => commands.Where(command => command.Id == "presets"), _ => commands };
        string? previousSelectedId = SelectedTool?.Id; VisibleTools.Clear(); ToolRowViewModel? newSelectedTool = null;
        foreach (CommandDefinition command in commands)
        {
            var row = new ToolRowViewModel(command) { IsCompact = IsCompactLayout }; VisibleTools.Add(row);
            if (previousSelectedId != null && string.Equals(command.Id, previousSelectedId, StringComparison.Ordinal)) newSelectedTool = row;
        }
        SelectedTool = newSelectedTool; OnPropertyChanged(nameof(ResultCountText)); OnPropertyChanged(nameof(IsEmpty)); OnPropertyChanged(nameof(EmptyMessage));
    }

    private void OnCatalogChanged(object? sender, EventArgs e)
    {
        if (_isDisposed) return;
        if (_uiQueue != null && !_uiQueue.HasThreadAccess) _uiQueue.TryEnqueue(() => { if (_isDisposed) return; RebuildAreaOptions(); Refresh(); });
        else { RebuildAreaOptions(); Refresh(); }
    }

    public void Dispose()
    {
        if (_isDisposed) return; _isDisposed = true; _catalog.CatalogChanged -= OnCatalogChanged; _searchCts?.Cancel(); _searchCts?.Dispose(); _searchCts = null;
    }
}

public sealed record ToolCategoryViewModel(string Area, string Section, int ToolCount, string ExampleSummary)
{
    public string CountText => ToolCount == 1 ? "1 tool" : $"{ToolCount} tools";
    public string Key => $"{Area}\u001f{Section}";
    public string AccessibleName => $"Browse {Area}, {Section}, {CountText}";
}

public sealed record PresetCardViewModel(string Id, string Title, string Description, string RuleCountText, string ImpactText, string RecoveryText)
{
    public string InspectAccessibleName => $"Inspect {Title} plan";
    public static PresetCardViewModel Create(PresetDefinition preset, IReadOnlyDictionary<string, RemediationRule> ruleCatalog)
    {
        RemediationRule[] rules = preset.RuleIds.Select(id => ruleCatalog[id]).ToArray(); RemediationRisk highestRisk = rules.Max(rule => rule.Risk);
        string admin = rules.Any(rule => rule.RequiresAdmin) ? "Administrator access" : "Standard access";
        string restart = rules.Any(rule => rule.RestartPossible) ? "restart may be needed" : "no restart expected";
        int reversible = rules.Count(rule => rule.Reversible);
        return new PresetCardViewModel(preset.Id, preset.Title, preset.Description, $"{rules.Length} rule{(rules.Length == 1 ? string.Empty : "s")}", $"Up to {highestRisk} risk · {admin} · {restart}", $"{reversible} of {rules.Length} rules declare recovery");
    }
}
