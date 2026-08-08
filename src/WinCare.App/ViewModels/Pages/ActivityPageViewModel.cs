using WinCare.Application.Activity;
using WinCare.Application.Commands;
using WinCare.Domain.Activity;

namespace WinCare.App.ViewModels.Pages;

public sealed class ActivityPageViewModel : TabbedPageViewModel
{
    private readonly ActivityJournalService _journal;

    public ActivityPageViewModel()
        : this(CommandRuntime.LastJournal)
    {
    }

    public ActivityPageViewModel(ActivityJournalService journal)
        : base([
            new PageSection("Running", "No operations are running.", []),
            new PageSection("Needs attention", "No operations need attention.", []),
            new PageSection("Completed", "Completed native operations will appear here.", []),
            new PageSection("Reports", "No native reports are available.", [])])
    {
        _journal = journal ?? throw new ArgumentNullException(nameof(journal));
        RefreshFromJournal();
    }

    public bool HasAttentionItems =>
        _journal.GetAll().Any(r => r.State == ActivityState.NeedsAttention);

    public void RefreshFromJournal()
    {
        IReadOnlyList<ActivityRecord> records = _journal.GetAll();
        OnPropertyChanged(nameof(HasAttentionItems));
    }
}
