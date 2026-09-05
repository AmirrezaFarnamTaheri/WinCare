from __future__ import annotations

import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]


class PluginAdmissionRollbackTests(unittest.TestCase):
    def test_failed_admission_restore_preserves_known_good_recovery_backup(self) -> None:
        source = (ROOT / "src/WinCare.Infrastructure/Plugins/PluginInstallerService.cs").read_text(encoding="utf-8")

        for token in (
            "bool preserveAdmissionBackup = false;",
            "preserveAdmissionBackup = true;",
            "new AggregateException(installException, admissionRestoreException)",
            "A recovery backup was preserved in the plugin staging backup directory.",
            "if (!preserveAdmissionBackup && admissionBackupPath != null",
        ):
            self.assertIn(token, source)

        self.assertNotIn(
            "try { File.Copy(admissionBackupPath, admissionPath, overwrite: true); } catch { }",
            source,
        )


if __name__ == "__main__":
    unittest.main()
