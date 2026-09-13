using System.Text.Json;
using WinCare.Application.Commands;
using WinCare.Domain.Commands;
using WinCare.Infrastructure.Commands;
using Xunit;

namespace WinCare.Infrastructure.Tests;

public sealed class Winapp2CleanerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly WindowsCommandExecutor _executor;

    public Winapp2CleanerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"wincare_winapp2_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _executor = new WindowsCommandExecutor();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { }
        }
    }

    private static CommandParameters Params(object data)
    {
        return new CommandParameters(JsonSerializer.SerializeToElement(data));
    }

    [Fact]
    public void Winapp2Admission_admits_valid_ini_file()
    {
        string iniPath = Path.Combine(_tempDir, "test_cleaner.ini");
        File.WriteAllText(iniPath, "[TestCleaner]\nFileKey1=%Temp%|*.tmp\nExcludeKey1=%Temp%|keep.tmp\n");

        var method = typeof(WindowsCommandExecutor).GetMethod(
            "Winapp2Admission",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        var outcome = (CommandHandlerOutcome)method!.Invoke(_executor, [Params(new { Path = iniPath })])!;

        Assert.Equal(CommandResultStatus.Succeeded, outcome.Status);
        Assert.NotNull(outcome.Data);
        Assert.True(outcome.Data.Value.TryGetProperty("admitted", out JsonElement admitted));
        Assert.True(admitted.GetBoolean());
    }

    [Fact]
    public void Winapp2Admission_rejects_file_with_executable_command_directive()
    {
        string iniPath = Path.Combine(_tempDir, "malicious_cleaner.ini");
        File.WriteAllText(iniPath, "[UnsafeCleaner]\nRun=calc.exe\nFileKey1=%Temp%|*.tmp\n");

        var method = typeof(WindowsCommandExecutor).GetMethod(
            "Winapp2Admission",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        var outcome = (CommandHandlerOutcome)method!.Invoke(_executor, [Params(new { Path = iniPath })])!;

        Assert.Equal(CommandResultStatus.Succeeded, outcome.Status);
        Assert.NotNull(outcome.Data);
        Assert.True(outcome.Data.Value.TryGetProperty("admitted", out JsonElement admitted));
        Assert.False(admitted.GetBoolean());
    }

    [Fact]
    public void CleanerWinapp2Run_respects_exclude_key_patterns()
    {
        string tempRoot = Path.GetTempPath();
        string testFileName = $"wincare_exclude_test_{Guid.NewGuid():N}.tmp";
        string excludedFileName = $"wincare_exclude_keep_{Guid.NewGuid():N}.tmp";
        string testFile = Path.Combine(tempRoot, testFileName);
        string excludedFile = Path.Combine(tempRoot, excludedFileName);

        File.WriteAllText(testFile, "test data");
        File.WriteAllText(excludedFile, "keep data");

        try
        {
            string iniPath = Path.Combine(_tempDir, "exclude_test.ini");
            File.WriteAllText(iniPath, $"[ExcludeTest]\nFileKey1=%Temp%|*.tmp\nExcludeKey1=%Temp%|{excludedFileName}\n");

            var method = typeof(WindowsCommandExecutor).GetMethod(
                "CleanerWinapp2Run",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            var outcome = (CommandHandlerOutcome)method!.Invoke(_executor, [Params(new { Path = iniPath }), CancellationToken.None])!;

            Assert.Equal(CommandResultStatus.Succeeded, outcome.Status);
            Assert.False(File.Exists(testFile));
            Assert.True(File.Exists(excludedFile));
        }
        finally
        {
            try { if (File.Exists(testFile)) File.Delete(testFile); } catch { }
            try { if (File.Exists(excludedFile)) File.Delete(excludedFile); } catch { }
        }
    }

    [Fact]
    public void Winapp2Admission_resolves_categories_and_special_detect_codes()
    {
        string iniPath = Path.Combine(_tempDir, "categories_test.ini");
        File.WriteAllText(iniPath, "[ChromeCleaner]\nLangSecRef=3029\nSpecialDetect=DET_CHROME\nFileKey1=%Temp%|*.tmp\n[EdgeCleaner]\nLangSecRef=3006\nSpecialDetect=DET_EDGE\nFileKey1=%Temp%|*.log\n");

        var method = typeof(WindowsCommandExecutor).GetMethod(
            "Winapp2Admission",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        var outcome = (CommandHandlerOutcome)method!.Invoke(_executor, [Params(new { Path = iniPath })])!;

        Assert.Equal(CommandResultStatus.Succeeded, outcome.Status);
        Assert.NotNull(outcome.Data);
        Assert.True(outcome.Data.Value.TryGetProperty("categories", out JsonElement cats));
        Assert.Contains(cats.EnumerateArray(), c => c.GetString() == "Google Chrome");
        Assert.Contains(cats.EnumerateArray(), c => c.GetString() == "Microsoft Edge");
        Assert.True(outcome.Data.Value.TryGetProperty("specialDetectCodes", out JsonElement specs));
        Assert.Contains(specs.EnumerateArray(), s => s.GetString() == "DET_CHROME");
        Assert.Contains(specs.EnumerateArray(), s => s.GetString() == "DET_EDGE");
    }
}
