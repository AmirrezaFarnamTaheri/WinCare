using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using WinCare.Application.Transfers;

namespace WinCare.Infrastructure.Torrents;

/// <summary>
/// High-throughput, robust Bencode parser and Torrent metadata extractor.
/// Implements standard BitTorrent BEP 0003 encoding rules with zero allocations where possible.
/// </summary>
public static class BencodeDecoder
{
    public static TorrentMetadata DecodeTorrentFile(byte[] bencodeBytes)
    {
        ArgumentNullException.ThrowIfNull(bencodeBytes);
        if (bencodeBytes.Length < 11)
        {
            throw new FormatException("Torrent file is too small to contain valid metadata.");
        }

        int index = 0;
        object root = DecodeElement(bencodeBytes, ref index);
        if (root is not Dictionary<string, object> rootDict)
        {
            throw new FormatException("Torrent root must be a Bencode dictionary.");
        }

        if (!rootDict.TryGetValue("info", out object? infoObj) || infoObj is not Dictionary<string, object> infoDict)
        {
            throw new FormatException("Torrent missing required 'info' dictionary.");
        }

        // Compute info_hash (SHA-1 over raw bencoded info dictionary bytes)
        byte[] infoHashBytes = ExtractAndHashInfoDict(bencodeBytes);
        string infoHashHex = Convert.ToHexString(infoHashBytes).ToLowerInvariant();

        // Extract Name
        string name = "Unnamed Torrent";
        if (infoDict.TryGetValue("name", out object? nameObj))
        {
            name = GetString(nameObj);
        }

        // Extract Piece Length
        int pieceLength = 262144; // default 256KB
        if (infoDict.TryGetValue("piece length", out object? pieceLenObj) && pieceLenObj is long pl)
        {
            pieceLength = (int)pl;
        }

        // Extract Pieces byte array (concatenated 20-byte SHA-1 hashes)
        var pieceHashes = new List<byte[]>();
        if (infoDict.TryGetValue("pieces", out object? piecesObj) && piecesObj is byte[] piecesBytes)
        {
            int numPieces = piecesBytes.Length / 20;
            for (int i = 0; i < numPieces; i++)
            {
                byte[] hash = new byte[20];
                Buffer.BlockCopy(piecesBytes, i * 20, hash, 0, 20);
                pieceHashes.Add(hash);
            }
        }

        // Extract Files
        var files = new List<TorrentFileEntry>();
        long totalBytes = 0;

        if (infoDict.TryGetValue("files", out object? filesObj) && filesObj is List<object> filesList)
        {
            // Multi-file torrent
            long currentOffset = 0;
            foreach (object item in filesList)
            {
                if (item is Dictionary<string, object> fileDict &&
                    fileDict.TryGetValue("length", out object? lenObj) && lenObj is long length &&
                    fileDict.TryGetValue("path", out object? pathObj) && pathObj is List<object> pathParts)
                {
                    var pathSegments = new List<string>();
                    foreach (object part in pathParts)
                    {
                        pathSegments.Add(GetString(part));
                    }
                    string relPath = Path.Combine(pathSegments.ToArray());
                    files.Add(new TorrentFileEntry(relPath, length, currentOffset));
                    currentOffset += length;
                    totalBytes += length;
                }
            }
        }
        else if (infoDict.TryGetValue("length", out object? singleLenObj) && singleLenObj is long singleLen)
        {
            // Single-file torrent
            files.Add(new TorrentFileEntry(name, singleLen, 0));
            totalBytes = singleLen;
        }

        // Extract Trackers
        var trackers = new List<string>();
        if (rootDict.TryGetValue("announce", out object? announceObj))
        {
            trackers.Add(GetString(announceObj));
        }
        if (rootDict.TryGetValue("announce-list", out object? announceListObj) && announceListObj is List<object> annList)
        {
            foreach (object tier in annList)
            {
                if (tier is List<object> subList)
                {
                    foreach (object trk in subList)
                    {
                        string t = GetString(trk);
                        if (!trackers.Contains(t)) trackers.Add(t);
                    }
                }
            }
        }

        return new TorrentMetadata(
            InfoHashHex: infoHashHex,
            Name: name,
            TotalBytes: totalBytes,
            PieceLength: pieceLength,
            PieceCount: pieceHashes.Count,
            PieceHashes: pieceHashes.AsReadOnly(),
            Files: files.AsReadOnly(),
            Trackers: trackers.AsReadOnly());
    }

