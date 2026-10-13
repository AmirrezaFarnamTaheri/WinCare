using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using WinCare.Application.Transfers;
using WinCare.Infrastructure.Torrents;
using Xunit;

namespace WinCare.Infrastructure.Tests;

public sealed class BitTorrentTransferEngineTests
{
    [Fact]
    public void MagnetUriParser_parses_valid_hex_magnet_uri()
    {
        string uri = "magnet:?xt=urn:btih:da39a3ee5e6b4b0d3255bfef95601890afd80709&dn=Ubuntu-24.04-desktop-amd64.iso&tr=udp%3A%2F%2Ftracker.opentrackr.org%3A1337%2Fannounce";

        bool ok = MagnetUriParser.TryParse(uri, out TorrentMetadata? metadata);

        Assert.True(ok);
        Assert.NotNull(metadata);
        Assert.Equal("da39a3ee5e6b4b0d3255bfef95601890afd80709", metadata.InfoHashHex);
        Assert.Equal("Ubuntu-24.04-desktop-amd64.iso", metadata.Name);
        Assert.Contains("udp://tracker.opentrackr.org:1337/announce", metadata.Trackers);
    }

    [Fact]
    public void MagnetUriParser_rejects_invalid_uri()
    {
        Assert.False(MagnetUriParser.TryParse("https://example.com/file.iso", out _));
        Assert.False(MagnetUriParser.TryParse("magnet:?dn=NoHashTorrent", out _));
        Assert.False(MagnetUriParser.TryParse("", out _));
    }

    [Fact]
    public void BencodeDecoder_decodes_single_file_torrent()
    {
        // Build a synthetic bencoded torrent dictionary:
        // d8:announce26:http://tracker.example.com4:infod6:lengthi1048576e4:name10:sample.bin12:piece lengthi262144e6:pieces20:12345678901234567890ee
        string raw = "d8:announce26:http://tracker.example.com4:infod6:lengthi1048576e4:name10:sample.bin12:piece lengthi262144e6:pieces20:12345678901234567890ee";
        byte[] bytes = Encoding.Latin1.GetBytes(raw);

        TorrentMetadata meta = BencodeDecoder.DecodeTorrentFile(bytes);

        Assert.NotNull(meta);
        Assert.Equal("sample.bin", meta.Name);
        Assert.Equal(1048576, meta.TotalBytes);
        Assert.Equal(262144, meta.PieceLength);
        Assert.Equal(1, meta.PieceCount);
        Assert.Single(meta.Files);
        Assert.Equal("sample.bin", meta.Files[0].RelativePath);
        Assert.NotEmpty(meta.InfoHashHex);
    }

    [Fact]
    public async Task TorrentStorageManager_verifies_piece_hash_correctly()
    {
        byte[] samplePiece = "WinCare native transfers engine high performance chunk"u8.ToArray();
        byte[] pieceHash = SHA1.HashData(samplePiece);

        var metadata = new TorrentMetadata(
            InfoHashHex: "0123456789abcdef0123456789abcdef01234567",
            Name: "test.dat",
            TotalBytes: samplePiece.Length,
            PieceLength: samplePiece.Length,
            PieceCount: 1,
            PieceHashes: [pieceHash],
            Files: [new TorrentFileEntry("test.dat", samplePiece.Length, 0)],
            Trackers: ["http://localhost"]);

        string tempDir = Path.Combine(Path.GetTempPath(), "WinCare_Torrent_Test_" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var storage = new TorrentStorageManager(metadata, tempDir);
            await storage.InitializeFilesAsync();

            // Correct piece data must verify successfully
            bool valid = await storage.WriteAndVerifyPieceAsync(0, samplePiece);
            Assert.True(valid);

            // Corrupted piece data must fail verification
            byte[] corrupted = (byte[])samplePiece.Clone();
            corrupted[0] ^= 0xFF;
            bool invalid = await storage.WriteAndVerifyPieceAsync(0, corrupted);
            Assert.False(invalid);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, recursive: true); } catch { }
            }
        }
    }
}
