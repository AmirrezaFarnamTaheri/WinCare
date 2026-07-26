import unittest
import xml.etree.ElementTree as ET
import os

class TestGuiAutomation(unittest.TestCase):
    def test_mainwindow_xaml_virtualization(self):
        xaml_path = os.path.join(os.path.dirname(__file__), "..", "src", "WinCare", "Data", "Gui", "WinCare.MainWindow.xaml")
        self.assertTrue(os.path.exists(xaml_path), "WinCare.MainWindow.xaml must exist")
        tree = ET.parse(xaml_path)
        root = tree.getroot()
        content = ET.tostring(root, encoding="utf-8").decode("utf-8")
        self.assertIn("VirtualizingStackPanel.IsVirtualizing", content)
        self.assertIn("VirtualizingStackPanel.VirtualizationMode", content)

if __name__ == "__main__":
    unittest.main()
