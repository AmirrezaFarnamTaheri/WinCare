"""Download-workspace wiring and fail-closed contract checks.

Static gates are supplementary to the real .NET command and WinUI tests.
"""
from pathlib import Path
import unittest
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]


class DownloadsWorkspaceTests(unittest.TestCase):
    def read(self, path: str) -> str:
        return (ROOT / path).read_text(encoding="utf-8")

    def test_page_is_native_and_reuses_one_runtime_dispatcher(self) -> None:
        routes = self.read("src/WinCare.Application/Navigation/NavigationCatalog.cs")
        mapping = self.read("src/WinCare.App/Services/PageService.cs")
        shell = self.read("src/WinCare.App/Views/ShellPage.xaml")
        root = self.read("src/WinCare.App/Services/AppRuntime.cs")
        page = self.read("src/WinCare.App/Views/Pages/DownloadsPage.xaml")
        code = self.read("src/WinCare.App/Views/Pages/DownloadsPage.xaml.cs")
        view_model = self.read("src/WinCare.App/ViewModels/Pages/DownloadsPageViewModel.cs")
        self.assertIn('new("downloads", "Downloads", "NavDownloads"', routes)
        self.assertIn('["downloads"] = typeof(DownloadsPage)', mapping)
        self.assertIn('Tag="downloads"', shell)
        self.assertIn('Downloads = new DownloadCenterService(Dispatcher);', root)
        self.assertIn('await Downloads.PauseActiveForShutdownAsync(token)', root)
        self.assertIn('IsItemClickEnabled="False"', page)
        self.assertIn('ItemsSource="{x:Bind ViewModel.VisibleJobs, Mode=OneWay}"', page)
        self.assertIn("ViewModel.Deactivate();", code)
        self.assertIn("AppRuntime.Current.Downloads", view_model)
        self.assertIn("_center.TryStart(id, resume: false", view_model)
        self.assertNotIn("new HttpClient(", code)
        ET.fromstring(page)
        home = self.read("src/WinCare.App/Views/Pages/HomePage.xaml")
        self.assertIn('x:Name="DownloadsRecommendation"', home)
        self.assertIn('<RowDefinition Height="Auto"/><RowDefinition Height="Auto"/><RowDefinition Height="Auto"/><RowDefinition Height="Auto"/>', home)
        self.assertNotIn('RecommendationsGrid.ColumnDefinitions[2]', self.read("src/WinCare.App/Views/Pages/HomePage.xaml.cs"))
        self.assertIn('AutomationProperties.AutomationId="HomeDownloads"', home)
        # The old synthetic BitTorrent panel is intentionally absent until
        # a real transfer engine has independently passed network tests.
        self.assertNotIn("Active BitTorrent Swarms", page)
        self.assertNotIn("xmlns:transfers", page)
        self.assertNotIn("TorrentSessionSnapshot", view_model)
        self.assertNotIn("Torrents.GetActiveSnapshots", view_model)
        self.assertNotIn("PauseTorrent_Click", code)

    def test_native_downloads_never_overwrite_a_destination_or_trust_bad_ranges(self) -> None:
        executor = self.read("src/WinCare.Infrastructure/Commands/WindowsCommandExecutor.Productivity.cs")
        scheduler = self.read("src/WinCare.Application/Downloads/DownloadTaskScheduler.cs")
        for contract in (
            "DownloadCreationGate.WaitAsync", "uri.UserInfo.Length != 0",
            "ContentRange", "range.From != existingBytes",
            "download-path:", "File.Move(partial, destination, overwrite: false)",
            'node["expectedBytes"]', "Download job created.",
            "Too many downloads use this file name",
            "idleTimeout.CancelAfter(TimeSpan.FromSeconds(90))",
        ):
            self.assertIn(contract, executor)
        self.assertNotIn("File.Move(partial, destination, overwrite: true)", executor)
        self.assertIn("File.Move(tempPath, task.DestinationPath, overwrite: false)", scheduler)

    def test_metrics_have_explicit_rows_for_compact_layout(self) -> None:
        page = ET.fromstring(self.read("src/WinCare.App/Views/Pages/DownloadsPage.xaml"))
        code = self.read("src/WinCare.App/Views/Pages/DownloadsPage.xaml.cs")
        xaml_ns = "http://schemas.microsoft.com/winfx/2006/xaml"
        metrics = next(element for element in page.iter()
                       if element.get(f"{{{xaml_ns}}}Name") == "MetricsGrid")
        definitions = next(child for child in metrics if child.tag.endswith("Grid.RowDefinitions"))
        self.assertEqual(3, len(list(definitions)))
        self.assertIn("MetricsGrid.RowDefinitions[i].Height", code)
        self.assertIn("Grid.SetRow(FinishedMetric, compact ? 2 : 0);", code)

    def test_ui_displays_only_redacted_sources_and_real_progress(self) -> None:
        projection = self.read("src/WinCare.Application/Downloads/DownloadJobProjection.cs")
        page = self.read("src/WinCare.App/Views/Pages/DownloadsPage.xaml")
        for token in ('address.IdnHost', '"Interrupted"', '"expectedBytes"', 'IsIndeterminate', 'MaxVisibleJobs'):
            self.assertIn(token, projection)
        self.assertNotIn('Text="{x:Bind Url}"', page)
        self.assertNotIn('Text="{x:Bind Destination}"', page)
        self.assertIn('Text="{x:Bind Host}"', page)
        self.assertIn('AutomationProperties.Name="{x:Bind AccessibleName}"', page)
        self.assertIn('MenuFlyoutItem Text="Remove record" Click="Remove_Click"', page)
        self.assertIn('Content="More"', page)
        self.assertNotIn('HorizontalScrollBarVisibility="Auto"', page)
        self.assertIn('IsIndeterminate="{x:Bind IsIndeterminate}"', page)


if __name__ == "__main__":
    unittest.main()
