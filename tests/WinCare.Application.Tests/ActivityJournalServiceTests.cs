using WinCare.Application.Activity;
using WinCare.Domain.Activity;

namespace WinCare.Application.Tests;

public sealed class ActivityJournalServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "wincare-journal-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Restart_marks_unfinished_operations_for_review()
    {
        var path = Path.Combine(_root, "journal.json");
        var journal = new ActivityJournalService(path);
        var running = journal.Begin("test", "Test");
        var reloaded = new ActivityJournalService(path);
        var record = Assert.Single(reloaded.GetAll());
        Assert.Equal(running.Id, record.Id);
        Assert.Equal(ActivityState.NeedsAttention, record.State);
        Assert.False(record.UndoAvailable);
    }

    [Fact]
    public void Completed_history_is_bounded_without_evicting_active_operations()
    {
        var journal = new ActivityJournalService(Path.Combine(_root, "journal.json"));
        var active = journal.Begin("active", "Still running");
        for (int i = 0; i < 205; i++)
        {
            var record = journal.Begin("test", "Test");
            journal.Complete(record.Id, "Done");
        }
        Assert.Equal(200, journal.GetAll().Count);
        Assert.Contains(journal.GetAll(), record => record.Id == active.Id);
        journal.Complete(active.Id, "Finally done");
        Assert.Contains(journal.GetAll(), record => record.Id == active.Id && record.State == ActivityState.Completed);
    }

    [Fact]
    public void Null_records_in_saved_json_do_not_crash_startup()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "journal.json");
        File.WriteAllText(path, "[null]");
        Assert.Empty(new ActivityJournalService(path).GetAll());
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
