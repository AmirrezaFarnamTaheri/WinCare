"""End-to-end contract for the documentation screenshot pipeline.

Runtime captures are e2e evidence: the app renders each documented route itself
(`--capture-screens`, mirroring the `--smoke-test` navigation loop), the generator
turns those renders into docs/images/*.png plus a provenance manifest, and
docs/Screenshots.md is rendered from that same manifest so the document cannot
drift from the images it describes. These gates pin every seam in that chain.
"""

from __future__ import annotations

import importlib.util
import json
import re
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


class ScreenshotPipelineContractTests(unittest.TestCase):
    def setUp(self) -> None:
        self.generator = _load_generator()

    def test_app_exposes_readonly_capture_mode(self) -> None:
        source = (ROOT / "src/WinCare.App/App.xaml.cs").read_text(encoding="utf-8")
        self.assertIn('"--capture-screens"', source)
        self.assertIn("CaptureRoutes", source)
        # Captures must stay read-only: the mode navigates and renders, never dispatches
        # mutations beyond what plugin initialization (same as the smoke test) requires.
        # Anchor on the method definition: the cref doc comment and the OnLaunched call
        # site also contain the name, so slicing from the first text hit checks nothing.
        capture_body = source[source.index("private static async Task RunCaptureScreensAsync"):]
        self.assertNotIn("ExecuteAsync", capture_body[: capture_body.index("InitializeRuntimeAsync")])

    def test_capture_routes_are_real_readonly_navigation_targets(self) -> None:
        catalog = (ROOT / "src/WinCare.Application/Navigation/NavigationCatalog.cs").read_text(encoding="utf-8")
        app_source = (ROOT / "src/WinCare.App/App.xaml.cs").read_text(encoding="utf-8")
        routes = re.findall(r'\("([a-z0-9-]+)",\s*\d+\)', app_source)
        self.assertTrue(routes, "no capture routes parsed from App.xaml.cs")
        for route in routes:
            self.assertIn(f'"{route}"', catalog, f"capture route {route} is not in NavigationCatalog")

    def test_generator_maps_routes_to_runtime_images(self) -> None:
        app_source = (ROOT / "src/WinCare.App/App.xaml.cs").read_text(encoding="utf-8")
        app_routes = re.findall(r'\("([a-z0-9-]+)",\s*\d+\)', app_source)
        generator_routes = {route for _, route in self.generator.RUNTIME_CAPTURE_IMAGES.values()}
        self.assertEqual(set(app_routes), generator_routes)
        self.assertEqual(
            sorted(self.generator.RUNTIME_CAPTURE_IMAGES),
            sorted(self.generator.REQUIRED_RUNTIME_IMAGES),
        )

    def test_runtime_manifest_records_policy_provenance(self) -> None:
        manifest_path = self.generator.RUNTIME_MANIFEST
        if not manifest_path.is_file():
            self.skipTest(
                "runtime capture manifest not present until tools/capture_screenshots.py --runtime runs"
            )
        manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
        self.assertEqual(
            sorted(manifest["images"]),
            sorted(self.generator.REQUIRED_RUNTIME_IMAGES),
        )
        for name, entry in manifest["images"].items():
            for field in ("route", "source", "architecture", "version", "commit", "captured_utc", "appearance"):
                self.assertTrue(entry.get(field), f"{name}: missing provenance field {field}")
            self.assertEqual(entry["source"], "portable", f"{name}: runtime evidence must come from a built artifact")
            self.assertRegex(entry["architecture"], r"^(x64|ARM64)$")

    def test_screenshots_doc_is_synced_to_the_generator_manifest(self) -> None:
        manifest_path = self.generator.RUNTIME_MANIFEST
        if not manifest_path.is_file():
            self.skipTest("runtime capture manifest not present until --runtime capture runs")
        manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
        doc = (ROOT / "docs/Screenshots.md").read_text(encoding="utf-8")
        self.assertEqual(self.generator.render_screenshots_doc(manifest), doc)

    def test_readme_only_labels_images_by_their_captured_version(self) -> None:
        readme = (ROOT / "README.md").read_text(encoding="utf-8")
        self.assertIn("docs/images/runtime-dashboard.png", readme)
        manifest_path = self.generator.RUNTIME_MANIFEST
        if manifest_path.is_file():
            manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
            version = manifest["images"]["runtime-dashboard.png"]["version"]
            caption_lines = [
                line for line in readme.splitlines()
                if "runtime-dashboard.png" in line or "runtime capture" in line.lower()
            ]
            joined = "\n".join(caption_lines)
            self.assertIn(f"v{version}", joined, "README capture caption does not name the manifest version")
            self.assertNotIn("2.5.0", joined, f"README still labels the capture v2.5.0 while manifest says v{version}")


if __name__ == "__main__":
    unittest.main()
