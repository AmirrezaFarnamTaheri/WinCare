from __future__ import annotations

from pathlib import Path

ROOT = Path.cwd()


def read(path: str) -> str:
    return (ROOT / path).read_text(encoding="utf-8-sig")


def write(path: str, text: str) -> None:
    target = ROOT / path
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(text, encoding="utf-8", newline="\n")


def replace_once(path: str, old: str, new: str) -> None:
    text = read(path)
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"{path}: expected one occurrence, found {count}: {old[:140]!r}")
    write(path, text.replace(old, new, 1))


def replace_count(path: str, old: str, new: str, expected: int) -> None:
    text = read(path)
    count = text.count(old)
    if count != expected:
        raise RuntimeError(
            f"{path}: expected {expected} occurrences, found {count}: {old[:140]!r}"
        )
    write(path, text.replace(old, new))


# Installer shortcuts explicitly own and verify their branded icon source.
shortcuts = "src/WinCare/Install/Private/30-Shortcuts.ps1"
replace_once(
    shortcuts,
    "param([string]$ShortcutPath,[string]$ExpectedTarget,[AllowEmptyString()][string]$ExpectedArguments='',[string]$ExpectedWorkingDirectory)",
    "param([string]$ShortcutPath,[string]$ExpectedTarget,[AllowEmptyString()][string]$ExpectedArguments='',[string]$ExpectedWorkingDirectory,[string]$ExpectedIconLocation)",
)
replace_once(
    shortcuts,
    "([IO.Path]::GetFullPath($shortcut.TargetPath) -eq [IO.Path]::GetFullPath($ExpectedTarget)) -and ([string]$shortcut.Arguments -eq $ExpectedArguments) -and ([IO.Path]::GetFullPath($shortcut.WorkingDirectory) -eq [IO.Path]::GetFullPath($ExpectedWorkingDirectory))",
    "([IO.Path]::GetFullPath($shortcut.TargetPath) -eq [IO.Path]::GetFullPath($ExpectedTarget)) -and ([string]$shortcut.Arguments -eq $ExpectedArguments) -and ([IO.Path]::GetFullPath($shortcut.WorkingDirectory) -eq [IO.Path]::GetFullPath($ExpectedWorkingDirectory)) -and ([string]::Equals([string]$shortcut.IconLocation,[string]$ExpectedIconLocation,[StringComparison]::OrdinalIgnoreCase))",
)
replace_once(
    shortcuts,
    "$records=@([ordered]@{Name='WinCare.lnk';Target=Join-Path $Destination 'WinCare-GUI.exe';Arguments = '';WorkingDirectory = $Destination},[ordered]@{Name='WinCare Console.lnk';Target=Join-Path $Destination 'WinCare-TUI.exe';Arguments = '';WorkingDirectory = $Destination});$shell=$null",
    "$records=@([ordered]@{Name='WinCare.lnk';Target=Join-Path $Destination 'WinCare-GUI.exe';Arguments='';WorkingDirectory=$Destination;IconLocation=(Join-Path $Destination 'WinCare-GUI.exe')+',0'},[ordered]@{Name='WinCare Console.lnk';Target=Join-Path $Destination 'WinCare-TUI.exe';Arguments='';WorkingDirectory=$Destination;IconLocation=(Join-Path $Destination 'WinCare-TUI.exe')+',0'});$shell=$null",
)
replace_once(
    shortcuts,
    "$shortcut.TargetPath=$r.Target;$shortcut.Arguments=$r.Arguments;$shortcut.WorkingDirectory=$r.WorkingDirectory;$shortcut.Save()",
    "$shortcut.TargetPath=$r.Target;$shortcut.Arguments=$r.Arguments;$shortcut.WorkingDirectory=$r.WorkingDirectory;$shortcut.IconLocation=$r.IconLocation;$shortcut.Save()",
)
replace_once(
    shortcuts,
    "Test-WinCareShortcutIdentity (Join-Path $folder $r.Name) $r.Target $r.Arguments $r.WorkingDirectory",
    "Test-WinCareShortcutIdentity (Join-Path $folder $r.Name) $r.Target $r.Arguments $r.WorkingDirectory $r.IconLocation",
)
replace_count(
    shortcuts,
    "$working=if($r.WorkingDirectory){[string]$r.WorkingDirectory}else{$Destination};if(!(Test-WinCareShortcutIdentity (Join-Path $folder $r.Name) $r.Target $arguments $working))",
    "$working=if($r.WorkingDirectory){[string]$r.WorkingDirectory}else{$Destination};$iconLocation=if($r.IconLocation){[string]$r.IconLocation}else{([string]$r.Target)+',0'};if(!(Test-WinCareShortcutIdentity (Join-Path $folder $r.Name) $r.Target $arguments $working $iconLocation))",
    3,
)

