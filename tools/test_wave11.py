from __future__ import annotations
import csv, json, re, unittest
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]

class Wave11Tests(unittest.TestCase):
    def test_routes_contracts_and_dispatch_are_complete(self):
        head=(ROOT/'src/WinCare/UI/98-Headless.ps1').read_text('utf-8')
        contracts=(ROOT/'src/WinCare/Core/11-ActionContracts.ps1').read_text('utf-8')
        dispatch=(ROOT/'src/WinCare/Core/09-Transactions.ps1').read_text('utf-8')
        for name in ('cleaner-disk-pressure','cleaner-winapp2-run','cleaner-relocation','file-preview','preview-handlers'):
            self.assertIn("'"+name+"'",head)
        for name in ('DeleteAdmittedCleanupFiles','RelocateDirectoryWithJunction','RestoreRelocatedDirectory'):
            self.assertIn("Add-Contract '"+name+"'",contracts)
            self.assertRegex(dispatch,re.escape("'"+name+"'{Invoke-WinCare"))

    def test_provider_preserves_guardrails(self):
        text=(ROOT/'src/WinCare/Providers/111-CleanerPreviewStudio.ps1').read_text('utf-8')
        for phrase in ('Drive-root wildcard scan','Defender history deletion','Restore-point deletion','Remote UNC paths are not admitted','DtdProcessing]::Prohibit','Loaded=$false'):
            self.assertIn(phrase,text)
        self.assertNotRegex(text,r'Invoke-Expression|ScriptBlock::Create|Stop-Process|SetWindowsHookEx')

    def test_gui_actions_are_unique_and_refer_to_routes(self):
        actions=json.loads((ROOT/'src/WinCare/Data/Gui/actions.json').read_text('utf-8'))
        ids=[a['id'] for a in actions]
        self.assertEqual(len(ids),len(set(ids)))
        selected={a['id']:a for a in actions if a['id'] in {'disk-pressure-cleanup','winapp2-assessment','local-file-preview','preview-handler-assessment'}}
        self.assertEqual(len(selected),4)
        head=(ROOT/'src/WinCare/UI/98-Headless.ps1').read_text('utf-8')
        for action in selected.values(): self.assertIn("'"+action['command']+"'",head)

    def test_wave11_evidence_and_duplicate_link_are_present(self):
        with (ROOT/'docs/convergence/adoption-matrix.csv').open(encoding='utf-8') as f: rows=list(csv.DictReader(f))
        ids={r['record_id'] for r in rows}
        required={'D164-WINDOWS-CLEANER-DISK-PRESSURE','D164-WINDOWS-CLEANER-WINAPP2','D164-WINDOWS-CLEANER-SOFTWARE-MOVE','D165-DUPLICATE-SUBMISSION','D166-QUICKLOOK-PREVIEW-PIPELINE','D166-QUICKLOOK-ACTIVE-CONTENT-GUARDRAIL'}
        self.assertTrue(required<=ids)
        dup=next(r for r in rows if r['record_id']=='D165-DUPLICATE-SUBMISSION')
        self.assertIn('D143',dup['decision_rationale'])

    def test_inventory_remains_continuous_and_counts_three_submissions(self):
        donors=json.loads((ROOT/'docs/convergence/donor-inventory.json').read_text('utf-8'))
        ids={d['donor_id'] for d in donors}
        expected={f'D{i:02d}' if i<100 else f'D{i}' for i in range(1,167)}
        self.assertEqual(ids,expected)
        by={d['donor_id']:d for d in donors}
        self.assertEqual(by['D165'].get('duplicate_of'),'D143')
        self.assertEqual(sum(1 for d in donors if not d.get('duplicate_of')),153)

if __name__=='__main__': unittest.main()
