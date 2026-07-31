#!/usr/bin/env python3
"""Align WinCare brand closure with the canonical release v3 implementation."""
from __future__ import annotations

import re
from pathlib import Path

ROOT = Path.cwd().resolve()


def read(path: Path) -> str:
    return path.read_text(encoding="utf-8-sig")


def write(path: Path, text: str) -> None:
    path.write_text(text, encoding="utf-8", newline="\n")


def regex_once(text: str, pattern: str, replacement: str) -> str:
    updated, count = re.subn(
        pattern,
        lambda _match: replacement,
        text,
        count=1,
        flags=re.DOTALL,
    )
    if count != 1:
        raise RuntimeError(f"expected one regex match: {pattern!r}")
    return updated


brand_path = ROOT / ".wincare-finalize" / "brand.py"
brand = read(brand_path)
release_function = r'''def patch_release_finalizer() -> None:
    path = ROOT / "tools" / "finalize_release.py"
    text = read_text(path)
    if MARKER not in text:
        anchor = '    manifest_path = executable_directory / "WinCare.Standalone.build.json"\n'
        block = '''    # wincare.brand.integration/v1
    brand_asset_names = (
        "design/WinCare-Logo.svg",
        "design/WinCare-Wordmark.svg",
        "design/WinCare-Logo-512.png",
        "design/WinCare.ico",
        "design/BRAND.md",
        "design/WinCare-Brand.manifest.json",
    )
    missing_brand_assets = [name for name in brand_asset_names if name not in package_inputs]
    if missing_brand_assets:
        raise ValueError("release omits canonical brand assets: " + ", ".join(missing_brand_assets))
    brand_manifest_bytes = package_inputs["design/WinCare-Brand.manifest.json"]
    try:
        brand_manifest = json.loads(brand_manifest_bytes.decode("utf-8-sig"))
    except (UnicodeDecodeError, json.JSONDecodeError) as error:
        raise ValueError("WinCare brand manifest is invalid") from error
    if not isinstance(brand_manifest, dict) or brand_manifest.get("schema") != "wincare.brand.identity/v1":
        raise ValueError("unsupported WinCare brand manifest schema")
    if brand_manifest.get("product") != "WinCare":
        raise ValueError("WinCare brand manifest product mismatch")
    icon_sizes = brand_manifest.get("iconSizes")
    if icon_sizes != [16, 20, 24, 32, 40, 48, 64, 128, 256]:
        raise ValueError("WinCare brand manifest icon-size contract mismatch")
    manifest_records = brand_manifest.get("assets")
    if not isinstance(manifest_records, list):
        raise ValueError("WinCare brand manifest asset records are missing")
    record_by_path = {
        str(record.get("path")): record
        for record in manifest_records
        if isinstance(record, dict) and isinstance(record.get("path"), str)
    }
    expected_manifest_assets = set(brand_asset_names) - {"design/WinCare-Brand.manifest.json"}
    if set(record_by_path) != expected_manifest_assets or len(record_by_path) != len(manifest_records):
        raise ValueError("WinCare brand manifest membership mismatch")
    closed_brand_assets = []
    for name in sorted(expected_manifest_assets, key=str.casefold):
        data = package_inputs[name]
        record = record_by_path[name]
        observed_hash = sha256(data)
        if record.get("bytes") != len(data) or str(record.get("sha256", "")).lower() != observed_hash:
            raise ValueError(f"WinCare brand manifest evidence mismatch: {name}")
        closed_brand_assets.append({"path": name, "bytes": len(data), "sha256": observed_hash})
    brand_icon_sha256 = str(record_by_path["design/WinCare.ico"]["sha256"]).lower()
    brand = {
        "schema": "wincare.brand.identity/v1",
        "manifest": {
            "path": "design/WinCare-Brand.manifest.json",
            "sha256": sha256(brand_manifest_bytes),
            "bytes": len(brand_manifest_bytes),
        },
        "iconSizes": icon_sizes,
        "iconSha256": brand_icon_sha256,
        "assets": closed_brand_assets,
    }

