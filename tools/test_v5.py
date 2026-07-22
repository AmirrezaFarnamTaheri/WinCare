from __future__ import annotations
import csv, importlib.util, json, unittest, xml.etree.ElementTree as ET
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
spec=importlib.util.spec_from_file_location('validate_v5',ROOT/'tools/validate_v5.py')
mod=importlib.util.module_from_spec(spec);spec.loader.exec_module(mod)

class V5ValidationTests(unittest.TestCase):
    def test_primary_contrast_exceeds_wcag_aa(self):
        self.assertGreaterEqual(mod.contrast('#F4F7FB','#0B1020'),4.5)
        self.assertGreaterEqual(mod.contrast('#F97066','#512128'),4.5)

    def test_gui_json_ids_are_unique(self):
        for name in ('actions.json','navigation.json'):
            data=json.loads((ROOT/'src/WinCare/Data/Gui'/name).read_text('utf-8'))
            ids=[item['id'] for item in data]
            self.assertEqual(len(ids),len(set(ids)))

    def test_xaml_is_code_behind_free_and_named(self):
        ns='{http://schemas.microsoft.com/winfx/2006/xaml}'
        tree=ET.parse(ROOT/'src/WinCare/Data/Gui/WinCare.MainWindow.xaml')
        names=[v for e in tree.iter() for k,v in e.attrib.items() if k==ns+'Name']
        self.assertIn('MainWindow',names);self.assertIn('ConfirmOverlay',names)
        self.assertFalse(any(ns+'Class' in e.attrib for e in tree.iter()))

    def test_v5_records_have_surface_links(self):
        required={'D111-V5-HEALTH-DASHBOARD','D127-V5-COMMAND-WORKBENCH','D127-V5-RISK-COMMUNICATION','D143-V5-CAPABILITY-NAVIGATION','D143-V5-EVIDENCE-EXPLORER'}
        with (ROOT/'docs/convergence/adoption-matrix.csv').open(encoding='utf-8') as stream:
            rows=list(csv.DictReader(stream))
        with (ROOT/'docs/convergence/surface-accountability.csv').open(encoding='utf-8') as stream:
            surfaces=list(csv.DictReader(stream))
        self.assertTrue(required <= {r['record_id'] for r in rows})
        refs={ref for row in surfaces for ref in row['semantic_record_ids'].split(';') if ref}
        self.assertTrue(required <= refs)

    def test_donor_inventory_is_continuous(self):
        donors=json.loads((ROOT/'docs/convergence/donor-inventory.json').read_text('utf-8'))
        ids={d['donor_id'] for d in donors}
        expected={f'D{i:02d}' if i<100 else f'D{i}' for i in range(1,167)}
        self.assertEqual(ids,expected)

    def test_gui_process_boundary_is_argument_safe_and_bounded(self):
        runtime=(ROOT/'src/WinCare/UI/Gui/10-GuiRuntime.ps1').read_text('utf-8')
        self.assertIn('[Diagnostics.ProcessStartInfo]::new()',runtime)
        self.assertIn('$psi.ArgumentList.Add',runtime)
        self.assertIn('$psi.WorkingDirectory=$root',runtime)
        self.assertIn('24576 characters',runtime)
        self.assertNotRegex(runtime,r'Invoke-WinCarePlan\s+-Plan')

    def test_gui_themes_are_dynamic_and_accessible(self):
        xaml=(ROOT/'src/WinCare/Data/Gui/WinCare.MainWindow.xaml').read_text('utf-8')
        runtime=(ROOT/'src/WinCare/UI/Gui/10-GuiRuntime.ps1').read_text('utf-8')
        for token in ('BackgroundBrush','SurfaceBrush','AccentBrush','TextBrush'):
            self.assertIn('{DynamicResource '+token+'}',xaml)
        self.assertIn("ValidateSet('Normal','HighContrast','Monochrome')",runtime)
        self.assertIn('[Windows.SystemParameters]::HighContrast',runtime)

    def test_visible_evidence_metrics_match_inventory(self):
        import re
        xaml=(ROOT/'src/WinCare/Data/Gui/WinCare.MainWindow.xaml').read_text('utf-8')
        donors=json.loads((ROOT/'docs/convergence/donor-inventory.json').read_text('utf-8'))
        submitted=int(re.search(r'x:Name="EvidenceDonorText"\s+Text="(\d+)"',xaml).group(1))
        unique=int(re.search(r'<TextBlock\s+Text="(\d+) unique baselines"',xaml).group(1))
        self.assertEqual(submitted,len(donors))
        self.assertEqual(unique,sum(1 for d in donors if not d.get('duplicate_of')))

    def test_xaml_resource_references_are_resolved(self):
        import re
        text=(ROOT/'src/WinCare/Data/Gui/WinCare.MainWindow.xaml').read_text('utf-8')
        definitions=set(re.findall(r'x:Key\s*=\s*["\']([^"\']+)["\']',text))
        references=set(re.findall(r'\{(?:StaticResource|DynamicResource)\s+([^},\s]+)',text))
        self.assertEqual(references-definitions,set())

    def test_wave10_records_and_symlink_surfaces_are_present(self):
        with (ROOT/'docs/convergence/adoption-matrix.csv').open(encoding='utf-8') as stream:
            rows=list(csv.DictReader(stream))
        with (ROOT/'docs/convergence/surface-accountability.csv').open(encoding='utf-8') as stream:
            surfaces=list(csv.DictReader(stream))
        record_ids={row['record_id'] for row in rows}
        self.assertTrue({'D146-MONITORIAN-DDCCI','D152-APPX-LAUNCH-METADATA','D159-NANAZIP-ARCHIVE-SAFETY','D161-WINHANCE-MAXIMUM','D163-EXPLORER-SESSION-RESTORE'} <= record_ids)
        links=[row for row in surfaces if row['file_type']=='symlink' and row['donor'] in {'D153','D162'}]
        self.assertEqual(len(links),15)
        self.assertTrue(all('not materialized' in row['notes'] for row in links))

    def test_pester_inventory_and_gui_suite_are_complete(self):
        import re
        suites=sorted((ROOT/'tests').glob('*.ps1'))
        counts={p.name:len(re.findall(r"^\s*It\s+['\"]",p.read_text('utf-8'),re.M)) for p in suites}
        self.assertEqual(len(suites),18)
        self.assertEqual(sum(counts.values()),217)
        self.assertEqual(counts['V5.Gui.Tests.ps1'],12)

if __name__=='__main__': unittest.main()
