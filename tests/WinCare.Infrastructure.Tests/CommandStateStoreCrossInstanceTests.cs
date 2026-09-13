using System.Text.Json;
using WinCare.Infrastructure.Commands;
using Xunit;

namespace WinCare.Infrastructure.Tests;

/// <summary>
/// Regression tests for cross-instance state store concurrency.
/// </summary>
public sealed class CommandStateStoreCrossInstanceTests : IDisposable
{
    private readonly string _root;

    public CommandStateStoreCrossInstanceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "wincare-f011-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public async Task Independent_instances_serializing_updates_do_not_lose_records()
    {
        const int writersPerInstance = 25;
        var instanceA = new CommandStateStore(_root);
        var instanceB = new CommandStateStore(_root);

        async Task Writer(CommandStateStore store, int start)
        {
            for (int i = 0; i < writersPerInstance; i++)
            {
                string id = $"record-{start + i}";
                await store.UpdateAsync(
                    "shared",
                    Parse("[]"),
                    current =>
                    {
                        var list = current.EnumerateArray().Select(x => x.Clone()).ToList();
                        list.Add(Parse($$"""{"id": "{{id}}"}"""));
                        return Parse(JsonSerializer.Serialize(list));
                    },
                    CancellationToken.None);
            }
        }

        Task[] tasks =
        [
            Writer(instanceA, 0),
            Writer(instanceA, writersPerInstance),
            Writer(instanceB, 2 * writersPerInstance),
            Writer(instanceB, 3 * writersPerInstance),
        ];
        await Task.WhenAll(tasks);

        JsonElement final = await instanceA.ReadAsync("shared", Parse("[]"), CancellationToken.None);
        var ids = final.EnumerateArray()
            .Select(item => item.GetProperty("id").GetString())
            .ToHashSet();

        Assert.Equal(4 * writersPerInstance, final.GetArrayLength());
        for (int i = 0; i < 4 * writersPerInstance; i++)
        {
            Assert.Contains($"record-{i}", ids);
        }
    }

    [Fact]
    public async Task Store_instances_share_the_same_os_lock_name_for_one_root()
    {
        // Both instances resolve the same lock name, which is what makes the write
        // protocol effective across processes.
        var a = new CommandStateStore(_root);
        var b = new CommandStateStore(_root);
        string rootA = a.Root;
        string rootB = b.Root;

        Assert.Equal(rootA, rootB);
        await a.WriteAsync("probe", Parse("{\"v\":1}"), CancellationToken.None);
        JsonElement read = await b.ReadAsync("probe", Parse("null"), CancellationToken.None);
        Assert.Equal(1, read.GetProperty("v").GetInt32());
    }
}
