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

        TotalMemoryBlock.Text = $"{totalGb:F1} GB TOTAL";
        TxtActive.Text = $"{activeGb:F1} GB";
        TxtFree.Text = $"{availGb:F1} GB";

        ColActive.Width = new GridLength(activeGb, GridUnitType.Star);
        ColFree.Width = new GridLength(availGb, GridUnitType.Star);
    }

    private async void BtnCompact_Click(object sender, RoutedEventArgs e)
    {
        BtnCompact.IsEnabled = false;
        try
        {
            _ = await MemoryGovernor.CompactSystemMemoryAsync();
            RefreshTopology();
        }
        finally
        {
            BtnCompact.IsEnabled = true;
        }
    }
}
