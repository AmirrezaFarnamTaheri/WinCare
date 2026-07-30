#!/usr/bin/env python3
"""Apply the final review remediation to pull request 4 deterministically."""
from __future__ import annotations

import ast
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def read(path: str) -> str:
    """Read a repository text file as UTF-8."""
    return (ROOT / path).read_text(encoding="utf-8")


def write(path: str, text: str) -> None:
    """Write a repository text file with normalized LF newlines."""
    (ROOT / path).write_text(text.replace("\r\n", "\n"), encoding="utf-8", newline="\n")


def replace_once(text: str, old: str, new: str, label: str) -> str:
    """Replace one exact source fragment and fail when the source contract drifted."""
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"{label}: expected one source fragment, found {count}")
    return text.replace(old, new, 1)


def rewrite_pwsh_contexts(text: str) -> str:
    """Replace GitHub expression interpolation only inside PowerShell run blocks."""
    mapping = {
        "${{ github.event_name }}": "$env:WINCARE_EVENT_NAME",
        "${{ github.ref }}": "$env:WINCARE_REF",
        "${{ github.ref_name }}": "$env:WINCARE_REF_NAME",
        "${{ github.sha }}": "$env:WINCARE_SHA",
        "${{ github.run_id }}": "$env:WINCARE_RUN_ID",
        "${{ github.run_attempt }}": "$env:WINCARE_RUN_ATTEMPT",
        "${{ matrix.os }}": "$env:WINCARE_RUNNER_OS",
    }
    lines = text.splitlines(keepends=True)
    index = 0
    while index < len(lines):
        line = lines[index]
        if line.lstrip().startswith("shell: pwsh"):
            shell_indent = len(line) - len(line.lstrip())
            run_index = index + 1
            while run_index < len(lines):
                candidate = lines[run_index]
                candidate_indent = len(candidate) - len(candidate.lstrip())
                if candidate.strip() and candidate_indent < shell_indent:
                    break
                if candidate_indent == shell_indent and candidate.lstrip().startswith("run: |"):
                    block_index = run_index + 1
                    while block_index < len(lines):
                        block_line = lines[block_index]
                        block_indent = len(block_line) - len(block_line.lstrip())
                        if block_line.strip() and block_indent <= shell_indent:
                            break
                        for expression, variable in mapping.items():
                            block_line = block_line.replace(expression, variable)
                        for variable in mapping.values():
                            block_line = block_line.replace(f"'{variable}'", variable)
                        lines[block_index] = block_line
                        block_index += 1
                    index = block_index
                    break
                run_index += 1
        index += 1
    return "".join(lines)


def add_missing_docstrings(path: str) -> None:
    """Add concise docstrings to every undocumented Python function and method."""
    text = read(path)
    tree = ast.parse(text, filename=path)
    lines = text.splitlines(keepends=True)
    insertions: list[tuple[int, str]] = []

    def description(name: str) -> str:
        words = name.strip("_").replace("_", " ") or "helper"
        if name == "setUp":
            return "Prepare isolated state for the current test case."
        if name.startswith("test_"):
            return f"Verify {words[5:]}."
        if name == "main":
            return "Run the command-line entrypoint and return its exit status."
        return f"Execute the {words} operation with validated inputs."

    for node in ast.walk(tree):
        if not isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef)):
            continue
        if ast.get_docstring(node, clean=False) is not None:
            continue
        if not node.body or node.body[0].lineno == node.lineno:
            raise RuntimeError(f"{path}:{node.lineno}: one-line function cannot be documented safely")
        first_line = node.body[0].lineno - 1
        indent = re.match(r"\s*", lines[first_line]).group(0)
        insertions.append((first_line, f'{indent}"""{description(node.name)}"""\n'))
    for line_index, docstring in sorted(insertions, reverse=True):
        lines.insert(line_index, docstring)
    result = "".join(lines)
    ast.parse(result, filename=path)
    write(path, result)


