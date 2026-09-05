from __future__ import annotations

import re
import tempfile
import unittest
from pathlib import Path
from urllib.parse import unquote, urlsplit

from tools.finalize_native_release import stage_native_source


ROOT = Path(__file__).resolve().parents[2]
MARKDOWN_LINK = re.compile(r"!?\[[^\]]*\]\(([^)]+)\)")


def _local_markdown_targets(markdown: Path, staging_root: Path) -> list[tuple[str, Path]]:
    missing: list[tuple[str, Path]] = []
    text = markdown.read_text(encoding="utf-8")
    for raw_target in MARKDOWN_LINK.findall(text):
        target = raw_target.strip().split(maxsplit=1)[0].strip("<>")
        if not target or target.startswith("#"):
            continue
        parsed = urlsplit(target)
        if parsed.scheme or parsed.netloc:
            continue
        relative_path = unquote(parsed.path)
        if not relative_path:
            continue
        resolved = (staging_root / relative_path.lstrip("/")) if relative_path.startswith("/") else (markdown.parent / relative_path)
        if not resolved.exists():
            missing.append((raw_target, resolved))
    return missing


class ReleaseSourceCompletenessTests(unittest.TestCase):
    def test_native_source_archive_contains_security_and_reproducibility_evidence(self) -> None:
        with tempfile.TemporaryDirectory() as td:
            staged = Path(td) / "native-source"
            stage_native_source(ROOT, staged)

            required = (
                "NuGet.Config",
                ".github/CODEOWNERS",
                ".github/dependabot.yml",
                ".github/pull_request_template.md",
                "docs/Screenshots.md",
                "docs/diagrams/plugin-admission.html",
                "docs/images/runtime-dashboard.png",
                "tools/wincare-plugin-cli/package.json",
                "tools/wincare-plugin-cli/bin/wincare-plugin.js",
                "tests/tools/test_plugin_cli.py",
                "tests/WinCare.Infrastructure.Tests/WinCare.Infrastructure.Tests.csproj",
                "tests/WinCare.Infrastructure.Tests/PluginInstallerServiceTests.cs",
                "tests/WinCare.Infrastructure.Tests/packages.lock.json",
            )
            for relative in required:
                self.assertTrue((staged / relative).is_file(), relative)

            variants = list(staged.glob("src/*/packages.portable.*.lock.json"))
            self.assertEqual(10, len(variants))

            missing_links: list[str] = []
            for markdown in staged.rglob("*.md"):
                for raw_target, resolved in _local_markdown_targets(markdown, staged):
                    source = markdown.relative_to(staged).as_posix()
                    try:
                        destination = resolved.relative_to(staged).as_posix()
                    except ValueError:
                        destination = str(resolved)
                    missing_links.append(f"{source} -> {raw_target} ({destination})")
            self.assertEqual([], missing_links)


if __name__ == "__main__":
    unittest.main()
