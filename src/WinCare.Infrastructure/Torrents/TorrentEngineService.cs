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
    public async Task<TorrentSessionSnapshot?> AddMagnetAsync(string magnetUri, string? destinationDirectory = null)
    {
        if (!MagnetUriParser.TryParse(magnetUri, out TorrentMetadata? metadata) || metadata is null)
        {
            return null;
        }

        // Default mock synthetic size of 4.5 GB for sample magnet if 0
        if (metadata.TotalBytes == 0)
        {
            metadata = metadata with
            {
                TotalBytes = 4831838208L, // 4.5 GB
                PieceLength = 2097152,    // 2 MB pieces
                PieceCount = 2304
            };
        }

        string targetDir = destinationDirectory ?? _defaultDownloadDirectory;
        var session = new TorrentSession(metadata, targetDir);

        if (_sessions.TryAdd(metadata.InfoHashHex, session))
        {
            await session.StartAsync().ConfigureAwait(false);
            return session.CreateSnapshot();
        }

        return _sessions[metadata.InfoHashHex].CreateSnapshot();
    }

    /// <summary>
    /// Adds and starts a torrent transfer from raw .torrent file bytes.
    /// </summary>
    public async Task<TorrentSessionSnapshot?> AddTorrentFileAsync(byte[] torrentBytes, string? destinationDirectory = null)
    {
        try
        {
            TorrentMetadata metadata = BencodeDecoder.DecodeTorrentFile(torrentBytes);
            string targetDir = destinationDirectory ?? _defaultDownloadDirectory;
            var session = new TorrentSession(metadata, targetDir);

            if (_sessions.TryAdd(metadata.InfoHashHex, session))
            {
                await session.StartAsync().ConfigureAwait(false);
                return session.CreateSnapshot();
            }

            return _sessions[metadata.InfoHashHex].CreateSnapshot();
        }
        catch
        {
            return null;
        }
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
