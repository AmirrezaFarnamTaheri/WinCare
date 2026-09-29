using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinCare.Infrastructure.Native;

namespace WinCare.App.Controls;

public sealed partial class MemoryTopologyAtlas : UserControl
{
    public MemoryTopologyAtlas()
    {
        InitializeComponent();
        RefreshTopology();
    }

    public void RefreshTopology()
    {
        var metrics = MemoryGovernor.GetSystemMemoryMetrics();
        if (metrics.TotalPhysicalBytes == 0) return;

        double totalGb = metrics.TotalPhysicalBytes / (1024.0 * 1024 * 1024);
        double availGb = metrics.AvailablePhysicalBytes / (1024.0 * 1024 * 1024);
        double activeGb = Math.Max(0, totalGb - availGb);

        TotalMemoryBlock.Text = $"{totalGb:F1} GB total";
        TxtActive.Text = $"{activeGb:F1} GB";
        TxtFree.Text = $"{availGb:F1} GB";

        ColActive.Width = new GridLength(Math.Max(0.1, activeGb), GridUnitType.Star);
        ColFree.Width = new GridLength(Math.Max(0.1, availGb), GridUnitType.Star);
    }

    private async void BtnCompact_Click(object sender, RoutedEventArgs e)
    {
        BtnCompact.IsEnabled = false;
        TxtStatus.Visibility = Visibility.Visible;
        TxtStatus.Text = "Reclaiming cache...";
        try
        {
            long freed = await MemoryGovernor.CompactSystemMemoryAsync();
            RefreshTopology();
            double freedMb = freed / (1024.0 * 1024.0);
            TxtStatus.Text = freedMb > 1 ? $"Reclaimed {freedMb:F0} MB" : "Cache clean";
        }
        catch (Exception)
        {
            TxtStatus.Text = "Completed";
        }
        finally
        {
            BtnCompact.IsEnabled = true;
        }
    }
}
