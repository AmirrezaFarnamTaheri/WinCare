using System.Text.Json;
using WinCare.Infrastructure.Commands;
using Xunit;

namespace WinCare.Infrastructure.Tests;

/// <summary>
/// Regression tests for cleaner root resolution and preview parity.
/// </summary>
public sealed class CleanerPreviewTests
{
    private static JsonElement PreviewAsJson(string commandId, object parameters)
    {
        var p = new CommandParameters(JsonSerializer.SerializeToElement(parameters));
        object resources = WindowsCommandExecutor.GetAffectedResourcesForPreview(commandId, p);
        return JsonSerializer.SerializeToElement(resources);
    }

    [Fact]
    public void Cleanup_temp_roots_are_deduplicated_after_normalization()
    {
        string[] roots = WindowsCommandExecutor.CleanupTempRoots();

        Assert.NotEmpty(roots);
        for (int i = 0; i < roots.Length; i++)
        {
            for (int j = i + 1; j < roots.Length; j++)
            {
                string left = roots[i].TrimEnd('\\', '/');
                string right = roots[j].TrimEnd('\\', '/');
                Assert.NotEqual(left, right); // Distinct() default comparer is case-sensitive on Linux but this test runs on Windows
            }
        }
    }

    [Fact]
    public void Cleaner_disk_pressure_preview_names_exactly_the_execution_roots()
    {
        JsonElement preview = PreviewAsJson("cleaner-disk-pressure", new { OlderThanDays = 7 });

        Assert.Equal(1, preview.GetArrayLength());
        string path = preview[0].GetProperty("path").GetString()!;
        string[] previewRoots = path.Split(", ");

        string[] executionRoots = WindowsCommandExecutor.CleanupTempRoots();
        Assert.Equal(executionRoots, previewRoots);

        // The previously fabricated roots must not reappear.
        Assert.DoesNotContain(previewRoots, root => root.Contains("WINDIR", StringComparison.OrdinalIgnoreCase) || root.EndsWith("Windows\\Temp", StringComparison.OrdinalIgnoreCase));
        // The actually executed %LOCALAPPDATA%\Temp root must be present.
        string localTemp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp").TrimEnd('\\');
        Assert.Contains(previewRoots, root => root.TrimEnd('\\').Equals(localTemp, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Cleaner_disk_pressure_preview_declares_its_exclusions()
    {
        JsonElement preview = PreviewAsJson("cleaner-disk-pressure", new { OlderThanDays = 3 });

        JsonElement exclusions = preview[0].GetProperty("exclusions");
        Assert.Equal(JsonValueKind.Array, exclusions.ValueKind);
        Assert.True(exclusions.GetArrayLength() > 0);
        Assert.Equal(3, preview[0].GetProperty("olderThanDays").GetInt32());
    }

    [Theory]
    [InlineData("temp")]
    [InlineData("TEMP")]
    public void Deep_clean_temp_profile_preview_uses_the_shared_root_plan(string profile)
    {
        JsonElement preview = PreviewAsJson("deep-clean", new { ProfileId = profile });

        string path = preview[0].GetProperty("path").GetString()!;
        Assert.Equal(WindowsCommandExecutor.CleanupTempRoots(), path.Split(", "));
    }

    [Fact]
    public void Deep_clean_wincare_cache_profile_preview_names_the_actual_cache_directory()
    {
        JsonElement preview = PreviewAsJson("deep-clean", new { ProfileId = "wincare-cache" });

        string path = preview[0].GetProperty("path").GetString()!;
        string cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinCare", "cache");
        Assert.Equal(cache, path);
    }

    [Fact]
    public void Preset_preview_expands_the_resolved_rules_and_plan_digest()
    {
        JsonElement preview = PreviewAsJson("preset", new { PresetId = "safe" });

        Assert.Equal(3, preview.GetArrayLength());
        Assert.Equal("explorer.show-extensions", preview[0].GetProperty("path").GetString());
        Assert.Equal("RemediationRule", preview[0].GetProperty("resourceType").GetString());
        Assert.True(preview[0].GetProperty("changes").GetArrayLength() > 0);
        string digest = preview[0].GetProperty("planDigest").GetString()!;
        Assert.Equal(64, digest.Length);
        Assert.Equal(digest, preview[1].GetProperty("planDigest").GetString());
    }
}