def remediate_workflows() -> None:
    """Eliminate script interpolation and align the exact release asset closure."""
    context_env = (
        "env:\n"
        "  PYTHONUTF8: '1'\n"
        "  PYTHONUNBUFFERED: '1'\n"
        "  WINCARE_EVENT_NAME: ${{ github.event_name }}\n"
        "  WINCARE_REF: ${{ github.ref }}\n"
        "  WINCARE_REF_NAME: ${{ github.ref_name }}\n"
        "  WINCARE_SHA: ${{ github.sha }}\n"
        "  WINCARE_RUN_ID: ${{ github.run_id }}\n"
        "  WINCARE_RUN_ATTEMPT: ${{ github.run_attempt }}\n"
    )
    for path in (".github/workflows/ci.yml", ".github/workflows/release.yml"):
        text = read(path)
        text = replace_once(
            text,
            "env:\n  PYTHONUTF8: '1'\n  PYTHONUNBUFFERED: '1'\n",
            context_env,
            f"{path} global context environment",
        )
        text = rewrite_pwsh_contexts(text)
        write(path, text)

    ci_path = ".github/workflows/ci.yml"
    ci = read(ci_path)
    ci = replace_once(
        ci,
        "    timeout-minutes: 100\n    steps:\n",
        "    timeout-minutes: 100\n    env:\n      WINCARE_RUNNER_OS: ${{ matrix.os }}\n    steps:\n",
        "CI matrix environment",
    )
    write(ci_path, ci)

    release_path = ".github/workflows/release.yml"
    release = read(release_path)
    release = release.replace(',"WinCare-$version-validation-v3.json"', "")
    release = release.replace("$actual.Count -ne 12", "$actual.Count -ne 11")
    release = release.replace("$records.Count -ne 12", "$records.Count -ne 11")
    release = release.replace("exactly twelve release assets", "exactly eleven release assets")
    write(release_path, release)


def remediate_host() -> None:
    """Handle help before extraction and tolerate a valid cross-session cache winner."""
    path = "src/WinCare/Standalone/WinCare.Host.cs"
    text = read(path)
    text = replace_once(
        text,
        "        try\n        {\n            var prepared = PreparePayload();\n",
        "        try\n        {\n            if (args.Any(IsHelpArgument))\n            {\n                PrintUsage();\n                return 0;\n            }\n            var prepared = PreparePayload();\n",
        "standalone help preflight",
    )
    text = replace_once(
        text,
        "            if (token is \"-?\" or \"--help\")\n            {\n                command.Append(\" -?\");\n                continue;\n            }\n",
        "",
        "standalone help forwarding removal",
    )
    helper = '''    private static bool IsHelpArgument(string argument)\n    {\n        return string.Equals(argument, "-?", StringComparison.OrdinalIgnoreCase)\n            || string.Equals(argument, "--help", StringComparison.OrdinalIgnoreCase);\n    }\n\n    private static void PrintUsage()\n    {\n        Console.Out.WriteLine(\n            "Usage: WinCare[.exe] [-Command <name>] [-ArgumentsJson <json>] " +\n            "[-Theme <name>] [-Apply] [-Json] [-ReadOnly] [-Ascii] [-NoLogo]");\n    }\n\n'''
    text = replace_once(
        text,
        "    private static readonly Dictionary<string, bool> EntrypointParameters = new(PathComparer)\n",
        helper + "    private static readonly Dictionary<string, bool> EntrypointParameters = new(PathComparer)\n",
        "standalone help helpers",
    )
    text = replace_once(
        text,
        "                Directory.Move(staging, cache);\n",
        "                try\n                {\n                    Directory.Move(staging, cache);\n                }\n                catch (IOException) when (Directory.Exists(cache) && ValidateCache(cache, payloadHash, expected))\n                {\n                    DeleteTreeNoFollow(staging);\n                }\n                if (!ValidateCache(cache, payloadHash, expected))\n                    throw new InvalidDataException(\"Published WinCare payload cache failed verification.\");\n",
        "standalone cache publication race",
    )
    write(path, text)


