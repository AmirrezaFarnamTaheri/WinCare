using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using WinCare.Application.Diagnostics;

namespace WinCare.App.Views.Pages;

public sealed partial class ProcessesPage : Page
{
    private const double CompactWidth = 840;
    private readonly ProcessMonitorService _monitor = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly Dictionary<int, ProcessDisplayRow> _rows = [];
private CancellationTokenSource _life = new();
    private bool _visible;
    private IReadOnlyList<MonitoredProcess> _snapshot = [];

    public ProcessesPage()
    {
        InitializeComponent();
        _timer.Tick += (_, _) => _ = RefreshAsync();
        SortPicker.SelectedIndex = 0;
    }

    public ObservableCollection<ProcessDisplayRow> VisibleRows { get; } = [];

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _life.Dispose();
        _life = new CancellationTokenSource();
        _visible = true;
        if (LiveRefresh.IsChecked == true) _timer.Start();
        _ = RefreshAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        _visible = false;
        _timer.Stop();
        _life.Cancel();
        base.OnNavigatedFrom(e);
    }

    private async Task RefreshAsync()
    {
        if (!_visible || !await _refreshGate.WaitAsync(0)) return;
        CancellationToken cancellationToken = _life.Token;
        try
        {
            ProcessSnapshot snapshot = await Task.Run(() => _monitor.Capture(cancellationToken), cancellationToken);
            if (!_visible || cancellationToken.IsCancellationRequested) return;
            _snapshot = snapshot.Items;
            foreach (MonitoredProcess item in snapshot.Items)
            {
                if (_rows.TryGetValue(item.Id, out ProcessDisplayRow? row) && row.StartedAt == item.StartedAt)
                    row.Update(item);
                else
                    _rows[item.Id] = new ProcessDisplayRow(item);
            }
            foreach (int staleId in _rows.Keys.Except(snapshot.Items.Select(item => item.Id)).ToArray())
                _rows.Remove(staleId);
            ApplyFilterAndSort();
            TaskCount.Text = snapshot.TotalCount.ToString("N0", CultureInfo.CurrentCulture);
            UpdatedTime.Text = DateTime.Now.ToString("t", CultureInfo.CurrentCulture);
            FooterText.Text = snapshot.Truncated
                ? "Showing the first 1,000 processes. Use Windows Task Manager for the full list."
                : "Readings refresh every five seconds when Live is enabled.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            if (_visible) ShowNotice(ex.Message, InfoBarSeverity.Error);
        }
        finally { _refreshGate.Release(); }
    }

    private void ApplyFilterAndSort()
    {
        string query = FilterInput.Text.Trim();
        IEnumerable<ProcessDisplayRow> rows = _snapshot
            .Where(item => query.Length == 0 ||
                item.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                item.Id.ToString(CultureInfo.InvariantCulture).Contains(query, StringComparison.OrdinalIgnoreCase))
            .Select(item => _rows[item.Id]);

        rows = SortPicker.SelectedIndex switch
        {
            1 => rows.OrderByDescending(item => item.MemoryBytes ?? -1).ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase),
            2 => rows.OrderByDescending(item => item.CpuPercent ?? -1).ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase),
            3 => rows.OrderBy(item => item.Id),
            _ => rows.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.Id)
        };

        ProcessDisplayRow[] desired = rows.ToArray();
        for (int index = 0; index < desired.Length; index++)
        {
            if (index < VisibleRows.Count && ReferenceEquals(VisibleRows[index], desired[index])) continue;
            int currentIndex = VisibleRows.IndexOf(desired[index]);
            if (currentIndex >= 0) VisibleRows.Move(currentIndex, index);
            else VisibleRows.Insert(index, desired[index]);
        }
        while (VisibleRows.Count > desired.Length)
            VisibleRows.RemoveAt(VisibleRows.Count - 1);
        ShownCount.Text = $"{VisibleRows.Count:N0} shown";
    }

    private async void EndTask_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ProcessDisplayRow row } || !row.CanEnd) return;
        MonitoredProcess item = row.Snapshot;
        var confirmation = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = $"End {item.Name}?",
            Content = $"Process {item.Id} will be asked to close immediately. Unsaved work may be lost. This cannot be undone.",
            PrimaryButtonText = "End task",
            CloseButtonText = "Keep running",
            DefaultButton = ContentDialogButton.Close
        };
        if (await confirmation.ShowAsync() != ContentDialogResult.Primary) return;
        if (sender is Button button) button.IsEnabled = false;
        try
        {
            EndTaskResult result = await Task.Run(() => ProcessMonitorService.EndTask(item));
            if (!_visible) return;
            ShowNotice(result.Message, result.Succeeded ? InfoBarSeverity.Success : InfoBarSeverity.Warning);
            await RefreshAsync();
        }
        finally
        {
            if (sender is Button control) control.IsEnabled = row.CanEnd;
        }
    }

    private void ShowNotice(string message, InfoBarSeverity severity)
    {
        TaskNotice.Severity = severity;
        TaskNotice.Message = message;
        TaskNotice.IsOpen = true;
    }

    private void OpenTaskManager_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "Taskmgr.exe"))
                { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException)
        { ShowNotice($"Windows Task Manager could not open: {ex.Message}", InfoBarSeverity.Error); }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();
    private void FilterInput_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilterAndSort();
    private void SortPicker_SelectionChanged(object sender, SelectionChangedEventArgs e) => ApplyFilterAndSort();
    private void LiveRefresh_Changed(object sender, RoutedEventArgs e)
    {
        if (_visible && LiveRefresh.IsChecked == true) _timer.Start();
        else _timer.Stop();
    }

    private void Page_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        bool compact = e.NewSize.Width < CompactWidth;
        PageLayout.Padding = compact ? new Thickness(16, 18, 16, 20) : new Thickness(32, 28, 32, 28);
        ActionsGrid.RowDefinitions.Clear();
        ActionsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        if (compact) ActionsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetColumn(FilterInput, 0);
        Grid.SetColumnSpan(FilterInput, compact ? 4 : 1);
        Grid.SetRow(SortPicker, compact ? 1 : 0);
        Grid.SetRow(LiveRefresh, compact ? 1 : 0);
        Grid.SetRow(RefreshProcessesButton, compact ? 1 : 0);
        Grid.SetColumn(SortPicker, compact ? 0 : 1);
        Grid.SetColumn(LiveRefresh, compact ? 1 : 2);
        Grid.SetColumn(RefreshProcessesButton, compact ? 2 : 3);
    }
}

