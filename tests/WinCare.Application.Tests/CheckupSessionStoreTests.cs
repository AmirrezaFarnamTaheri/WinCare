using WinCare.Application.Checkups;

namespace WinCare.Application.Tests;

public sealed class CheckupSessionStoreTests
{
    [Fact]
    public async Task Completed_checkup_is_reloaded_as_one_run_not_combined_unrelated_activity()
    {
        string folder = Path.Combine(Path.GetTempPath(), "WinCareCheckup_" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(folder, "checkup.json");
        try
        {
            var snapshot = new CheckupSessionSnapshot(
                1, Guid.NewGuid(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "Completed",
                CheckupSessionStore.ProbeIds.Select(id => new CheckupAreaSnapshot(id, "Checked")).ToArray());
            await new CheckupSessionStore(path).SaveAsync(snapshot);

            CheckupSessionSnapshot? restored = new CheckupSessionStore(path).ReadLatest();

            Assert.NotNull(restored);
            Assert.Equal(snapshot.Id, restored.Id);
            Assert.Equal(4, restored.Areas.Count);
            Assert.Equal("Completed", restored.Status);
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); }
    }

    [Fact]
    public async Task Application_restart_marks_abandoned_checkup_interrupted()
    {
        string folder = Path.Combine(Path.GetTempPath(), "WinCareCheckup_" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(folder, "checkup.json");
        try
        {
            var snapshot = new CheckupSessionSnapshot(1, Guid.NewGuid(), DateTimeOffset.UtcNow,
                null, "Running",
                CheckupSessionStore.ProbeIds.Select((id, index) =>
                    new CheckupAreaSnapshot(id, index == 0 ? "Checked" : "Running")).ToArray());
            var running = new CheckupSessionStore(path);
            await running.SaveAsync(snapshot);
            Assert.Equal("Running", running.ReadLatest()!.Status);

            CheckupSessionSnapshot restored = new CheckupSessionStore(path).ReadLatest()!;

            Assert.Equal("Interrupted", restored.Status);
            Assert.Equal("Checked", restored.Areas[0].State);
            Assert.All(restored.Areas.Skip(1), a => Assert.Equal("Interrupted", a.State));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); }
    }

    [Fact]
    public void Invalid_or_oversized_disk_snapshot_is_not_presented_as_a_real_checkup()
    {
        string folder = Path.Combine(Path.GetTempPath(), "WinCareCheckup_" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(folder, "checkup.json");
        Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllText(path, new string('x', CheckupSessionStore.MaximumSnapshotBytes + 1));
            Assert.Null(new CheckupSessionStore(path).ReadLatest());
            File.WriteAllText(path, "{}");
            Assert.Null(new CheckupSessionStore(path).ReadLatest());
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [Theory]
    [InlineData("Checked", "SuccessBrush", "Checked")]
    [InlineData("Checked", "WarningBrush", "NeedsReview")]
    [InlineData("Checked", "DangerBrush", "NeedsReview")]
    [InlineData("NeedsReview", "SuccessBrush", "NeedsReview")]
    [InlineData("Running", "WarningBrush", "Running")]
    public void Probe_success_with_adverse_findings_is_not_presented_as_clean_checkup(
        string probeState, string brush, string expected)
    {
        Assert.Equal(expected, CheckupSessionStore.ClassifyArea(probeState, brush));
    }

}