'''
        text = replace_required(text, anchor, block + anchor)

    manifest_anchor = '    if set(record_by_name) != set(EXPECTED_EXES) or len(record_by_name) != len(manifest_records):\n        raise ValueError("standalone build manifest artifact membership mismatch")\n'
    manifest_check = '''    if set(record_by_name) != set(EXPECTED_EXES) or len(record_by_name) != len(manifest_records):
        raise ValueError("standalone build manifest artifact membership mismatch")
    if str(standalone_manifest.get("IconSha256", "")).lower() != brand_icon_sha256:
        raise ValueError("standalone build manifest icon identity does not match the canonical brand")
    for name, record in record_by_name.items():
        if record.get("EmbeddedIconVerified") is not True:
            raise ValueError(f"standalone executable icon was not verified: {name}")
        if str(record.get("IconSha256", "")).lower() != brand_icon_sha256:
            raise ValueError(f"standalone executable icon identity mismatch: {name}")
'''
    if 'standalone executable icon identity mismatch' not in text:
        text = replace_required(text, manifest_anchor, manifest_check)

    build_anchor = '        "packageInputs": {"treeSha256": package_hash, "members": len(package_inputs)},\n'
    if '        "brand": brand,\n' not in text:
        text = replace_required(text, build_anchor, build_anchor + '        "brand": brand,\n')

    validation_anchor = '            "standaloneHosts": "source-built-and-verified",\n'
    if '            "brandIdentity": "verified",\n' not in text:
        text = replace_required(
            text,
            validation_anchor,
            validation_anchor + '            "brandIdentity": "verified",\n',
        )

    release_anchor = '        "packageProfile": "production",\n        "archive":'
    release_replacement = '        "packageProfile": "production",\n        "brand": brand,\n        "archive":'
    if release_replacement not in text:
        text = replace_required(text, release_anchor, release_replacement)

    result_anchor = '        "version": version,\n        "archive": str(archive_path),\n'
    result_replacement = '        "version": version,\n        "brand": brand,\n        "archive": str(archive_path),\n'
    if result_replacement not in text:
        text = replace_required(text, result_anchor, result_replacement)

    output_anchor = '    (output_directory / "WinCare.Standalone.build.json").write_bytes(manifest_bytes)\n'
    output_line = '    (output_directory / f"WinCare-{version}-BRAND.json").write_bytes(brand_manifest_bytes)\n'
    if output_line not in text:
        text = replace_required(text, output_anchor, output_anchor + output_line)

    result_path_anchor = '        "standaloneManifest": str(output_directory / "WinCare.Standalone.build.json"),\n'
    result_path_line = '        "brandManifest": str(output_directory / f"WinCare-{version}-BRAND.json"),\n'
    if result_path_line not in text:
        text = replace_required(text, result_path_anchor, result_path_anchor + result_path_line)

    provenance_anchor = '                "externalParameters": {"version": version, "runtimeIdentifier": "win-x64"},\n'
    provenance_replacement = '''                "externalParameters": {
                    "version": version,
                    "runtimeIdentifier": "win-x64",
                    "brandManifestSha256": brand["manifest"]["sha256"],
                },
'''
    if '"brandManifestSha256": brand["manifest"]["sha256"]' not in text:
        text = replace_required(text, provenance_anchor, provenance_replacement)

    write_text(path, text)


def patch_windows_workflow() -> None:
    path = ROOT / ".github" / "workflows" / "windows-release-validation.yml"
    text = read_text(path)
    brand_required = '              "WinCare-$version-BRAND.json",\n'
    if brand_required not in text:
        anchor = '              "WinCare-$version-build-result.json",\n'
        text = replace_required(text, anchor, anchor + brand_required)

    verification_marker = "Release brand manifest schema is invalid."
    if verification_marker not in text:
        anchor = '          $assets = @(\n'
        block = '''          $brandPath = Join-Path $release "WinCare-$version-BRAND.json"
          $brand = [IO.File]::ReadAllText(
              $brandPath,
              [Text.UTF8Encoding]::new($false, $true)
          ) | ConvertFrom-Json -Depth 24
          if ([string]$brand.schema -ne 'wincare.brand.identity/v1') {
              throw 'Release brand manifest schema is invalid.'
          }
          $brandIconRecord = @(
              $brand.assets | Where-Object { [string]$_.path -eq 'design/WinCare.ico' }
          )
          if ($brandIconRecord.Count -ne 1) {
              throw 'Release brand manifest does not contain exactly one canonical icon record.'
          }
          $brandIconSha256 = ([string]$brandIconRecord[0].sha256).ToLowerInvariant()
          $standalone = [IO.File]::ReadAllText(
              (Join-Path $release 'WinCare.Standalone.build.json'),
              [Text.UTF8Encoding]::new($false, $true)
          ) | ConvertFrom-Json -Depth 24
          if (([string]$standalone.IconSha256).ToLowerInvariant() -ne $brandIconSha256) {
              throw 'Standalone manifest icon identity does not match the release brand.'
          }
          foreach ($artifact in @($standalone.Artifacts)) {
              if (-not [bool]$artifact.EmbeddedIconVerified) {
                  throw "Embedded icon was not verified: $($artifact.Name)"
              }
              if (([string]$artifact.IconSha256).ToLowerInvariant() -ne $brandIconSha256) {
                  throw "Embedded icon identity mismatch: $($artifact.Name)"
              }
          }
          $brandManifestSha256 = (Get-FileHash -LiteralPath $brandPath -Algorithm SHA256).Hash.ToLowerInvariant()
