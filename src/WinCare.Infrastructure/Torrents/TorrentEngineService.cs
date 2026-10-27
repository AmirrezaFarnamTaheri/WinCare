using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Application.Transfers;

namespace WinCare.Infrastructure.Torrents;

/// <summary>
/// Central BitTorrent transfers orchestrator managing active swarm sessions and storage allocations.
/// </summary>
public sealed class TorrentEngineService : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, TorrentSession> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _defaultDownloadDirectory;

    public TorrentEngineService(string? defaultDownloadDirectory = null)
    {
        _defaultDownloadDirectory = defaultDownloadDirectory ??
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "WinCare-Transfers");
    }

    /// <summary>
    /// Adds and starts a torrent transfer from a Magnet URI.
    /// </summary>
    public Task<TorrentSessionSnapshot?> AddMagnetAsync(string magnetUri, string? destinationDirectory = null)
    {
        // No tracker, peer-wire, piece-verification or persisted transfer engine is wired here.
        // Never manufacture progress, allocate files or present a synthetic swarm as genuine.
        return Task.FromResult<TorrentSessionSnapshot?>(null);
    }

    public Task<TorrentSessionSnapshot?> AddTorrentFileAsync(byte[] torrentBytes, string? destinationDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(torrentBytes);
        return Task.FromResult<TorrentSessionSnapshot?>(null);
    }

    public bool PauseSession(string infoHashHex)
    {
        if (_sessions.TryGetValue(infoHashHex, out TorrentSession? session))
        {
            session.Pause();
            return true;
        }
        return false;
    }

    public bool ResumeSession(string infoHashHex)
    {
        if (_sessions.TryGetValue(infoHashHex, out TorrentSession? session))
        {
            session.Resume();
            return true;
        }
        return false;
    }

    public async Task<bool> RemoveSessionAsync(string infoHashHex)
    {
        if (_sessions.TryRemove(infoHashHex, out TorrentSession? session))
        {
            await session.DisposeAsync().ConfigureAwait(false);
            return true;
        }
        return false;
    }

    public IReadOnlyList<TorrentSessionSnapshot> GetActiveSnapshots()
    {
        return _sessions.Values.Select(s => s.CreateSnapshot()).ToList().AsReadOnly();
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var session in _sessions.Values)
        {
            await session.DisposeAsync().ConfigureAwait(false);
        }
        _sessions.Clear();
    }
}