/// <summary>Stable list row updated in place so live refresh preserves focus and scroll.</summary>
public sealed class ProcessDisplayRow : INotifyPropertyChanged
{
    private MonitoredProcess _snapshot;
    public ProcessDisplayRow(MonitoredProcess snapshot) { _snapshot = snapshot; }
    public event PropertyChangedEventHandler? PropertyChanged;
    public MonitoredProcess Snapshot => _snapshot;
    public int Id => _snapshot.Id;
    public string Name => _snapshot.Name;
    public DateTimeOffset? StartedAt => _snapshot.StartedAt;
    public bool CanEnd => _snapshot.CanEnd;
    public long? MemoryBytes => _snapshot.WorkingSetBytes;
    public double? CpuPercent => _snapshot.CpuPercent;
    public string CpuLabel => "CPU " + _snapshot.CpuLabel;
    public string MemoryLabel => _snapshot.MemoryLabel;
    public string PidLabel => "PID " + Id.ToString(CultureInfo.InvariantCulture);
    public string AccessibleName => $"End {Name}, process {Id}. Memory {MemoryLabel}, CPU {_snapshot.CpuLabel}.";

    public void Update(MonitoredProcess snapshot)
    {
        _snapshot = snapshot;
        foreach (string property in new[] { nameof(CpuLabel), nameof(MemoryLabel), nameof(AccessibleName) })
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    }
}