'''
        text = replace_required(text, anchor, block + anchor)

    inventory_anchor = "                  previousFixtureVersion = '${{ steps.previous.outputs.version }}'\n"
    brand_inventory = '''                  brand = [ordered]@{
                      schema = [string]$brand.schema
                      manifestSha256 = $brandManifestSha256
                      iconSha256 = $brandIconSha256
                      iconSizes = @($brand.iconSizes)
                  }
'''
    if "                  brand = [ordered]@{\n" not in text:
        text = replace_required(text, inventory_anchor, inventory_anchor + brand_inventory)

    text = text.replace(
        "pester = [string](Get-Module Pester).Version",
        "pester = [string]((Get-Module -ListAvailable Pester | Where-Object Version -ge ([version]'5.5.0') | Sort-Object Version -Descending | Select-Object -First 1).Version)",
    )
    text = text.replace(
        "psscriptAnalyzer = [string](Get-Module PSScriptAnalyzer).Version",
        "psscriptAnalyzer = [string]((Get-Module -ListAvailable PSScriptAnalyzer | Where-Object Version -ge ([version]'1.24.0') | Sort-Object Version -Descending | Select-Object -First 1).Version)",
    )
    write_text(path, text)

'''
brand = regex_once(
    brand,
    r"def patch_release_finalizer\(\) -> None:\n.*?\n\ndef verify_project_icon_contract",
    release_function + "\ndef verify_project_icon_contract",
)
brand = brand.replace(
    "    patch_release_finalizer()\n    verify_project_icon_contract(icon)",
    "    patch_release_finalizer()\n    patch_windows_workflow()\n    verify_project_icon_contract(icon)",
    1,
)
write(brand_path, brand)

brand_test_path = ROOT / "tools" / "test_brand_identity.py"
brand_test = read(brand_test_path)
brand_test = brand_test.replace(
    'ROOT / "tools" / "finalize_standalone_release.py"',
    'ROOT / "tools" / "finalize_release.py"',
)
write(brand_test_path, brand_test)

pipeline_test_path = ROOT / "tools" / "test_windows_release_pipeline.py"
pipeline_test = read(pipeline_test_path)
pipeline_test = pipeline_test.replace(
    'ROOT / "src" / "WinCare" / "Data" / "Gui" / "WinCare.Brand.json"',
    'ROOT / "design" / "WinCare-Brand.manifest.json"',
)
pipeline_test = pipeline_test.replace(
    'self.assertEqual(manifest["schema"], "wincare.brand/v1")',
    'self.assertEqual(manifest["schema"], "wincare.brand.identity/v1")',
)
pipeline_test = pipeline_test.replace(
    'manifest["assets"]["appIcon"]["frames"],\n            [16, 24, 32, 48, 64, 128, 256],',
    'manifest["iconSizes"],\n            [16, 20, 24, 32, 40, 48, 64, 128, 256],',
)
pipeline_test = pipeline_test.replace(
    'self.assertIn("WinCare.Brand.json", workflow)',
    'self.assertIn("-BRAND.json", workflow)',
)
write(pipeline_test_path, pipeline_test)

print("Aligned WinCare brand closure with tools/finalize_release.py and permanent CI.")
