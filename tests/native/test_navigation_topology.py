"""Cross-layer WinUI navigation contract.

The managed catalog, XAML shell, .resw localization and PageService routing
must evolve together. No full Windows runtime is needed to catch mismatches.
"""
from pathlib import Path
import re
import unittest
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
XAML = "http://schemas.microsoft.com/winfx/2006/xaml"


def read(relative: str) -> str:
    return (ROOT / relative).read_text(encoding="utf-8")


def local_name(tag: str) -> str:
    return tag.split("}")[-1]


class NavigationTopologyTests(unittest.TestCase):
    def test_catalog_routes_shell_and_localization_are_bijective(self) -> None:
        catalog = read("src/WinCare.Application/Navigation/NavigationCatalog.cs")
        page_service = read("src/WinCare.App/Services/PageService.cs")
        shell = ET.fromstring(read("src/WinCare.App/Views/ShellPage.xaml"))
        resources = ET.fromstring(read("src/WinCare.App/Strings/en-US/Resources.resw"))

        entries = re.findall(
            r'^\s*new\("([^"]+)",\s*"([^"]+)",\s*"([^"]+)",[^\n]+',
            catalog, flags=re.MULTILINE
        )
        self.assertGreaterEqual(len(entries), 10)
        ids = [entry[0] for entry in entries]
        self.assertEqual(len(ids), len(set(ids)), "Catalog route IDs must be unique")
        self.assertEqual(len(entries), len({entry[2] for entry in entries}),
                         "Catalog automation IDs must be unique")

        mappings = re.findall(r'\["([^"]+)"\]\s*=\s*typeof\((\w+)\)', page_service)
        self.assertEqual(set(ids), {route for route, _ in mappings},
                         "Every catalog route needs exactly one PageService entry")
        self.assertEqual(len(mappings), len(ids))
        for _, page_name in mappings:
            self.assertTrue((ROOT / f"src/WinCare.App/Views/Pages/{page_name}.xaml").is_file(),
                            f"Missing UI for {page_name}")

        menu = [element for element in shell.iter()
                if local_name(element.tag) == "NavigationViewItem"]
        self.assertEqual(len(menu), len({item.attrib["Tag"] for item in menu}),
                         "Shell cannot contain duplicate route Tags")
        keyed = {item.attrib["Tag"]: item for item in menu}
        hidden = {entry[0] for entry in entries if 'IsHidden: true' in
                  next(line for line in catalog.splitlines() if
                       line.strip().startswith(f'new("{entry[0]}",'))}
        self.assertEqual(set(ids) - hidden, set(keyed),
                         "Every nonhidden route must have a shell item")
        localized = {item.attrib["name"] for item in resources.findall("data")}
        for route, label, automation_id in entries:
            self.assertIn(f"{automation_id}.Content", localized)
            if route in hidden:
                continue
            menu_item = keyed[route]
            self.assertEqual(menu_item.attrib.get(f"{{{XAML}}}Uid"), automation_id)
            self.assertEqual(menu_item.attrib.get("AutomationProperties.AutomationId"), automation_id)
            self.assertEqual(menu_item.attrib.get("Content"), label)

        footer = next(item for item in shell.iter()
                      if local_name(item.tag) == "NavigationView.FooterMenuItems")
        footer_routes = {item.attrib["Tag"] for item in footer
                         if local_name(item.tag) == "NavigationViewItem"}
        expected_footer = {
            route for route, _, _ in entries
            if 'IsFooter: true' in next(line for line in catalog.splitlines()
                  if line.strip().startswith(f'new("{route}",'))
        }
        self.assertEqual(expected_footer, footer_routes)


if __name__ == "__main__":
    unittest.main()
