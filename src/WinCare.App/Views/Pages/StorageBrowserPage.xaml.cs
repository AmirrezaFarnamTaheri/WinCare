using System.Collections.ObjectModel;
using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Windows.ApplicationModel.DataTransfer;
using WinCare.App.Services;
using WinCare.Application.Storage;

namespace WinCare.App.Views.Pages;

public sealed partial class StorageBrowserPage : Page
{
    private const double CompactWidth = 820;
    private readonly Stack<string> _back = new();
    private readonly List<StorageBrowserItem> _allEntries = [];
    private CancellationTokenSource? _reportCancellation;
    private string? _currentPath;
    private bool _loadingDrives;
    private bool _visible;
    private int _navigationVersion;

    public StorageBrowserPage()
    {
        InitializeComponent();
    }

    public ObservableCollection<StorageBrowserDrive> Drives { get; } = [];
    public ObservableCollection<StorageBrowserItem> VisibleEntries { get; } = [];
    public ObservableCollection<StorageHotspot> LargestFolders { get; } = [];

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _visible = true;
        if (Drives.Count == 0)
        {
            try
            {
                _loadingDrives = true;
                var drives = await Task.Run(StorageExplorer.Drives);
                if (!_visible) return;
                foreach (StorageBrowserDrive drive in drives) Drives.Add(drive);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ShowError(ex.Message);
            }
            finally { _loadingDrives = false; }
        }
        string initial = e.Parameter as string ?? _currentPath ??
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!Directory.Exists(initial))
        {
            string? folder = Path.GetDirectoryName(initial);
            if (folder is not null && Directory.Exists(folder)) initial = folder;
        }
        if (string.IsNullOrWhiteSpace(initial) && Drives.Count > 0) initial = Drives[0].Root;
        if (!string.IsNullOrWhiteSpace(initial)) await NavigateAsync(initial, remember: false);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        _visible = false;
        _navigationVersion++;
        _reportCancellation?.Cancel();
        base.OnNavigatedFrom(e);
    }

    private async Task NavigateAsync(string path, bool remember = true)
    {
        int version = ++_navigationVersion;
        _reportCancellation?.Cancel();
        ScanProgress.Visibility = Visibility.Visible;
        ScanProgress.IsActive = true;
        try
        {
            StorageBrowserListing listing = await Task.Run(() => StorageExplorer.Browse(path));
            if (!_visible || version != _navigationVersion) return;
            if (remember && _currentPath is not null &&
                !string.Equals(listing.Path, _currentPath, StringComparison.OrdinalIgnoreCase))
                _back.Push(_currentPath);
            _currentPath = listing.Path;
            PathInput.Text = listing.Path;
            BackButton.IsEnabled = _back.Count > 0;
            UpButton.IsEnabled = Path.GetDirectoryName(listing.Path) is not null;
            _allEntries.Clear();
            _allEntries.AddRange(listing.Items);
            LargestFolders.Clear();
            HotspotsExpander.Visibility = Visibility.Collapsed;
            UpdateFilteredEntries();
            NoticeBar.IsOpen = false;
            ScanSummaryText.Text = listing.Truncated
                ? "Showing the first 600 entries. Narrow your search in Windows File Explorer for larger folders."
                : listing.UnreadableEntries > 0
                    ? $"{listing.UnreadableEntries} entries could not be read. Other files are shown."
                    : "Select a folder to open it, or measure this folder to estimate its contents.";
            _loadingDrives = true;
            try
            {
                StorageBrowserDrive? drive = Drives
                    .Where(item => listing.Path.StartsWith(item.Root, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(item => item.Root.Length).FirstOrDefault();
                if (drive is not null)
                {
                    DrivePicker.SelectedItem = drive;
                    DriveSpaceText.Text = drive.SpaceLabel;
                    DriveUsageBar.Value = drive.UsedPercent;
                }
            }
            finally { _loadingDrives = false; }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            if (_visible && version == _navigationVersion) ShowError(ex.Message);
        }
        finally
        {
            if (version == _navigationVersion)
            {
                ScanProgress.IsActive = false;
                ScanProgress.Visibility = Visibility.Collapsed;
            }
        }
    }

    private void UpdateFilteredEntries()
    {
        if (FolderTitle is null || CountText is null || EmptyFolderText is null) return;
        string term = FilterInput.Text.Trim();
        IEnumerable<StorageBrowserItem> filtered = term.Length == 0 ? _allEntries :
            _allEntries.Where(item => item.Name.Contains(term, StringComparison.OrdinalIgnoreCase));
        VisibleEntries.Clear();
        foreach (StorageBrowserItem item in filtered) VisibleEntries.Add(item);
        FolderTitle.Text = _currentPath is null ? "Folder contents" : Path.GetFileName(Path.TrimEndingDirectorySeparator(_currentPath)) is string name && name.Length > 0 ? name : _currentPath;
        CountText.Text = $"{VisibleEntries.Count:N0} shown / {_allEntries.Count:N0} loaded";
        EmptyFolderText.Visibility = VisibleEntries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyFolderText.Text = _allEntries.Count == 0 ? "This folder is empty." : "No items match your filter.";
    }

    private async void DrivePicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loadingDrives && DrivePicker.SelectedItem is StorageBrowserDrive drive)
            await NavigateAsync(drive.Root);
    }

    private async void EntriesList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not StorageBrowserItem item) return;
        if (item.IsFolder && !item.IsLink) await NavigateAsync(item.FullPath);
        else ScanSummaryText.Text = item.IsLink
            ? "This link was not followed. Use Windows File Explorer to inspect its destination."
            : "File selected. Use Copy path or Open in Explorer to work with it.";
    }

    private async void Go_Click(object sender, RoutedEventArgs e) => await NavigateAsync(PathInput.Text.Trim());
    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPath is not null) await NavigateAsync(_currentPath, remember: false);
    }
    private async void Up_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPath is null) return;
        string? parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(_currentPath));
        if (parent is not null) await NavigateAsync(parent);
    }
    private async void Back_Click(object sender, RoutedEventArgs e)
    {
        if (_back.Count > 0) await NavigateAsync(_back.Pop(), remember: false);
    }
    private void PathInput_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter) { Go_Click(sender, e); e.Handled = true; }
    }
    private void FilterInput_TextChanged(object sender, TextChangedEventArgs e) => UpdateFilteredEntries();

    private void OpenLocation_Click(object sender, RoutedEventArgs e)
    {
        string? path = (EntriesList.SelectedItem as StorageBrowserItem)?.FullPath;
        path = path is not null && File.Exists(path) ? Path.GetDirectoryName(path) : path ?? _currentPath;
        if (path is null) return;
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        { ShowError($"Windows could not open this location: {ex.Message}"); }
    }

    private void CopyPath_Click(object sender, RoutedEventArgs e)
    {
        string? path = (EntriesList.SelectedItem as StorageBrowserItem)?.FullPath ?? _currentPath;
        if (path is null) return;
        var data = new DataPackage();
        data.SetText(path);
        Clipboard.SetContent(data);
        ScanSummaryText.Text = "Path copied to clipboard.";
    }

    private async void Scan_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPath is null || !ScanButton.IsEnabled) return;
        string root = _currentPath;
        _reportCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _reportCancellation = cancellation;
        ScanButton.IsEnabled = false;
        ScanProgress.Visibility = Visibility.Visible;
        ScanProgress.IsActive = true;
        ScanSummaryText.Text = "Measuring folder contents…";
        try
        {
            StorageReport report = await AppRuntime.Current.StorageReports.CreateReportAsync(
                new StorageReportRequest(root, MaxEntries: 20_000, MaxDepth: 12), cancellation.Token);
            if (!_visible || cancellation.IsCancellationRequested || _currentPath != root) return;
            LargestFolders.Clear();
            foreach (StorageDirectoryEntry directory in report.Directories
                .Where(item => item.Depth == 1)
                .OrderByDescending(item => item.AccountedBytes).Take(8))
            {
                LargestFolders.Add(new StorageHotspot(
                    Path.GetFileName(Path.TrimEndingDirectorySeparator(directory.Path)),
                    directory.Path, directory.AccountedBytes, report.AccountedBytes, report.IsPartial));
            }
            HotspotsExpander.Visibility = LargestFolders.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            string scope = report.IsPartial ? "Partial scan" : "Folder total";
            ScanSummaryText.Text = $"{scope}: {StorageExplorer.FormatBytes(report.AccountedBytes)} across {report.FilesSeen:N0} files. " +
                (report.IsPartial ? "Some entries were skipped or a scan limit was reached." : "Read-only; no files changed.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            if (_visible && !cancellation.IsCancellationRequested) ShowError(ex.Message);
        }
        finally
        {
            if (ReferenceEquals(_reportCancellation, cancellation))
            {
                ScanButton.IsEnabled = true;
                ScanProgress.IsActive = false;
                ScanProgress.Visibility = Visibility.Collapsed;
                _reportCancellation = null;
            }
            cancellation.Dispose();
        }
    }

    private async void Hotspot_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is StorageHotspot folder)
            await NavigateAsync(folder.FullPath);
    }

    private void ShowError(string message)
    {
        NoticeBar.Severity = InfoBarSeverity.Error;
        NoticeBar.Message = message;
        NoticeBar.IsOpen = true;
    }

    private void Page_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        bool compact = e.NewSize.Width < CompactWidth;
        PageLayout.Padding = compact ? new Thickness(16, 18, 16, 20) : new Thickness(32, 28, 32, 28);
        SearchActions.RowDefinitions.Clear();
        SearchActions.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        if (compact) SearchActions.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        SearchActions.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
        for (int i = 1; i < 4; i++)
            SearchActions.ColumnDefinitions[i].Width = GridLength.Auto;
        Grid.SetRow(ScanButton, compact ? 1 : 0);
        Grid.SetRow(RefreshButton, compact ? 1 : 0);
        Grid.SetRow(CopyPathButton, compact ? 1 : 0);
        Grid.SetColumn(ScanButton, compact ? 0 : 1);
        Grid.SetColumn(RefreshButton, compact ? 1 : 2);
        Grid.SetColumn(CopyPathButton, compact ? 2 : 3);
        Grid.SetColumnSpan(FilterInput, compact ? 4 : 1);
        DriveSpaceText.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        DriveUsageBar.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        Grid.SetRow(OpenExplorerButton, compact ? 1 : 0);
        Grid.SetColumn(OpenExplorerButton, compact ? 0 : 2);
        Grid.SetColumnSpan(OpenExplorerButton, compact ? 3 : 1);
        OpenExplorerButton.HorizontalAlignment = compact ? HorizontalAlignment.Stretch : HorizontalAlignment.Right;
    }
}