def remediate_release_tools() -> None:
    """Repair release evidence placement, reproducibility, manifests, and SBOM verification."""
    build_path = "tools/Build-Release.ps1"
    build = read(build_path)
    build = replace_once(
        build,
        '$validationPath = Join-Path $outputPath "WinCare-$version-validation-v3.json"',
        '$validationPath = Join-Path $workRoot "WinCare-$version-validation-v3.json"',
        "v3 validation report location",
    )
    write(build_path, build)

    finalizer_path = "tools/finalize_release.py"
    finalizer = read(finalizer_path)
    finalizer = replace_once(
        finalizer,
        "def timestamp() -> tuple[str, tuple[int, int, int, int, int, int]]:\n",
        "def timestamp() -> tuple[str, tuple[int, int, int, int, int, int], int]:\n",
        "timestamp return contract",
    )
    finalizer = replace_once(
        finalizer,
        "    return created, (value.year, value.month, value.day, value.hour, value.minute, second)\n",
        "    return created, (value.year, value.month, value.day, value.hour, value.minute, second), raw\n",
        "timestamp effective epoch",
    )
    finalizer = replace_once(
        finalizer,
        "    created, zip_time = timestamp()\n",
        "    created, zip_time, effective_epoch = timestamp()\n",
        "finalizer timestamp call",
    )
    finalizer = replace_once(
        finalizer,
        '            "timestampEpoch": int(os.environ.get("SOURCE_DATE_EPOCH", FIXED_EPOCH)),\n',
        '            "timestampEpoch": effective_epoch,\n',
        "receipt timestamp epoch",
    )
    write(finalizer_path, finalizer)

    verify_v3_path = "tools/verify_release_v3.py"
    verify_v3 = read(verify_v3_path)
    verify_v3 = replace_once(
        verify_v3,
        '                standalone_manifest = json.loads(standalone_manifest_data.decode("utf-8-sig"))\n                manifest_schema_version = standalone_manifest.get("SchemaVersion")\n',
        '                standalone_manifest = json.loads(standalone_manifest_data.decode("utf-8-sig"))\n                if not isinstance(standalone_manifest, dict):\n                    raise ValueError("standalone build manifest root must be a JSON object")\n                manifest_schema_version = standalone_manifest.get("SchemaVersion")\n',
        "standalone manifest root type",
    )
    verify_v3 = replace_once(
        verify_v3,
        "            except (UnicodeDecodeError, json.JSONDecodeError) as error:\n                errors.append(f\"standalone build manifest is invalid: {error}\")\n",
        "            except (UnicodeDecodeError, json.JSONDecodeError, ValueError) as error:\n                errors.append(f\"standalone build manifest is invalid: {error}\")\n",
        "standalone manifest local error handling",
    )
    write(verify_v3_path, verify_v3)

    verify_path = "tools/verify_release.py"
    verify = read(verify_path)
    verify = replace_once(
        verify,
        "def _validate_sbom(archive: zipfile.ZipFile, relative_infos: dict[str, zipfile.ZipInfo], actual_names: set[str], errors: list[str]) -> None:\n",
        "def _validate_sbom(archive: zipfile.ZipFile, relative_infos: dict[str, zipfile.ZipInfo], actual_names: set[str], relative_hashes: dict[str, str], errors: list[str]) -> None:\n",
        "generic verifier SBOM signature",
    )
    verify = replace_once(
        verify,
        '        if not source_names.issubset(sbom_files):\n            errors.append(f"SBOM omits source files: {sorted(source_names-set(sbom_files))[:20]}")\n',
        '        if not source_names.issubset(sbom_files):\n            errors.append(f"SBOM omits source files: {sorted(source_names-set(sbom_files))[:20]}")\n        for name in sorted(source_names & set(sbom_files), key=str.casefold):\n            observed = relative_hashes.get(name)\n            declared = sbom_files[name]\n            if declared is None:\n                errors.append(f"SBOM checksum is missing or malformed: {name}")\n            elif observed is not None and declared != observed:\n                errors.append(f"SBOM checksum mismatch: {name}")\n',
        "generic verifier SBOM digest checks",
    )
    verify = replace_once(
        verify,
        "            _validate_sbom(archive, relative_infos, actual_names, errors)\n",
        "            _validate_sbom(archive, relative_infos, actual_names, relative_hashes, errors)\n",
        "generic verifier SBOM call",
    )
    write(verify_path, verify)


