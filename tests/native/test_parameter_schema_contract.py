from __future__ import annotations

import re
import unittest
from pathlib import Path

from tools.verify_native_foundation import load_oracle_commands


class ParameterSchemaContractTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.root = Path(__file__).resolve().parents[2]
        cls.executor = (cls.root / "src/WinCare.Infrastructure/Commands/WindowsCommandExecutor.cs").read_text(encoding="utf-8")
        cls.security = (cls.root / "src/WinCare.Infrastructure/Commands/WindowsCommandExecutor.Security.cs").read_text(encoding="utf-8")
        cls.schema = (cls.root / "src/WinCare.CommandCatalog/Models/CommandParameterCatalog.cs").read_text(encoding="utf-8")
        cls.execution_vm = (cls.root / "src/WinCare.App/ViewModels/Pages/ToolExecutionViewModel.cs").read_text(encoding="utf-8")
        cls.all_tools = (cls.root / "src/WinCare.App/Views/Pages/AllToolsPage.xaml").read_text(encoding="utf-8")

    def test_schema_only_references_real_catalog_commands(self) -> None:
        schema_ids = set(re.findall(r"^([a-z0-9-]+)\\|", self.schema, re.MULTILINE))
        catalog_ids = set(load_oracle_commands())
        self.assertTrue(schema_ids, "typed parameter schema must not be empty")
        self.assertEqual(set(), schema_ids - catalog_ids)

    def test_offline_feature_set_uses_one_boolean_contract_end_to_end(self) -> None:
        match = re.search(r'case "offline-feature-set":(?P<body>.*?)break;', self.executor, re.DOTALL)
        self.assertIsNotNone(match)
        body = match.group("body")
        self.assertIn('RequireStrings(p, "ImagePath", "FeatureName")', body)
        self.assertIn('p.Boolean("Enabled", true)', body)
        self.assertNotIn('"State"', body)
        self.assertIn('bool enable = p.Boolean("Enabled", true);', self.security)
        self.assertIn('offline-feature-set|FeatureName:!s;Enabled:b=true;ImagePath:!s', self.schema)

    def test_all_tools_defaults_to_typed_fields_and_keeps_json_advanced(self) -> None:
        self.assertIn("CommandParameterCatalog.For", self.execution_vm)
        self.assertIn("ParameterFields", self.execution_vm)
        self.assertIn("CommandParameterFieldViewModel", self.execution_vm)
        self.assertIn('AutomationProperties.AutomationId="CommandParameterFields"', self.all_tools)
        self.assertIn('Header="Advanced JSON parameters"', self.all_tools)


if __name__ == "__main__":
    unittest.main()
