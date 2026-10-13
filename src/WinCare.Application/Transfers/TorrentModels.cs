using System;
using System.Collections.Generic;

namespace WinCare.Application.Transfers;

/// <summary>
/// Status lifecycle of a BitTorrent transfer session.
/// </summary>
public enum TorrentStatus
{
    Queued,
    Checking,
    Downloading,
    Seeding,
    Paused,
    Completed,
    Error
}

/// <summary>
/// State of an individual chunk/piece in the swarm piece bitfield.
/// </summary>
public enum TorrentPieceState
{
    Missing = 0,
    InFlight = 1,
    Verified = 2
}

/// <summary>
/// Single file entry inside a multi-file or single-file torrent payload.
/// </summary>
public sealed record TorrentFileEntry(
    string RelativePath,
    long Length,
    long Offset);

/// <summary>
/// Cryptographic and topological metadata for a BitTorrent swarm.
/// </summary>
public sealed record TorrentMetadata(
    string InfoHashHex,
    string Name,
    long TotalBytes,
    int PieceLength,
    int PieceCount,
    IReadOnlyList<byte[]> PieceHashes,
    IReadOnlyList<TorrentFileEntry> Files,
    IReadOnlyList<string> Trackers)
{
    public string FormattedTotalSize
    {
        get
        {
            if (TotalBytes < 1024) return $"{TotalBytes} B";
            if (TotalBytes < 1024 * 1024) return $"{TotalBytes / 1024.0:F1} KB";
            if (TotalBytes < 1024 * 1024 * 1024) return $"{TotalBytes / (1024.0 * 1024.0):F2} MB";
            return $"{TotalBytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
        }
    }
}

/// <summary>
/// Real-time peer telemetry inside a swarm mesh.
/// </summary>
public sealed record PeerTelemetry(
    string Endpoint,
    string ClientName,
    string Flags,
    double DownloadRateBytesPerSec,
    double UploadRateBytesPerSec)
{
    public string DownloadSpeedFormatted => FormatSpeed(DownloadRateBytesPerSec);
    public string UploadSpeedFormatted => FormatSpeed(UploadRateBytesPerSec);

    private static string FormatSpeed(double bytesPerSec)
    {
        if (bytesPerSec < 1024) return $"{bytesPerSec:F0} B/s";
        if (bytesPerSec < 1024 * 1024) return $"{bytesPerSec / 1024.0:F1} KB/s";
        return $"{bytesPerSec / (1024.0 * 1024.0):F1} MB/s";
    }
}

/// <summary>
/// Comprehensive observable snapshot of a BitTorrent transfer session.
/// </summary>
public sealed record TorrentSessionSnapshot(
    string InfoHashHex,
    string Name,
    TorrentStatus Status,
    long TotalBytes,
    long DownloadedBytes,
    long UploadedBytes,
    double DownloadRateBytesPerSec,
    double UploadRateBytesPerSec,
    int ConnectedPeers,
    int TotalSwarmPeers,
    int VerifiedPieces,
    int TotalPieces,
    IReadOnlyList<TorrentPieceState> PieceStates,
    IReadOnlyList<PeerTelemetry> Peers,
    string DestinationDirectory,
    DateTimeOffset CreatedAt)
{
    public double ProgressPercentage => TotalBytes > 0 ? Math.Clamp((double)DownloadedBytes / TotalBytes * 100.0, 0.0, 100.0) : 0.0;

    public string ProgressFormatted => TotalBytes > 0
        ? $"{FormatBytes(DownloadedBytes)} / {FormatBytes(TotalBytes)} ({ProgressPercentage:F1}%)"
        : $"{FormatBytes(DownloadedBytes)}";

    public string DownloadRateFormatted => FormatSpeed(DownloadRateBytesPerSec);
    public string UploadRateFormatted => FormatSpeed(UploadRateBytesPerSec);

    public string EtaFormatted
    {
        get
        {
            if (Status is TorrentStatus.Completed or TorrentStatus.Seeding) return "Done";
            if (Status is TorrentStatus.Paused) return "Paused";
            if (DownloadRateBytesPerSec <= 1024) return "\u221E"; // Infinity symbol

            long remainingBytes = Math.Max(0, TotalBytes - DownloadedBytes);
            double secondsRemaining = remainingBytes / DownloadRateBytesPerSec;
            if (secondsRemaining > 86400 * 7) return "> 7 days";

            var timeSpan = TimeSpan.FromSeconds(secondsRemaining);
            return timeSpan.TotalHours >= 1
                ? $"{(int)timeSpan.TotalHours}h {timeSpan.Minutes:D2}m"
                : $"{timeSpan.Minutes}m {timeSpan.Seconds:D2}s";
        }
    }

    public string StatusText => Status switch
    {
        TorrentStatus.Queued => "Queued",
        TorrentStatus.Checking => "Verifying Pieces",
        TorrentStatus.Downloading => "Downloading",
        TorrentStatus.Seeding => "Seeding",
        TorrentStatus.Paused => "Paused",
        TorrentStatus.Completed => "Completed",
        TorrentStatus.Error => "Error",
        _ => "Idle"
    };

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):F2} MB";
        return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
    }

    private static string FormatSpeed(double bytesPerSec)
    {
        if (bytesPerSec < 1024) return $"{bytesPerSec:F0} B/s";
        if (bytesPerSec < 1024 * 1024) return $"{bytesPerSec / 1024.0:F1} KB/s";
        return $"{bytesPerSec / (1024.0 * 1024.0):F1} MB/s";
    }
}
