# Testing

WinCare validation is layered so structural success cannot substitute for runtime evidence.

## Linux-capable assurance

Run from the repository root:

```bash
python tools/validate_source.py .
python tools/validate_module_manifest.py .
python tools/validate_action_bindings.py .
python tools/validate_powershell_calls.py .
python tools/validate_test_source_assertions.py .
python tools/validate_source_references.py .
python tools/audit_stub_inventory.py .
python tools/validate_maintainability.py .
python tools/validate_network_egress.py .
python tools/validate_external_processes.py .
python tools/validate_bounded_io.py .
python tools/validate_read_only_state.py .
python tools/validate_context_menu_catalog.py .
python tools/validate_test_fixtures.py .
python tools/validate_gui.py .
python -m unittest tools.test_release_tools tools.test_maintainability tools.test_security_invariants tools.test_cleaner_preview tools.test_gui tools.test_source_references tools.test_wincare_ps_source -v
```

These checks cover source topology, exports, contracts, public bindings, current-source reference hashes, stubs, resource bounds, process/network authority, read-only state discipline, test fixtures, GUI catalog integrity, deterministic packaging, and adversarial archive cases.

## Windows gate

```powershell
.\tools\Invoke-WindowsValidation.ps1 -OutputDirectory .\artifacts\windows-validation
```

The Windows gate must run PSScriptAnalyzer, every Pester suite, module import/export smoke tests, native .NET builds, GUI catalog and XAML resource validation, installation, deterministic packaging, archive verification, and clean uninstall exercises.

## Evidence language

- **Verified:** executed successfully.
- **Statically validated:** checked without executing the platform behavior.
- **Reviewed:** manually inspected.
- **Pending:** required but unavailable in the current environment.

Compilation, source inspection, or a passing smoke test is not runtime equivalence evidence.
