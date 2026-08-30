using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WinCare.App.Views;
using WinCare.App.ViewModels.Pages;

namespace WinCare.App.Views.Pages;

public sealed partial class HomePage : Page
{
    private const double CompactThreshold = 820;

    public HomePage()
    {
        ViewModel = new HomePageViewModel();
        InitializeComponent();
    }

    public HomePageViewModel ViewModel { get; }

    private void RunCheckupButton_Click(object sender, RoutedEventArgs e) => NavigateTo("checkup");

    private void ViewActivityButton_Click(object sender, RoutedEventArgs e) => NavigateTo("activity");

    private void BrowseToolsButton_Click(object sender, RoutedEventArgs e) => NavigateTo("all-tools");

    private void NavigateTo(string key)
    {
        DependencyObject? current = this;
        while (current is not null)
        {
            if (current is ShellPage shell)
            {
                shell.NavigateTo(key);
                return;
            }

            current = VisualTreeHelper.GetParent(current);
        }
    }

    private void Page_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ViewModel.SetCompactLayout(e.NewSize.Width < CompactThreshold);
    }
}
