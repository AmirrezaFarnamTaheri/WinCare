using WinCare.Application.Activity;
using WinCare.Domain.Activity;

namespace WinCare.Application.Tests;

public sealed class ActivityJournalServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "wincare-journal-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Restart_marks_unfinished_operations_for_review()
    {
        var path = Path.Combine(_root, "journal.json");
        var journal = new ActivityJournalService(path);
        var running = journal.Begin("test", "Test");
        await journal.FlushAsync();
        var reloaded = new ActivityJournalService(path);
        var record = Assert.Single(reloaded.GetAll());
        Assert.Equal(running.Id, record.Id);
        Assert.Equal(ActivityState.NeedsAttention, record.State);
        Assert.False(record.UndoAvailable);
    }

    [Fact]
    public async Task Completed_history_is_bounded_without_evicting_active_operations()
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
        await journal.FlushAsync();
    }

    [Fact]
    public void Null_records_in_saved_json_do_not_crash_startup()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "journal.json");
        File.WriteAllText(path, "[null]");
        Assert.Empty(new ActivityJournalService(path).GetAll());
    }

    [Fact]
    public async Task Persistence_failure_is_visible_while_memory_journal_remains_available()
    {
        Directory.CreateDirectory(_root);
        string blocker = Path.Combine(_root, "not-a-directory");
        File.WriteAllText(blocker, "file");
        var journal = new ActivityJournalService(Path.Combine(blocker, "activity.json"));

        ActivityRecord record = journal.Begin("test", "Test");
        await journal.FlushAsync();

        Assert.Contains(journal.GetAll(), item => item.Id == record.Id);
        Assert.False(journal.IsPersistenceHealthy);
        Assert.False(string.IsNullOrWhiteSpace(journal.PersistenceStatusMessage));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