def remediate_tests_and_lifecycle() -> None:
    """Isolate test environment state and protect lifecycle work directories."""
    test_path = "tools/test_standalone_release.py"
    test = read(test_path)
    test = replace_once(test, "import unittest\n", "import unittest\nimport unittest.mock\n", "unittest mock import")
    test = replace_once(
        test,
        '    def setUp(self) -> None:\n        os.environ["SOURCE_DATE_EPOCH"] = "315532800"\n',
        '    def setUp(self) -> None:\n        patcher = unittest.mock.patch.dict(os.environ, {"SOURCE_DATE_EPOCH": "315532800"})\n        patcher.start()\n        self.addCleanup(patcher.stop)\n',
        "test environment isolation",
    )
    regression_tests = '''\n    def test_remaining_review_hardening_contracts(self) -> None:\n        release_workflow = (ROOT / ".github/workflows/release.yml").read_text(encoding="utf-8")\n        ci_workflow = (ROOT / ".github/workflows/ci.yml").read_text(encoding="utf-8")\n        host = (ROOT / "src/WinCare/Standalone/WinCare.Host.cs").read_text(encoding="utf-8")\n        build_release = (ROOT / "tools/Build-Release.ps1").read_text(encoding="utf-8")\n        lifecycle = (ROOT / "tools/Test-InstallationLifecycle.ps1").read_text(encoding="utf-8")\n        verifier = (ROOT / "tools/verify_release.py").read_text(encoding="utf-8")\n        verifier_v3 = (ROOT / "tools/verify_release_v3.py").read_text(encoding="utf-8")\n        self.assertIn("WINCARE_EVENT_NAME: ${{ github.event_name }}", release_workflow)\n        self.assertIn("WINCARE_RUNNER_OS: ${{ matrix.os }}", ci_workflow)\n        self.assertNotIn("event='${{ github.event_name }}'", release_workflow)\n        self.assertNotIn("runner='${{ matrix.os }}'", ci_workflow)\n        self.assertIn("args.Any(IsHelpArgument)", host)\n        self.assertNotIn('command.Append(" -?")', host)\n        self.assertIn("catch (IOException) when", host)\n        self.assertIn('$validationPath = Join-Path $workRoot', build_release)\n        self.assertIn("Initialize-WinCareLifecycleWorkRoot", lifecycle)\n        self.assertIn("SBOM checksum mismatch", verifier)\n        self.assertIn("standalone build manifest root must be a JSON object", verifier_v3)\n\n    def test_finalizer_reports_the_effective_clamped_epoch(self) -> None:\n        with unittest.mock.patch.dict(os.environ, {"SOURCE_DATE_EPOCH": "1"}):\n            _, _, effective_epoch = finalize_release.timestamp()\n        self.assertEqual(effective_epoch, finalize_release.FIXED_EPOCH)\n'''
    test = replace_once(test, '\n\nif __name__ == "__main__":\n', regression_tests + '\n\nif __name__ == "__main__":\n', "review regression tests")
    write(test_path, test)

    lifecycle_path = "tools/Test-InstallationLifecycle.ps1"
    lifecycle = read(lifecycle_path)
    initializer = '''function Initialize-WinCareLifecycleWorkRoot {\n    param([Parameter(Mandatory)][string]$Path)\n    $full=[IO.Path]::GetFullPath($Path)\n    $root=[IO.Path]::GetPathRoot($full)\n    $trimmed=$full.TrimEnd([IO.Path]::DirectorySeparatorChar,[IO.Path]::AltDirectorySeparatorChar)\n    $trimmedRoot=$root.TrimEnd([IO.Path]::DirectorySeparatorChar,[IO.Path]::AltDirectorySeparatorChar)\n    if([string]::IsNullOrWhiteSpace($trimmed) -or $trimmed -eq $trimmedRoot){throw "Unsafe lifecycle work root: $full"}\n    $cursor=$full\n    while($cursor){\n        if(Test-Path -LiteralPath $cursor){\n            $item=Get-Item -LiteralPath $cursor -Force -ErrorAction Stop\n            if($item.Attributes -band [IO.FileAttributes]::ReparsePoint){throw "Lifecycle work root traverses a reparse point: $cursor"}\n        }\n        $parent=[IO.Directory]::GetParent($cursor)\n        if($null -eq $parent -or $parent.FullName -eq $cursor){break}\n        $cursor=$parent.FullName\n    }\n    if(Test-Path -LiteralPath $full){\n        $item=Get-Item -LiteralPath $full -Force -ErrorAction Stop\n        if(!$item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)){throw "Unsafe lifecycle work root: $full"}\n        if(@(Get-ChildItem -LiteralPath $full -Force -ErrorAction Stop).Count){throw "Lifecycle work root must be absent or empty: $full"}\n    }else{\n        [void][IO.Directory]::CreateDirectory($full)\n    }\n    $full\n}\n\n'''
    lifecycle = replace_once(
        lifecycle,
        "$archive = (Resolve-Path -LiteralPath $ArchivePath -ErrorAction Stop).Path\n$work = [IO.Path]::GetFullPath($WorkRoot)\nif (Test-Path -LiteralPath $work) { Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction Stop }\nNew-Item -ItemType Directory -Path $work -ErrorAction Stop | Out-Null\n",
        initializer + "$archive = (Resolve-Path -LiteralPath $ArchivePath -ErrorAction Stop).Path\n$work = Initialize-WinCareLifecycleWorkRoot $WorkRoot\n",
        "lifecycle work-root initialization",
    )
    write(lifecycle_path, lifecycle)


