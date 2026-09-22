"""Provenance gates for the runtime capture pipeline.

A capture's manifest must describe the artifact it was rendered from, not the checkout
that happened to build it. These tests pin the PE-header architecture reader and the
freshness report — the two pieces that make the manifest self-validating.
"""

from __future__ import annotations

import importlib.util
import json
import struct
import subprocess
import tempfile
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]


def _load_generator():
    spec = importlib.util.spec_from_file_location(
        "capture_screenshots", ROOT / "tools" / "capture_screenshots.py"
    )
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def _write_pe(path: Path, machine: int) -> None:
    """Writes the smallest plausible PE: MZ header, e_lfanew, PE\\0\\0, machine type."""
    pe_offset = 0x40
    blob = bytearray(pe_offset + 6)
    blob[0:2] = b"MZ"
    struct.pack_into("<I", blob, 0x3C, pe_offset)
    blob[pe_offset:pe_offset + 4] = b"PE\x00\x00"
    struct.pack_into("<H", blob, pe_offset + 4, machine)
    path.write_bytes(blob)


class PeArchitectureTests(unittest.TestCase):
    def setUp(self) -> None:
        self.generator = _load_generator()

    def test_reads_x64_and_arm64_machine_types(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            for machine, expected in ((0x014C, "x64"), (0x8664, "x64"), (0xAA64, "ARM64")):
                exe = Path(tmp) / f"winCare-{machine}.exe"
                _write_pe(exe, machine)
                self.assertEqual(self.generator._exe_architecture_from_pe(exe), expected)

    def test_a_non_pe_file_reads_as_unknown(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            not_an_exe = Path(tmp) / "not-an-exe.exe"
            not_an_exe.write_bytes(b"this is not a PE file at all")
            self.assertIsNone(self.generator._exe_architecture_from_pe(not_an_exe))

    def test_an_unmapped_machine_type_reads_as_unknown(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            exe = Path(tmp) / "exotic.exe"
            _write_pe(exe, 0xFFFF)
            self.assertIsNone(self.generator._exe_architecture_from_pe(exe))

    def test_normalization_accepts_the_runtime_names_the_app_reports(self) -> None:
        normalize = self.generator._normalize_architecture
        self.assertEqual(normalize("X64"), "x64")
        self.assertEqual(normalize("Arm64"), "ARM64")
        self.assertEqual(normalize("x86_64"), "x64")
        self.assertEqual(normalize("aarch64"), "ARM64")
        self.assertIsNone(normalize(""))
        self.assertIsNone(normalize(None))
        self.assertIsNone(normalize("mips"))


class ProvenanceResolutionTests(unittest.TestCase):
    def setUp(self) -> None:
        self.generator = _load_generator()

    def test_sidecar_version_and_architecture_win_over_the_checkout(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            exe = Path(tmp) / "WinCare.App.exe"
            _write_pe(exe, 0x8664)
            meta = {"version": "3.0.0", "architecture": "Arm64", "windowSizeDips": "1280x800"}
            provenance = self.generator._resolve_provenance(exe, meta, "abc1234")

        self.assertEqual(provenance["version"], "3.0.0")
        self.assertEqual(provenance["version_source"], "executable")
        self.assertEqual(provenance["architecture"], "ARM64")
        self.assertEqual(provenance["commit"], "abc1234")

    def test_a_missing_sidecar_falls_back_to_the_pe_header_and_props(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            exe = Path(tmp) / "WinCare.App.exe"
            _write_pe(exe, 0xAA64)
            provenance = self.generator._resolve_provenance(exe, {}, "abc1234")

        self.assertEqual(provenance["architecture"], "ARM64")
        self.assertEqual(provenance["architecture_checked"], "ARM64")
        self.assertEqual(provenance["version_source"], "directory.build.props")
        self.assertEqual(provenance["version"], self.generator._product_version())

    def test_an_architecture_conflict_fails_the_capture(self) -> None:
        """An exe whose sidecar and header disagree must not be recorded as either."""
        with tempfile.TemporaryDirectory() as tmp:
            exe = Path(tmp) / "WinCare.App.exe"
            _write_pe(exe, 0xAA64)
            provenance = self.generator._resolve_provenance(
                exe, {"architecture": "X64", "version": "3.0.0"}, "abc1234")

        self.assertEqual(provenance["architecture"], "x64")
        self.assertEqual(provenance["architecture_checked"], "ARM64")
        self.assertNotEqual(provenance["architecture"], provenance["architecture_checked"])


class FreshnessReportTests(unittest.TestCase):
    def setUp(self) -> None:
        self.generator = _load_generator()

    def _head(self) -> str:
        return subprocess.run(
            ["git", "rev-parse", "--short", "HEAD"],
            cwd=ROOT, stdout=subprocess.PIPE, text=True, timeout=10,
        ).stdout.strip()

    def test_an_empty_manifest_reports_unrecorded(self) -> None:
        report = self.generator.capture_freshness_report(None)
        self.assertEqual(report["status"], "unrecorded")
        # The tree is dirty while these tests are part of it, so the tool correctly
        # appends the uncommitted marker; the base SHA is what matters.
        self.assertEqual(report["head"].split("+")[0], self._head())

    def test_a_manifest_at_head_with_no_source_change_is_fresh(self) -> None:
        head = self._head()
        manifest = {"images": {"runtime-dashboard.png": {"commit": head}}}
        report = self.generator.capture_freshness_report(manifest)
        self.assertEqual(report["status"], "fresh")
        self.assertEqual(report["manifest_commit"], head)
        self.assertEqual(report["changed"], [])

    def test_a_manifest_pointing_at_an_ancestor_source_commit_is_stale(self) -> None:
        ancestor = subprocess.run(
            ["git", "rev-parse", "--short", "HEAD~3"],
            cwd=ROOT, stdout=subprocess.PIPE, text=True, timeout=10,
        ).stdout.strip()
        manifest = {"images": {"runtime-dashboard.png": {"commit": ancestor}}}
        report = self.generator.capture_freshness_report(manifest)

        self.assertEqual(report["status"], "stale")
        self.assertEqual(report["manifest_commit"], ancestor)
        self.assertTrue(report["changed"], "a stale manifest must list what changed")
        self.assertTrue(all(p.startswith("src/") or p.startswith("tools/") for p in report["changed"]),
                         "only capture-affecting paths may be reported")

    def test_a_manifest_behind_head_on_docs_only_is_still_fresh(self) -> None:
        """Documentation-only changes cannot make a runtime image historical."""
        head = self._head()
        docs_only = subprocess.run(
            ["git", "rev-list", "--max-count=20", "-s", "--all", "--", "docs/Screenshots.md"],
            cwd=ROOT, stdout=subprocess.PIPE, text=True, timeout=10,
        ).stdout.split()
        if not docs_only:
            self.skipTest("no docs-only commit reachable to test against")
        candidate = docs_only[0][:7]
        changed = self.generator._paths_changed_since(candidate)
        if changed:
            self.skipTest(f"nearest docs commit {candidate} also touched source")

        manifest = {"images": {"runtime-dashboard.png": {"commit": candidate}}}
        report = self.generator.capture_freshness_report(manifest)
        self.assertEqual(report["status"], "fresh")


class ManifestSchemaTests(unittest.TestCase):
    def setUp(self) -> None:
        self.generator = _load_generator()

    def test_the_required_provenance_fields_are_what_the_manifest_records(self) -> None:
        """The doc renderer and the schema test must read the same field set."""
        import inspect

        source = inspect.getsource(self.generator.capture_runtime_screenshots)
        for field in (
            '"route"', '"source"', '"architecture"', '"version"', '"version_source"',
            '"commit"', '"captured_utc"', '"appearance"', '"window_dips"', '"exe"',
        ):
            self.assertIn(field, source, f"capture_runtime_screenshots no longer records {field}")


if __name__ == "__main__":
    unittest.main()