# Standalone outputs carry the complete brand packet and prove icon identity.
build_exe = "tools/Build-Exe.ps1"
replace_once(
    build_exe,
    "$iconEvidence=Get-WinCareToolingFileSha256 -LiteralPath $iconPath -MaximumBytes 1048576 -Purpose 'WinCare application icon'",
    "$iconEvidence=Get-WinCareToolingFileSha256 -LiteralPath $iconPath -MaximumBytes 1048576 -Purpose 'WinCare application icon'\n"
    "$brandManifestPath=(Resolve-Path -LiteralPath (Join-Path $rootPath 'src\\WinCare\\Data\\Gui\\WinCare.Brand.json') -ErrorAction Stop).Path\n"
    "$brandManifest=Read-WinCareToolingJson -LiteralPath $brandManifestPath -MaximumBytes 1048576 -MaximumDepth 24\n"
    "if([string]$brandManifest.schema -ne 'wincare.brand/v1'){throw 'WinCare brand manifest schema is invalid.'}\n"
    "if(([string]$brandManifest.assets.appIcon.sha256).ToLowerInvariant() -ne $iconEvidence.Sha256){throw 'Canonical icon hash does not match the WinCare brand manifest.'}\n"
    "$brandManifestEvidence=Get-WinCareToolingFileSha256 -LiteralPath $brandManifestPath -MaximumBytes 1048576 -Purpose 'WinCare brand manifest'",
)
replace_once(
    build_exe,
    "        IconSha256 = $iconEvidence.Sha256\n        IconFrameSizes = @($iconFrameSizes)\n        Artifacts = @($records)",
    "        IconSha256 = $iconEvidence.Sha256\n"
    "        IconFrameSizes = @($iconFrameSizes)\n"
    "        BrandSchema = [string]$brandManifest.schema\n"
    "        BrandManifestSha256 = $brandManifestEvidence.Sha256\n"
    "        Artifacts = @($records)",
)
replace_once(
    build_exe,
    "    Write-WinCareToolingAtomicJson -LiteralPath $manifestPath -Value $manifest -MaximumBytes 1048576 -Depth 12\n    Write-WinCareStandaloneTrace -Phase 'complete'",
    "    Write-WinCareToolingAtomicJson -LiteralPath $manifestPath -Value $manifest -MaximumBytes 1048576 -Depth 12\n"
    "    foreach($brandAssetName in @('WinCare.Brand.json','WinCare-Mark.svg','WinCare-Logo.svg','WinCare-Logo.png','WinCare-Logo-256.png','WinCare.ico')){\n"
    "        $brandAssetPath=Join-Path (Split-Path -Parent $brandManifestPath) $brandAssetName\n"
    "        $null=Get-WinCareToolingRegularFile -LiteralPath $brandAssetPath -MaximumBytes 16777216 -Purpose \"WinCare brand asset $brandAssetName\"\n"
    "        Copy-Item -LiteralPath $brandAssetPath -Destination (Join-Path $outputPath $brandAssetName) -ErrorAction Stop\n"
    "    }\n"
    "    Write-WinCareStandaloneTrace -Phase 'complete'",
)

# Add deterministic brand generation and verification to Windows validation.
validation = "tools/Invoke-WindowsValidation.ps1"
replace_once(
    validation,
    "    $static = Invoke-ValidationGate -Name '01-static' -Script @\"",
    "    $brand = Invoke-ValidationGate -Name '00-brand' -Script \"& '$quotedRoot\\tools\\Generate-WinCareBrand.ps1' -Root '$quotedRoot' -Check; & '$quotedRoot\\tools\\Test-WinCareBrand.ps1' -Root '$quotedRoot' -OutputPath '$quotedOutput\\brand-validation.json'\"\n\n"
    "    $static = Invoke-ValidationGate -Name '01-static' -Script @\"",
)

