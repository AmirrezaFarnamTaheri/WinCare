using System.Text.Json;
using WinCare.Infrastructure.Commands;
using Xunit;

namespace WinCare.Infrastructure.Tests;

/// <summary>
/// Regression tests for the bounded state read. A state value that exceeds the safety limit must
/// be refused with InvalidDataException instead of being fully buffered into memory; values under
/// the limit continue to round-trip unchanged.
/// </summary>
public sealed class CommandStateStoreBoundsTests : IDisposable
{
    private readonly string _root;
    private const int MaxStateBytes = 16 * 1024 * 1024;

    public CommandStateStoreBoundsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "wincare-bounds-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task Oversized_state_is_rejected_instead_of_fully_loaded()
    {
        var store = new CommandStateStore(_root);

        // Write a legitimately large but under-limit value so the write path itself is exercised,
        // then overwrite the file directly with an over-limit blob to model external growth or a
        // corrupted/truncated commit the store never sanctioned.
        var small = JsonDocument.Parse("""{"ok": true}""").RootElement.Clone();
        await store.WriteAsync("big", small, CancellationToken.None);

        string path = Path.Combine(_root, "big.json");
        await File.WriteAllBytesAsync(path, new byte[MaxStateBytes + 4096], CancellationToken.None);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            store.ReadAsync("big", small, CancellationToken.None));
    }

    [Fact]
    public async Task Bounded_values_round_trip_unchanged()
    {
        var store = new CommandStateStore(_root);
        var value = JsonDocument.Parse("""{"sequence": [1, 2, 3], "label": "state"}""").RootElement.Clone();

        await store.WriteAsync("roundtrip", value, CancellationToken.None);
        var read = await store.ReadAsync("roundtrip", value, CancellationToken.None);

        Assert.Equal("state", read.GetProperty("label").GetString());
        Assert.Equal(new[] { 1, 2, 3 }, read.GetProperty("sequence").EnumerateArray().Select(e => e.GetInt32()).ToArray());
    }

    [Fact]
    public async Task Oversized_state_is_rejected_by_update_instead_of_fully_loaded()
    {
        var store = new CommandStateStore(_root);

        // UpdateAsync reads the existing file before transforming it, so the same unbounded
        // deserialization path ReadAsync guards must be guarded here too. Most state mutations
        // flow through this method, so an unbounded parse would defeat the bound entirely.
        var small = JsonDocument.Parse("""{"ok": true}""").RootElement.Clone();
        await store.WriteAsync("big", small, CancellationToken.None);

        string path = Path.Combine(_root, "big.json");
        await File.WriteAllBytesAsync(path, new byte[MaxStateBytes + 4096], CancellationToken.None);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            store.UpdateAsync("big", small, _ => small, CancellationToken.None));
    }
}
