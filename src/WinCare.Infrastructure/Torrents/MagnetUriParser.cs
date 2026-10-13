using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using WinCare.Application.Transfers;

namespace WinCare.Infrastructure.Torrents;

/// <summary>
/// Parser and validator for BitTorrent Magnet URIs (BEP 0009).
/// </summary>
public static class MagnetUriParser
{
    public static bool TryParse(string magnetUri, out TorrentMetadata? metadata)
    {
        metadata = null;
        if (string.IsNullOrWhiteSpace(magnetUri)) return false;
        string trimmed = magnetUri.Trim();
        if (!trimmed.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase)) return false;

        string query = trimmed["magnet:?".Length..];
        string[] parts = query.Split('&', StringSplitOptions.RemoveEmptyEntries);

        string? infoHashHex = null;
        string? displayName = null;
        var trackers = new List<string>();

        foreach (string part in parts)
        {
            int eq = part.IndexOf('=');
            if (eq <= 0) continue;

            string key = part[..eq].Trim();
            string val = WebUtility.UrlDecode(part[(eq + 1)..].Trim());

            if (key.Equals("xt", StringComparison.OrdinalIgnoreCase))
            {
                if (val.StartsWith("urn:btih:", StringComparison.OrdinalIgnoreCase))
                {
                    string rawHash = val["urn:btih:".Length..];
                    if (rawHash.Length == 40)
                    {
                        infoHashHex = rawHash.ToLowerInvariant();
                    }
                    else if (rawHash.Length == 32)
                    {
                        // Base32 encoded hash
                        infoHashHex = Base32ToHex(rawHash);
                    }
                }
            }
            else if (key.Equals("dn", StringComparison.OrdinalIgnoreCase))
            {
                displayName = val;
            }
            else if (key.Equals("tr", StringComparison.OrdinalIgnoreCase))
            {
                if (!trackers.Contains(val)) trackers.Add(val);
            }
        }

        if (string.IsNullOrEmpty(infoHashHex)) return false;

        displayName ??= $"Magnet-{infoHashHex[..8]}";

        metadata = new TorrentMetadata(
            InfoHashHex: infoHashHex,
            Name: displayName,
            TotalBytes: 0, // Inferred upon metadata exchange
            PieceLength: 262144,
            PieceCount: 0,
            PieceHashes: Array.Empty<byte[]>(),
            Files: [new TorrentFileEntry(displayName, 0, 0)],
            Trackers: trackers.AsReadOnly());

        return true;
    }

    private static string Base32ToHex(string base32)
    {
        const string b32Chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bits = new StringBuilder();
        foreach (char c in base32.ToUpperInvariant())
        {
            int val = b32Chars.IndexOf(c);
            if (val >= 0)
            {
                bits.Append(Convert.ToString(val, 2).PadLeft(5, '0'));
            }
        }

        string bitStr = bits.ToString();
        var bytes = new List<byte>();
        for (int i = 0; i + 8 <= bitStr.Length; i += 8)
        {
            bytes.Add(Convert.ToByte(bitStr.Substring(i, 8), 2));
        }

        return Convert.ToHexString(bytes.ToArray()).ToLowerInvariant();
    }
}
