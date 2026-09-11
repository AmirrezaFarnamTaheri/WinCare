using System.Text.Json;
using WinCare.Infrastructure.Commands;
using Xunit;

namespace WinCare.Infrastructure.Tests;

/// <summary>
/// Regression tests for F-008: shared JSON exports failed because the temp file's
/// exclusive-write stream was still open when File.Move replaced the destination.
/// </summary>
public sealed class JsonExportStreamTests
{
    private static readonly JsonElement Payload =
        JsonSerializer.SerializeToElement(new[] { new { id = "one", value = 1 }, new { id = "two", value = 2 } });

    private static string[] TempFiles(string directory) =>
        Directory.GetFiles(directory, "*.tmp", SearchOption.TopDirectoryOnly);

    [Fact]
    public async Task Export_to_new_destination_succeeds_and_leaves_no_temp_files()
    {
        string directory = Path.Combine(Path.GetTempPath(), "wincare-f008-" + Guid.NewGuid().ToString("N"));
        try
        {
            string path = Path.Combine(directory, "widget-export.json");

            await WindowsCommandExecutor.WriteJsonExportAsync(path, Payload, CancellationToken.None);

            Assert.True(File.Exists(path));
            using JsonDocument parsed = JsonDocument.Parse(await File.ReadAllTextAsync(path));
            Assert.Equal(2, parsed.RootElement.GetArrayLength());
            Assert.Empty(TempFiles(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Export_replaces_existing_destination_and_leaves_no_temp_files()
    {
        string directory = Path.Combine(Path.GetTempPath(), "wincare-f008-" + Guid.NewGuid().ToString("N"));
        try
        {
            string path = Path.Combine(directory, "widget-export.json");
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(path, "stale-content");

            await WindowsCommandExecutor.WriteJsonExportAsync(path, Payload, CancellationToken.None);

            using JsonDocument parsed = JsonDocument.Parse(await File.ReadAllTextAsync(path));
            Assert.Equal(2, parsed.RootElement.GetArrayLength());
            Assert.Empty(TempFiles(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Cancelled_export_preserves_existing_destination_and_cleans_temp()
    {
        string directory = Path.Combine(Path.GetTempPath(), "wincare-f008-" + Guid.NewGuid().ToString("N"));
        try
        {
            string path = Path.Combine(directory, "widget-export.json");
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(path, "previous-good-content");
            using CancellationTokenSource cancelled = new();
            cancelled.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => WindowsCommandExecutor.WriteJsonExportAsync(path, Payload, cancelled.Token));

            Assert.Equal("previous-good-content", await File.ReadAllTextAsync(path));
            Assert.Empty(TempFiles(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
