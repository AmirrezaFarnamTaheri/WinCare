using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using WinCare.Application.Downloads;
using WinCare.Domain.Commands;

namespace WinCare.App.ViewModels.Pages;

/// <summary>
/// HTTP(S) download workspace. Torrents remain unavailable until an
/// independently tested real data-plane engine exists.
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
    private string _expectedSha256 = string.Empty;
    private string _notice = string.Empty;
    private string _filter = "All";
    private string _searchText = string.Empty;

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
    /// <summary>Optional expected digest. Blank means restart instead of unchecked byte-range continuation.</summary>
    public string ExpectedSha256
    {
        get => _expectedSha256;
        set
        {
            if (SetProperty(ref _expectedSha256, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(IsExpectedSha256Valid));
                OnPropertyChanged(nameof(ChecksumHint));
                OnPropertyChanged(nameof(CanAdd));
            }
        }
    }

    public bool IsExpectedSha256Valid
    {
        get
        {
            string digest = _expectedSha256.Trim();
            return digest.Length == 0 || (digest.Length == 64 && digest.All(Uri.IsHexDigit));
        }
    }

    public string ChecksumHint => IsExpectedSha256Valid
        ? "Optional. A known checksum verifies the complete file; otherwise retries restart."
        : "Enter exactly 64 hexadecimal characters (0–9, A–F), or leave blank.";

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
        !IsBusy &&
        Uri.TryCreate(_newUrl.Trim(), UriKind.Absolute, out Uri? uri) &&
        uri.Scheme is "http" or "https" &&
        uri.UserInfo.Length == 0 && uri.AbsoluteUri.Length <= 2048 &&
        IsExpectedSha256Valid;

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
        ? "No downloads yet. Paste an HTTP or HTTPS file link above."
        : "No downloads match. Try another search or choose All.";

    public string TotalText => Jobs.Count.ToString(System.Globalization.CultureInfo.CurrentCulture);
    public string InProgressText =>
        Jobs.Count(j => j.State is "Pending" or "Running" or "SuspendRequested" or "Suspended" or "Interrupted" or "CancelRequested")
            .ToString(System.Globalization.CultureInfo.CurrentCulture);
    public string FinishedText =>
        Jobs.Count(j => j.State is "Completed" or "Cancelled" or "Failed")
            .ToString(System.Globalization.CultureInfo.CurrentCulture);

    public void Activate()
    {
        _pageCancellation?.Dispose();
        _pageCancellation = new CancellationTokenSource();
        _isActive = true;

        // Opening Transfers must never start network activity or create a demo job.
    }

    public void Deactivate()
    {
        _isActive = false;
        _pageCancellation?.Cancel();
        _pageCancellation?.Dispose();
        _pageCancellation = null;
        NewUrl = string.Empty;
        ExpectedSha256 = string.Empty;
    }

    public void SetFilter(string filter)
    {
        _filter = filter is "Active" or "Finished" ? filter : "All";
        ApplyFilter();
    }

    public void SetSearch(string? search)
    {
        string normalized = search?.Trim() ?? string.Empty;
        if (string.Equals(_searchText, normalized, StringComparison.OrdinalIgnoreCase)) return;
        _searchText = normalized;
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
            if (result.Status == CommandResultStatus.Succeeded)
            {
                IReadOnlyList<DownloadJobRow> projected = DownloadJobProjection.Project(result.Data, _center.ActiveIds);
                Synchronize(Jobs, projected);
            }

            ApplyFilter();
            OnPropertyChanged(nameof(TotalText));
            OnPropertyChanged(nameof(InProgressText));
            OnPropertyChanged(nameof(FinishedText));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Downloads] Refresh failed: {ex.GetType().Name}");
            if (_isActive) Notice = "Transfers state is temporarily unavailable.";
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
        Synchronize(VisibleJobs, DownloadViewFilters.Apply(Jobs, _filter, _searchText));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EmptyDescription));
    }

    private static void Synchronize(ObservableCollection<DownloadJobRow> current, IReadOnlyList<DownloadJobRow> desired)
    {
        for (int i = 0; i < desired.Count; i++)
        {
            DownloadJobRow incoming = desired[i];
            if (i < current.Count && current[i].Id == incoming.Id)
            {
                if (current[i] != incoming) current[i] = incoming;
                continue;
            }
            int oldIndex = -1;
            for (int j = i + 1; j < current.Count; j++)
            {
                if (current[j].Id == incoming.Id) { oldIndex = j; break; }
            }
            if (oldIndex >= 0)
            {
                current.Move(oldIndex, i);
                if (current[i] != incoming) current[i] = incoming;
            }
            else
            {
                current.Insert(i, incoming);
            }
        }
        while (current.Count > desired.Count) current.RemoveAt(current.Count - 1);
    }

    public async Task AddAsync()
    {
        if (!CanAdd) return;
        string link = NewUrl.Trim();
        IsBusy = true;
        try
        {
            CommandResult result = await _center.AddAsync(
                link, ExpectedSha256, _pageCancellation?.Token ?? default);
            if (!_isActive) return;
            if (result.Status == CommandResultStatus.Succeeded)
            {
                NewUrl = string.Empty;
                ExpectedSha256 = string.Empty;
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
            Notice = action == "resume"
                ? "Download attempt started. Partial bytes are reused only when an expected SHA-256 digest and valid HTTP range support safe resumption; otherwise it restarts."
                : "Download started. You can keep using WinCare.";
            await RefreshAsync();
            return;
        }

        IsBusy = true;
        try
        {
            CancellationToken cancellationToken = _pageCancellation?.Token ?? default;
            CommandResult result = action switch
            {
                "pause" => await _center.PauseAsync(row.Id, cancellationToken),
                "cancel" => await _center.CancelAsync(row.Id, cancellationToken),
                _ => await _center.RemoveAsync(row.Id, cancellationToken),
            };
            if (!_isActive) return;
            Notice = result.Status == CommandResultStatus.Succeeded
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