# Permanent workflow validates and inventories the same brand packet.
workflow = ".github/workflows/windows-release-validation.yml"
replace_once(
    workflow,
    "              'WinCare-TUI.exe'\n          )",
    "              'WinCare-TUI.exe',\n"
    "              'WinCare.Brand.json',\n"
    "              'WinCare-Mark.svg',\n"
    "              'WinCare-Logo.svg',\n"
    "              'WinCare-Logo.png',\n"
    "              'WinCare-Logo-256.png',\n"
    "              'WinCare.ico'\n"
    "          )",
)
replace_once(
    workflow,
    "          $assets = @(\n              Get-ChildItem -LiteralPath $release -File -Force |",
    "          $brandManifest = [IO.File]::ReadAllText((Join-Path $release 'WinCare.Brand.json'),[Text.UTF8Encoding]::new($false,$true)) | ConvertFrom-Json -Depth 24\n"
    "          if ([string]$brandManifest.schema -ne 'wincare.brand/v1') { throw 'Release brand manifest schema is invalid.' }\n"
    "          $standaloneManifest = [IO.File]::ReadAllText((Join-Path $release 'WinCare.Standalone.build.json'),[Text.UTF8Encoding]::new($false,$true)) | ConvertFrom-Json -Depth 24\n"
    "          $expectedIconSha256 = ([string]$brandManifest.assets.appIcon.sha256).ToLowerInvariant()\n"
    "          if (([string]$standaloneManifest.IconSha256).ToLowerInvariant() -ne $expectedIconSha256) { throw 'Standalone brand icon identity does not match the release brand manifest.' }\n"
    "          foreach ($artifact in @($standaloneManifest.Artifacts)) {\n"
    "              if (-not [bool]$artifact.EmbeddedIconVerified) { throw \"Embedded icon was not verified: $($artifact.Name)\" }\n"
    "              if (([string]$artifact.IconSha256).ToLowerInvariant() -ne $expectedIconSha256) { throw \"Embedded icon hash mismatch: $($artifact.Name)\" }\n"
    "          }\n"
    "          $assets = @(\n              Get-ChildItem -LiteralPath $release -File -Force |",
)

# Strong source-level identity tests.
release_test = "tools/test_release_identity.py"
replace_once(release_test, "import unittest\n", "import hashlib\nimport json\nimport unittest\n")
replace_once(
    release_test,
    "        self.assertIn(\"icon pixels do not match\", build)\n",
    "        self.assertIn(\"icon pixels do not match\", build)\n"
    "        self.assertIn(\"WinCare.Brand.json\", build)\n"
    "        self.assertIn(\"BrandManifestSha256\", build)\n\n"
    "    def test_brand_manifest_hashes_every_canonical_asset(self) -> None:\n"
    "        directory = ROOT / \"src\" / \"WinCare\" / \"Data\" / \"Gui\"\n"
    "        manifest = json.loads((directory / \"WinCare.Brand.json\").read_text(encoding=\"utf-8\"))\n"
    "        self.assertEqual(manifest[\"schema\"], \"wincare.brand/v1\")\n"
    "        for entry in manifest[\"assets\"].values():\n"
    "            path = directory / entry[\"path\"]\n"
    "            self.assertTrue(path.is_file(), path)\n"
    "            self.assertEqual(hashlib.sha256(path.read_bytes()).hexdigest(), entry[\"sha256\"], path.name)\n\n"
    "    def test_installer_shortcuts_pin_logo_bearing_icon_locations(self) -> None:\n"
    "        shortcuts = self.read(\"src/WinCare/Install/Private/30-Shortcuts.ps1\")\n"
    "        self.assertIn(\"ExpectedIconLocation\", shortcuts)\n"
    "        self.assertIn(\"$shortcut.IconLocation=$r.IconLocation\", shortcuts)\n"
    "        self.assertIn(\"WinCare-GUI.exe')+',0'\", shortcuts)\n"
    "        self.assertIn(\"WinCare-TUI.exe')+',0'\", shortcuts)\n",
)

print("WinCare brand identity contracts applied.")
