namespace WinCare.App.Controls;

using Microsoft.UI.Xaml.Controls;
using WinCare.Application.Plugins;

public sealed partial class WidgetContainer : UserControl
{
    public WidgetContainer()
    {
        InitializeComponent();
    }

    public void PopulateWidgets(IEnumerable<IPluginWidget> widgets)
    {
        WidgetsList.ItemsSource = widgets;
    }
}
