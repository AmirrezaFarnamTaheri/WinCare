using WinCare.Infrastructure.Commands;
using Xunit;

namespace WinCare.Infrastructure.Tests;

public sealed class DeveloperJunkTests
{
    [Fact]
    public void DefaultCandidateRoots_IsNotEmpty()
    {
        var roots = WindowsCommandExecutor.DeveloperJunkHelper.DefaultCandidateRoots;
        Assert.NotEmpty(roots);
        Assert.Contains(roots, r => r.Contains("source", StringComparison.OrdinalIgnoreCase) || r.Contains("repos", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ScanNodeModules_WithMockDirectoryTree_FindsOnlyProjectsWithPackageJson()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), $"wincare_devjunk_test_{Guid.NewGuid():N}");
        try
        {
            // Valid project with package.json
            string validProject = Path.Combine(tempRoot, "my-app");
            string validModules = Path.Combine(validProject, "node_modules");
            Directory.CreateDirectory(validModules);
            File.WriteAllText(Path.Combine(validProject, "package.json"), "{}");
            File.WriteAllText(Path.Combine(validModules, "index.js"), "// junk");

            // Invalid directory: node_modules without package.json
            string invalidDir = Path.Combine(tempRoot, "orphan-dir");
            string invalidModules = Path.Combine(invalidDir, "node_modules");
            Directory.CreateDirectory(invalidModules);
            File.WriteAllText(Path.Combine(invalidModules, "index.js"), "// junk");

            IReadOnlyList<WindowsCommandExecutor.StaleNodeModulesCandidate> candidates =
                WindowsCommandExecutor.DeveloperJunkHelper.ScanNodeModules([tempRoot], maxDepth: 3, olderThanDays: 0);

            Assert.Single(candidates);
            Assert.Equal(validModules, candidates[0].Path);
            Assert.Equal(validProject, candidates[0].ProjectRoot);
            Assert.True(candidates[0].IsStale);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                try
                {
                    Directory.Delete(tempRoot, recursive: true);
                }
                catch
                {
                    // Best effort cleanup in test
                }
            }
        }
    }

    [Fact]
    public void ScanToolchainCaches_with_custom_targets_correctly_identifies_presence()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"wincare_toolchain_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        string nonExistentDir = Path.Combine(Path.GetTempPath(), $"wincare_missing_{Guid.NewGuid():N}");
        try
        {
            var targets = new List<(string Ecosystem, string Path)>
            {
                ("ExistingCache", tempDir),
                ("MissingCache", nonExistentDir),
            };

            var results = WindowsCommandExecutor.DeveloperJunkHelper.ScanToolchainCaches(targets);

            Assert.Equal(2, results.Count);
            Assert.Contains(results, r => r.Ecosystem == "ExistingCache" && r.Exists);
            Assert.Contains(results, r => r.Ecosystem == "MissingCache" && !r.Exists);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try
                {
                    Directory.Delete(tempDir, recursive: true);
                }
                catch
                {
                    // Best effort cleanup
                }
            }
        }
    }

    [Fact]
    public void ScanToolchainCaches_DefaultTargets_IncludesExtendedEcosystems()
    {
        var results = WindowsCommandExecutor.DeveloperJunkHelper.ScanToolchainCaches();
        Assert.Contains(results, r => r.Ecosystem == "PlatformIO-Cache");
        Assert.Contains(results, r => r.Ecosystem == "Electron-Cache");
        Assert.Contains(results, r => r.Ecosystem == "Claude-Cache");
        Assert.Contains(results, r => r.Ecosystem == "Flutter-Cache");
        Assert.Contains(results, r => r.Ecosystem == "HuggingFace-Hub");
        Assert.Contains(results, r => r.Ecosystem == "Ollama-Models");
        Assert.Contains(results, r => r.Ecosystem == "PyTorch-Hub");
        Assert.Contains(results, r => r.Ecosystem == "LMStudio-Cache");
        Assert.Contains(results, r => r.Ecosystem == "CherryStudio-Cache");
        Assert.Contains(results, r => r.Ecosystem == "Jan-Cache");
    }
}
