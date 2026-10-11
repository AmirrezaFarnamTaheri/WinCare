using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinCare.Infrastructure.Native;
using WinCare.App.Views;
using System.Text.Json;

namespace WinCare.App.Controls;

public sealed partial class MemoryTopologyAtlas : UserControl
{
    public MemoryTopologyAtlas()
    {
        InitializeComponent();
        WinCare.App.Animations.KineticSpringAnimation.AttachSpringHover(BtnCompact, 1.02f);
        RefreshTopology();
    }

    public void RefreshTopology()
    {
        var metrics = MemoryGovernor.GetSystemMemoryMetrics();
        if (metrics.TotalPhysicalBytes == 0)
        {
            TotalMemoryBlock.Text = "Memory reading unavailable";
            TxtPressure.Text = "No reading";
            TxtActive.Text = "--";
            TxtFree.Text = "--";
            MemoryUsageBar.Visibility = Visibility.Collapsed;
            return;
        }
        MemoryUsageBar.Visibility = Visibility.Visible;

        double totalGb = metrics.TotalPhysicalBytes / (1024.0 * 1024 * 1024);
        double availGb = metrics.AvailablePhysicalBytes / (1024.0 * 1024 * 1024);
        double activeGb = Math.Max(0, totalGb - availGb);

        TotalMemoryBlock.Text = $"{totalGb:F1} GB total";
        TxtActive.Text = $"{activeGb:F1} GB";
        TxtFree.Text = $"{availGb:F1} GB";
        TxtPressure.Text = $"{metrics.MemoryLoadPercentage}% load";

        ColActive.Width = new GridLength(Math.Max(0.1, activeGb), GridUnitType.Star);
        ColFree.Width = new GridLength(Math.Max(0.1, availGb), GridUnitType.Star);
    }

    // All system mutations are reviewed and executed by the canonical dispatcher.
    // The widget only displays read-only memory readings and navigates to the tool.
    private void BtnCompact_Click(object sender, RoutedEventArgs e) =>
        PageNavigation.OpenTool(this, "perf-standby-purge", JsonSerializer.SerializeToElement(new { }));

}
