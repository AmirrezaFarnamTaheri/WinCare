using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinCare.App.ViewModels.Pages;
using WinCare.App.Views;

namespace WinCare.App.Controls;

public sealed partial class CareToolList : UserControl
{
    private bool _isLoaded;
    public CareToolList()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (_isLoaded) return;
            _isLoaded = true;
            Services.AppRuntime.Current.Journal.Changed += EvidenceChanged;
            Services.AppRuntime.Current.ToolCatalog.CatalogChanged += EvidenceChanged;
            ViewModel?.RefreshTools();
        };
        Unloaded += (_, _) =>
        {
            _isLoaded = false;
            Services.AppRuntime.Current.Journal.Changed -= EvidenceChanged;
            Services.AppRuntime.Current.ToolCatalog.CatalogChanged -= EvidenceChanged;
        };
    }

    private void EvidenceChanged(object? sender, EventArgs e) => DispatcherQueue.TryEnqueue(() =>
    {
        if (_isLoaded) ViewModel?.RefreshTools();
    });

    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel), typeof(TabbedPageViewModel), typeof(CareToolList), new PropertyMetadata(null));

    public TabbedPageViewModel ViewModel
    {
        get => (TabbedPageViewModel)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    private void Control_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        bool compact = e.NewSize.Width < 760;
        ViewModel?.SetCompactLayout(compact);
        ColumnHeaders.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Tool_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is PageRow { CommandId: { } id }) PageNavigation.OpenTools(this, id);
    }
}