    private static byte[] ExtractAndHashInfoDict(byte[] bencodeBytes)
    {
        // Traverse the root dictionary's keys. Looking for the raw substring "4:info"
        // can match bytes inside an unrelated string and produce an incorrect info hash.
        if (bencodeBytes.Length == 0 || bencodeBytes[0] != (byte)'d')
            throw new FormatException("Torrent root must be a dictionary.");

        byte[]? infoHash = null;
        int index = 1;
        while (index < bencodeBytes.Length && bencodeBytes[index] != (byte)'e')
        {
            object key = DecodeElement(bencodeBytes, ref index);
            if (key is not byte[] keyBytes)
                throw new FormatException("Torrent dictionary keys must be byte strings.");

            int start = index;
            SkipElement(bencodeBytes, ref index);
            if (keyBytes.AsSpan().SequenceEqual("info"u8))
            {
                if (infoHash is not null)
                    throw new FormatException("Torrent contains duplicate root 'info' keys.");
                if (start >= bencodeBytes.Length || bencodeBytes[start] != (byte)'d')
                    throw new FormatException("Torrent info must be a dictionary.");
                infoHash = SHA1.HashData(bencodeBytes.AsSpan(start, index - start));
            }
        }

        return infoHash ?? throw new FormatException("Torrent missing root 'info' dictionary.");
    }

    private static void SkipElement(byte[] data, ref int index, int depth = 0)
    {
        if (depth > 64 || index >= data.Length)
            throw new FormatException("Malformed or excessively nested bencode.");

        byte b = data[index];
        if (b == (byte)'i')
        {
            int end = Array.IndexOf(data, (byte)'e', index + 1);
            if (end < 0) throw new FormatException("Unterminated bencode integer.");
            index = end + 1;
        }
        else if (b == (byte)'l' || b == (byte)'d')
        {
            index++;
            while (index < data.Length && data[index] != (byte)'e')
                SkipElement(data, ref index, depth + 1);
            if (index >= data.Length) throw new FormatException("Unterminated bencode collection.");
            index++;
        }
        else if (char.IsAsciiDigit((char)b))
        {
            int colon = Array.IndexOf(data, (byte)':', index);
            if (colon < 0 ||
                !int.TryParse(Encoding.ASCII.GetString(data, index, colon - index), out int length) ||
                length < 0 || (long)colon + 1 + length > data.Length)
                throw new FormatException("Malformed bencode byte string.");
            index = colon + 1 + length;
        }
        else
        {
            throw new FormatException($"Unknown bencode prefix at offset {index}.");
        }
    }

    private static object DecodeElement(byte[] data, ref int index)
    {
        if (index >= data.Length) throw new FormatException("Unexpected end of bencoded stream.");
        byte b = data[index];

        if (b == (byte)'i')
        {
            index++;
            int end = Array.IndexOf(data, (byte)'e', index);
            if (end < 0) throw new FormatException("Unterminated integer.");
            string numStr = Encoding.ASCII.GetString(data, index, end - index);
            index = end + 1;
            return long.Parse(numStr);
        }
        if (b == (byte)'l')
        {
            index++;
            var list = new List<object>();
            while (index < data.Length && data[index] != (byte)'e')
            {
                list.Add(DecodeElement(data, ref index));
            }
            if (index < data.Length && data[index] == (byte)'e') index++;
            return list;
        }
        if (b == (byte)'d')
        {
            index++;
            var dict = new Dictionary<string, object>(StringComparer.Ordinal);
            while (index < data.Length && data[index] != (byte)'e')
            {
                string key = GetString(DecodeElement(data, ref index));
                object val = DecodeElement(data, ref index);
                dict[key] = val;
            }
            if (index < data.Length && data[index] == (byte)'e') index++;
            return dict;
        }
        if (char.IsAsciiDigit((char)b))
        {
            int colon = Array.IndexOf(data, (byte)':', index);
            if (colon < 0) throw new FormatException("Malformed bencode string length.");
            string lenStr = Encoding.ASCII.GetString(data, index, colon - index);
            int len = int.Parse(lenStr);
            index = colon + 1;
            byte[] bytes = new byte[len];
            Buffer.BlockCopy(data, index, bytes, 0, len);
            index += len;
            return bytes;
        }

        throw new FormatException($"Unknown bencode prefix '{(char)b}' at offset {index}.");
    }

    private static string GetString(object obj)
    {
        return obj switch
        {
            byte[] b => Encoding.UTF8.GetString(b),
            string s => s,
            _ => obj.ToString() ?? string.Empty
        };
    }
}