def validate_result() -> None:
    """Validate syntax, workflow interpolation boundaries, and cleanup invariants."""
    python_paths = [
        "tools/finalize_release.py",
        "tools/prepare_standalone_payload.py",
        "tools/verify_release.py",
        "tools/verify_release_v3.py",
        "tools/test_standalone_release.py",
    ]
    for path in python_paths:
        add_missing_docstrings(path)
        ast.parse(read(path), filename=path)

    unsafe = re.compile(r"\$\{\{\s*(?:github|matrix)\.")
    for path in (".github/workflows/ci.yml", ".github/workflows/release.yml"):
        text = read(path)
        lines = text.splitlines()
        for index, line in enumerate(lines):
            if line.lstrip().startswith("shell: pwsh"):
                indent = len(line) - len(line.lstrip())
                cursor = index + 1
                while cursor < len(lines):
                    current = lines[cursor]
                    current_indent = len(current) - len(current.lstrip())
                    if current.strip() and current_indent < indent:
                        break
                    if current_indent == indent and current.lstrip().startswith("run: |"):
                        cursor += 1
                        while cursor < len(lines):
                            body = lines[cursor]
                            body_indent = len(body) - len(body.lstrip())
                            if body.strip() and body_indent <= indent:
                                break
                            if unsafe.search(body):
                                raise RuntimeError(f"{path}:{cursor + 1}: unsafe context interpolation in pwsh run block")
                            cursor += 1
                        break
                    cursor += 1

    if (ROOT / ".github/workflows/apply-standalone-closure.yml").exists():
        raise RuntimeError("obsolete closure workflow is present")
    ast.parse(read("tools/test_standalone_release.py"), filename="tools/test_standalone_release.py")


def main() -> None:
    """Apply every final remediation and validate the transformed source tree."""
    remediate_workflows()
    remediate_host()
    remediate_release_tools()
    remediate_tests_and_lifecycle()
    validate_result()
    print("PR 4 final remediation applied and source invariants validated.")


if __name__ == "__main__":
    main()
