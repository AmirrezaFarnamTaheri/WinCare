using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using WinCare.Application.Downloads;
using WinCare.Domain.Commands;

namespace WinCare.App.ViewModels.Pages;

/// <summary>
/// View-only projection of the existing command-owned download journal.
/// Transfers belong to AppRuntime, never to this cached page.
/// </summary>
public sealed class DownloadsPageViewModel : ObservableObject
{
    private readonly DownloadCenterService _center;
    private bool _isActive;
    private bool _isBusy;
    private bool _isLoading;
    private int _refreshInFlight;
    private CancellationTokenSource? _pageCancellation;
    private string _newUrl = string.Empty;
    private string _notice = string.Empty;
    private string _filter = "All";

    public DownloadsPageViewModel() : this(Services.AppRuntime.Current.Downloads) { }
    public DownloadsPageViewModel(DownloadCenterService center) =>
        _center = center ?? throw new ArgumentNullException(nameof(center));

    public ObservableCollection<DownloadJobRow> Jobs { get; } = new();
    public ObservableCollection<DownloadJobRow> VisibleJobs { get; } = new();

    public string NewUrl
    {
        get => _newUrl;
        set { if (SetProperty(ref _newUrl, value ?? string.Empty)) OnPropertyChanged(nameof(CanAdd)); }
    }
    public bool IsBusy
    {
        get => _isBusy;
        private set { if (SetProperty(ref _isBusy, value)) OnPropertyChanged(nameof(CanAdd)); }
    }
    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }
    public bool CanAdd =>
        !IsBusy && Uri.TryCreate(_newUrl.Trim(), UriKind.Absolute, out Uri? uri) &&
        uri.Scheme is "http" or "https" && uri.UserInfo.Length == 0 && uri.AbsoluteUri.Length <= 2048;
    public bool HasNotice => _notice.Length > 0;
    public string Notice
    {
        get => _notice;
        private set
        {
            if (SetProperty(ref _notice, value)) OnPropertyChanged(nameof(HasNotice));
        }
    }
    public bool IsEmpty => VisibleJobs.Count == 0 && !IsLoading;
    public string EmptyDescription => Jobs.Count == 0
        ? "No downloads yet. Paste a link above to create your first download."
        : "Nothing matches this view. Switch to All to see the full queue.";
    public string TotalText => Jobs.Count.ToString(System.Globalization.CultureInfo.CurrentCulture);
    public string InProgressText => Jobs.Count(j => j.State is "Pending" or "Running" or "SuspendRequested" or "Suspended" or "Interrupted" or "CancelRequested")
        .ToString(System.Globalization.CultureInfo.CurrentCulture);
    public string FinishedText => Jobs.Count(j => j.State is "Completed" or "Cancelled" or "Failed")
        .ToString(System.Globalization.CultureInfo.CurrentCulture);

    public void Activate()
    {
        _pageCancellation?.Dispose();
        _pageCancellation = new CancellationTokenSource();
        _isActive = true;
    }

    public void Deactivate()
    {
        _isActive = false;
        _pageCancellation?.Cancel();
        _pageCancellation?.Dispose();
        _pageCancellation = null;
        // Do not cancel process-lifetime transfers when navigating to another page.
        NewUrl = string.Empty;
    }

    public void SetFilter(string filter)
    {
        _filter = filter is "Active" or "Finished" ? filter : "All";
        ApplyFilter();
    }

    public async Task RefreshAsync()
    {
        if (!_isActive || Interlocked.CompareExchange(ref _refreshInFlight, 1, 0) != 0) return;
        CancellationToken token = _pageCancellation?.Token ?? CancellationToken.None;
        IsLoading = Jobs.Count == 0;
        try
        {
            CommandResult result = await _center.ReadAsync(token);
            if (!_isActive || token.IsCancellationRequested) return;
            if (result.Status != CommandResultStatus.Succeeded)
            {
                Notice = "Download history could not be loaded. Try Refresh.";
                return;
            }
            IReadOnlyList<DownloadJobRow> projected = DownloadJobProjection.Project(result.Data, _center.ActiveIds);
            Jobs.Clear();
            foreach (DownloadJobRow job in projected) Jobs.Add(job);
            ApplyFilter();
            OnPropertyChanged(nameof(TotalText));
            OnPropertyChanged(nameof(InProgressText));
            OnPropertyChanged(nameof(FinishedText));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Downloads] Refresh failed: {ex.GetType().Name}");
            if (_isActive) Notice = "Download history is temporarily unavailable.";
        }
        finally
        {
            IsLoading = false;
            Interlocked.Exchange(ref _refreshInFlight, 0);
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    private void ApplyFilter()
    {
        IEnumerable<DownloadJobRow> items = _filter switch
        {
            "Active" => Jobs.Where(j => j.State is "Pending" or "Running" or "SuspendRequested" or "Suspended" or "Interrupted" or "CancelRequested"),
            "Finished" => Jobs.Where(j => j.State is "Completed" or "Cancelled" or "Failed"),
            _ => Jobs,
        };
        VisibleJobs.Clear();
        foreach (DownloadJobRow item in items) VisibleJobs.Add(item);
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EmptyDescription));
    }

    public async Task AddAsync()
    {
        if (!CanAdd) return;
        IsBusy = true;
        try
        {
            CommandResult result = await _center.AddAsync(NewUrl.Trim(), _pageCancellation?.Token ?? default);
            if (!_isActive) return;
            if (result.Status == CommandResultStatus.Succeeded)
            {
                NewUrl = string.Empty;
                string? id = result.Data is System.Text.Json.JsonElement record &&
                    record.TryGetProperty("id", out System.Text.Json.JsonElement idValue)
                    ? idValue.GetString() : null;
                if (id is not null && _center.TryStart(id, resume: false, out Task<CommandResult> completion))
                {
                    Notice = "Download started. You can keep using WinCare.";
                    _ = ObserveTransferAsync(completion);
                }
                else
                {
                    Notice = "Download queued. Select Start to begin the transfer.";
                }
            }
            else Notice = "Could not add this download. Try another link or review your download folder.";
            await RefreshAsync();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Downloads] Add failed: {ex.GetType().Name}");
            if (_isActive) Notice = "Could not add that link. Check the URL and try again.";
        }
        finally { IsBusy = false; }
    }

    public async Task RunActionAsync(DownloadJobRow row, string action)
    {
        if (IsBusy || string.IsNullOrWhiteSpace(row.Id)) return;
        if (action is "start" or "resume")
        {
            if (!(action == "start" ? row.CanStart : row.CanResume)) return;
            if (!_center.TryStart(row.Id, action == "resume", out Task<CommandResult> completion))
            {
                Notice = "That download is already running.";
                return;
            }
            Notice = "Transfer started. You can keep using WinCare.";
            await RefreshAsync();
            _ = ObserveTransferAsync(completion);
            return;
        }

        if (action is not ("pause" or "cancel" or "remove")) return;
        if (action == "pause" && !row.CanPause ||
            action == "cancel" && !row.CanCancel ||
            action == "remove" && !row.CanRemove) return;
        IsBusy = true;
        try
        {
            CommandResult outcome = action switch
            {
                "pause" => await _center.PauseAsync(row.Id),
                "cancel" => await _center.CancelAsync(row.Id),
                _ => await _center.RemoveAsync(row.Id),
            };
            if (!_isActive) return;
            Notice = outcome.Status == CommandResultStatus.Succeeded
                ? action switch
                {
                    "pause" => "Pause requested. Partial data is retained.",
                    "cancel" => "Cancellation requested. Partial data will be discarded.",
                    _ => "Download removed from the list. The completed file was not deleted.",
                }
                : "Could not change this download. Refresh to see its current state.";
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Downloads] Action failed: {ex.GetType().Name}");
            if (_isActive) Notice = "The action could not finish. Check its status before retrying.";
        }
        finally { IsBusy = false; }
    }

    private async Task ObserveTransferAsync(Task<CommandResult> completion)
    {
        try
        {
            CommandResult result = await completion;
            if (_isActive)
            {
                Notice = result.Status == CommandResultStatus.Succeeded
                    ? "Download completed. The file is saved in Downloads."
                    : "The transfer stopped or failed. Refresh to inspect its state.";
                await RefreshAsync();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Downloads] Transfer stopped: {ex.GetType().Name}");
            if (_isActive)
            {
                Notice = "Transfer did not finish. Refresh to inspect its state.";
                await RefreshAsync();
            }
        }
    }
}
