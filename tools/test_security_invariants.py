from __future__ import annotations

import json
import tempfile
import unittest
from pathlib import Path

from tools import validate_context_menu_catalog as context_catalog
from tools import validate_test_fixtures as test_fixtures
from tools import validate_external_processes as external_processes
from tools import validate_network_egress as network_egress
from tools import validate_bounded_io as bounded_io
from tools import validate_read_only_state as read_only_state

ROOT = Path(__file__).resolve().parents[1]


def _workflow_files() -> list[Path]:
    """All GitHub Actions workflow files. Both .yml and .yaml are honored so a
    workflow cannot dodge the supply-chain pin/publisher gates by extension alone."""
    directory = ROOT / ".github/workflows"
    return sorted(directory.glob("*.yml")) + sorted(directory.glob("*.yaml"))


class SecurityInvariantTests(unittest.TestCase):
    def read(self, relative: str) -> str:
        return (ROOT / relative).read_text(encoding="utf-8-sig")

    def test_context_menu_commands_do_not_evaluate_path_controlled_code(self) -> None:
        report = context_catalog.validate(ROOT)
        self.assertEqual("passed", report["status"], report["errors"])
        commands = "\n".join(item["command"] for item in report["commands"])
        self.assertNotIn("-Command", commands)
        self.assertIn('-WorkingDirectory "%V"', commands)
        self.assertIn('-WorkingDirectory "%1"', commands)

    def test_context_menu_validator_rejects_command_string_regression(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            catalog = root / "src/WinCare/Data/Catalog/context-menu-rules.json"
            catalog.parent.mkdir(parents=True)
            catalog.write_text(
                json.dumps(
                    {
                        "schemaVersion": 1,
                        "rules": [
                            {
                                "id": "unsafe",
                                "trees": [
                                    {
                                        "path": "HKCU:test",
                                        "values": [
                                            {
                                                "name": "",
                                                "value": "pwsh.exe -Command Set-Location '%1'",
                                            }
                                        ],
                                    }
                                ],
                            }
                        ],
                    }
                ),
                encoding="utf-8",
            )
            self.assertEqual("failed", context_catalog.validate(root)["status"])

    def test_native_socket_helpers_validate_bounds_and_partial_sends(self) -> None:
        source = self.read("src/WinCare/Native/WinCare.DpiHelper.cs")
        self.assertIn("fragmentSize <= 0", source)
        self.assertIn("offset += sent", source)
        self.assertIn("socket.SetSocketOption", source)
        self.assertNotIn("setsockopt(", source)
        self.assertNotIn("TCP Desynchronization Engine", source)

    def test_security_auditor_does_not_infer_dse_from_migration_metadata(self) -> None:
        source = self.read("src/WinCare/Native/WinCare.SecurityAuditor.cs")
        self.assertNotIn('"UpgradedSystem"', source)
        self.assertIn("DriverSignatureEnforcement = AuditState.Unknown", source)

    def test_process_isolation_is_never_promoted_to_sandbox(self) -> None:
        source = self.read("src/WinCare/Providers/123-AutonomousPatchAgent.ps1")
        self.assertIn("ProcessIsolationDenied", source)
        self.assertIn("SandboxRunnerSha256", source)
        self.assertIn("WINCARE_SANDBOX_CONTRACT", source)
        self.assertNotIn("Get-Command pwsh", source)
        self.assertNotIn("Isolation='ProcessOnly'", source)

    def test_metrics_and_fleet_listeners_authenticate_and_probe_readiness(self) -> None:
        metrics = self.read("src/WinCare/Providers/118-MetricsHttpServer.ps1")
        fleet = self.read("src/WinCare/Providers/126-P2pRaftFleetOrchestrator.ps1")
        for source in (metrics, fleet):
            self.assertIn("FixedTimeEquals", source)
            self.assertIn("Cache-Control", source)
            self.assertIn("ReadinessTimeoutSeconds", source)
        self.assertNotIn("AbsolutePath -eq '/state'", fleet)
        self.assertIn("RemoteReplicationPerformed=$false", fleet)
        self.assertIn("RaftImplemented=$false", fleet)

    def test_safety_layer_uses_structural_redaction_atomic_replace_and_key_lock(self) -> None:
        source = self.read("src/WinCare/Core/06-Safety.ps1")
        self.assertIn("ConvertTo-WinCareRedactedValue", source)
        self.assertNotIn("$json=[regex]::Replace", source)
        self.assertIn("[IO.File]::Replace", source)
        self.assertIn("[IO.FileShare]::None", source)
        self.assertIn("local-integrity.key.lock", source)
        self.assertNotIn("Move-Item -LiteralPath $temp -Destination $keyPath -Force", source)

    def test_all_literal_test_fixtures_exist(self) -> None:
        report = test_fixtures.validate(ROOT)
        self.assertEqual("passed", report["status"], report["missingReferences"])


    def test_external_process_boundary_is_closed_and_bounded(self) -> None:
        report = external_processes.validate(ROOT)
        self.assertEqual("passed", report["status"], report["errors"])
        process = self.read("src/WinCare/Core/03-Process.ps1")
        self.assertIn("MaximumCapturedOutputBytes", process)
        self.assertIn("StandardOutput.BaseStream", process)
        self.assertIn("ReadAsync", process)
        self.assertIn("CancellationTokenSource", process)
        self.assertNotIn("BeginOutputReadLine", process)
        self.assertNotIn("add_OutputDataReceived", process)
        self.assertNotIn("ReadToEndAsync", process)

    def test_external_process_validator_rejects_bypass_patterns(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            provider = root / "src/WinCare/Providers/unsafe.ps1"
            provider.parent.mkdir(parents=True)
            provider.write_text(
                "& $command.Source --version\n"
                "& (Get-Command pwsh).Source -File unsafe.ps1\n"
                "Start-Process -FilePath calc.exe\n"
                "$task=$process.StandardOutput.ReadToEndAsync()\n",
                encoding="utf-8",
            )
            report = external_processes.validate(root)
            self.assertEqual("failed", report["status"])
            codes = {item["code"] for item in report["errors"]}
            self.assertEqual(
                {"DirectExternalCallOperator", "StartProcessBypass", "UnboundedAsyncReadToEnd"},
                codes,
            )


    def test_hash_primitives_reject_reparse_races_and_clear_sensitive_buffers(self) -> None:
        safety = self.read("src/WinCare/Core/06-Safety.ps1")
        start = safety.index("function Get-WinCareSha256")
        end = safety.index("function Get-WinCarePathContentHash", start)
        hashing = safety[start:end]
        self.assertIn("Assert-WinCareSafePath", hashing)
        self.assertIn("FileShare]::Read", hashing)
        self.assertIn("SHA-256 input changed while hashing", hashing)
        self.assertIn("MaximumBytes", hashing)
        self.assertGreaterEqual(hashing.count("[Array]::Clear"), 3)
        self.assertNotIn("Get-FileHash", hashing)

    def test_canonical_serialization_is_bounded_cycle_safe_and_non_executable(self) -> None:
        canonical = self.read("src/WinCare/Core/05-Canonical.ps1")
        self.assertIn("ReferenceEqualityComparer", canonical)
        self.assertIn("reference cycle", canonical)
        self.assertIn("MaximumItems", canonical)
        self.assertIn("MaximumProperties", canonical)
        self.assertIn("MaximumCollectionItems", canonical)
        self.assertIn("MaximumStringCharacters", canonical)
        self.assertIn("MaximumOutputBytes", canonical)
        self.assertNotIn("ScriptProperty", canonical)

    def test_tree_hashing_is_bounded_incremental_and_race_aware(self) -> None:
        safety = self.read("src/WinCare/Core/06-Safety.ps1")
        bounded = self.read("src/WinCare/Core/06-BoundedIO.ps1")
        start = safety.index("function Get-WinCarePathContentHash")
        end = safety.index("function Measure-WinCarePathTree", start)
        function = safety[start:end]
        self.assertIn("MaximumEntries", function)
        self.assertIn("MaximumPathMetadataBytes", function)
        self.assertIn("IncrementalHash", function)
        self.assertIn("StringComparer]::Ordinal", function)
        self.assertIn("Tree entry changed during hashing", function)
        self.assertNotIn("StringBuilder", function)
        self.assertIn("File length changed during reading", bounded)

    def test_live_runtime_uses_only_bounded_file_and_stream_reads(self) -> None:
        report = bounded_io.validate(ROOT)
        self.assertEqual("passed", report["status"], report["errors"])

    def test_bounded_io_validator_rejects_whole_input_reads(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            provider = root / "src/WinCare/Providers/unsafe.ps1"
            provider.parent.mkdir(parents=True)
            provider.write_text(
                "$a = Get-Content -LiteralPath $Path -Raw\n"
                "$b = [IO.File]::ReadAllBytes($Path)\n"
                "$c = $reader.ReadToEnd()\n",
                encoding="utf-8",
            )
            report = bounded_io.validate(root)
            self.assertEqual("failed", report["status"])
            self.assertEqual(
                {"UnboundedGetContent", "UnboundedReadAllBytes", "UnboundedReadToEnd"},
                {item["code"] for item in report["errors"]},
            )

    def test_network_egress_validator_rejects_duplicate_http_boundaries(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            files = {
                "src/WinCare/Core/01-Config.ps1": "NetworkExperimentTargets     = @()\n",
                "src/WinCare/Core/00-04-NetworkExperiments.ps1": "# will not silently probe public services\n",
                "src/WinCare/Core/04-Http.ps1": "# authoritative broker\n",
                "src/WinCare/Providers/100-Downloads.ps1": (
                    "Assert-WinCareServiceUri AllowInsecureDownloads "
                    "AllowPrivateDownloadTargets Start-BitsTransfer\n"
                ),
                "src/WinCare/Providers/unsafe.ps1": (
                    "$client=[System.Net.Http.HttpClient]::new()\n"
                    "Invoke-WebRequest -Uri 'https://example.invalid'\n"
                ),
                "src/WinCare/Data/Gui/actions.json": json.dumps([
                    {"id": "network-measure", "defaultArguments": {"TargetHosts": []}}
                ]),
            }
            for relative, content in files.items():
                path = root / relative
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text(content, encoding="utf-8")
            report = network_egress.validate(root)
            self.assertEqual("failed", report["status"])
            codes = {
                item.get("code") for item in report["errors"] if isinstance(item, dict)
            }
            self.assertIn("DuplicateHttpClient", codes)
            self.assertIn("DirectWebRequestCmdlet", codes)

    def test_release_build_isolated_and_refuses_populated_output(self) -> None:
        release = self.read("tools/Build-Release.ps1")
        native = self.read("src/WinCare/Native/Build-WinCareNativePolyglot.ps1")
        launcher = self.read("tools/Build-Exe.ps1")
        self.assertIn("Release output must be absent or empty", release)
        self.assertIn("WinCare-release-build-", release)
        self.assertIn("-OutputPath (Join-Path $workRoot", release)
        self.assertIn("WinCare-native-build-", native)
        self.assertIn("BaseIntermediateOutputPath", native)
        self.assertIn("BaseOutputPath", native)
        self.assertIn("SchemaVersion = 2", launcher)
        self.assertNotIn("[IO.Path]::GetFullPath($ZipPackage) } else", launcher)

    def test_onnx_and_wasi_execution_require_digest_pins_and_output_bounds(self) -> None:
        onnx = self.read("src/WinCare/Providers/115-SelfHealingCopilot.ps1")
        wasi = self.read("src/WinCare/Providers/128-WasiPluginRuntime.ps1")
        for source in (onnx, wasi):
            self.assertIn("MaximumCapturedOutputBytes", source)
            self.assertIn("Invoke-WinCareProcess", source)
        self.assertIn("ExpectedModelSha256", onnx)
        self.assertIn("ExpectedHostSha256", onnx)
        self.assertIn("ExpectedRuntimeSha256", wasi)
        self.assertIn("ExpectedSha256", wasi)

    def test_extension_admission_bounds_extraction_and_requires_revocation_evidence(self) -> None:
        source = self.read("src/WinCare/Providers/90-Extensions.ps1")
        self.assertIn("MaximumExpandedBytes", source)
        self.assertIn("MaximumCompressionRatio", source)
        self.assertIn("CompressedLength", source)
        self.assertIn("BoundedExactByteArchiveExtraction", source)
        self.assertIn("X509RevocationMode]::Online", source)
        self.assertIn("X509RevocationFlag]::EntireChain", source)
        self.assertNotIn("X509RevocationMode]::NoCheck", source)

    def test_pqc_adapter_pins_runtime_bounds_files_and_authenticates_metadata(self) -> None:
        source = self.read("src/WinCare/Providers/121-PostQuantumCrypto.ps1")
        self.assertIn("ExpectedOpenSslSha256", source)
        self.assertIn("MaximumInputBytes", source)
        self.assertIn("Get-WinCareHkdfSha256Key", source)
        self.assertIn("[Security.Cryptography.AesGcm]::new", source)
        self.assertIn("New-WinCarePqcPrivateWorkspace", source)
        self.assertIn("Write-WinCareAtomicJson", source)
        self.assertIn("Write-WinCareAtomicBytes", source)
        self.assertNotIn("& $", source)

    def test_fleet_http_adapter_blocks_metadata_and_bounds_structurally_redacted_io(self) -> None:
        broker = self.read("src/WinCare/Core/04-Http.ps1")
        fleet = self.read("src/WinCare/Providers/132-MultiCloudFleetController.ps1")
        self.assertIn("HttpCompletionOption]::ResponseHeadersRead", broker)
        self.assertIn("MaximumResponseBytes", broker)
        self.assertIn("Cloud instance-metadata endpoints are prohibited", broker)
        self.assertIn("ConvertTo-WinCareRedactedValue", fleet)
        self.assertIn("BodySha256", broker)
        self.assertNotIn("ReadAsStringAsync", broker)


    def test_elevation_secret_is_file_bound_and_never_exposed_in_arguments(self) -> None:
        process = self.read("src/WinCare/Core/03-Process.ps1")
        host = self.read("src/WinCare/Host/ElevatedActionHost.ps1")
        combined = process + "\n" + host
        self.assertNotIn("SecretBase64", combined)
        self.assertIn("SecretPath", combined)
        self.assertIn("SecretSha256", combined)
        self.assertIn("FileShare]::None", combined)

    def test_local_broker_uses_authenticated_bounded_binary_frames(self) -> None:
        broker = self.read("src/WinCare/Providers/89-Broker.ps1")
        self.assertIn("WinCare.LocalBroker.v3", broker)
        self.assertIn("Read-WinCareBrokerFrame", broker)
        self.assertIn("Write-WinCareBrokerFrame", broker)
        self.assertIn("MaximumResponseBytes", broker)
        self.assertIn("replay detected", broker)
        self.assertNotIn("ReadLine", broker)
        self.assertNotIn("StreamReader", broker)

    def _context_menu_report_for(self, command: str) -> dict:
        with tempfile.TemporaryDirectory() as temp:
            catalog = Path(temp) / "src/WinCare/Data/Catalog/context-menu-rules.json"
            catalog.parent.mkdir(parents=True)
            catalog.write_text(
                json.dumps(
                    {
                        "schemaVersion": 1,
                        "rules": [
                            {
                                "id": "probe",
                                "trees": [
                                    {
                                        "path": "HKCU:\\Software\\Classes\\probe\\command",
                                        "values": [{"name": "", "value": command, "valueType": "String"}],
                                    }
                                ],
                            }
                        ],
                    }
                ),
                encoding="utf-8",
            )
            return context_catalog.validate(Path(temp))

    def test_context_menu_gate_catches_case_insensitive_bypasses(self) -> None:
        # Windows treats shell placeholders and the cmd /c switch case-insensitively; the
        # gate must not be fooled by lowercase %v or a plain (no .exe) cmd invocation.
        lowercase_placeholder = self._context_menu_report_for("pwsh.exe -File run.ps1 %v")
        self.assertEqual("failed", lowercase_placeholder["status"])
        self.assertTrue(any("placeholder" in error for error in lowercase_placeholder["errors"]))

        plain_cmd = self._context_menu_report_for('cmd /c "del %1"')
        self.assertEqual("failed", plain_cmd["status"])
        self.assertTrue(any("shell-evaluation primitive" in error for error in plain_cmd["errors"]))

        quoted_placeholder = self._context_menu_report_for('pwsh.exe -File run.ps1 "%V"')
        self.assertEqual("passed", quoted_placeholder["status"], quoted_placeholder["errors"])

    def test_workflow_pin_gate_scans_yaml_and_yml(self) -> None:
        names = {path.name for path in _workflow_files()}
        self.assertIn("ci.yml", names)
        # A .yaml workflow must also be enumerated so it cannot dodge the pinning gate.
        self.assertTrue(all(name.endswith((".yml", ".yaml")) for name in names))

    def test_state_root_discovery_is_non_persistent_in_read_only_mode(self) -> None:
        report = read_only_state.validate(ROOT)
        self.assertEqual("passed", report["status"], report["errors"])

    def test_read_only_state_validator_rejects_directory_creating_getters(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            core = root / "src/WinCare/Core"
            core.mkdir(parents=True)
            (core / "00-Bridge.ps1").write_text(
                "function Get-WinCareBridgeStateRoot { "
                "$readOnly=$script:WinCareState.ReadOnlyLocked; "
                "if(-not $readOnly){New-Item x}; "
                "Test-WinCarePathWithin; ReparsePoint }\n",
                encoding="utf-8",
            )
            provider = root / "src/WinCare/Providers/unsafe.ps1"
            provider.parent.mkdir(parents=True)
            provider.write_text(
                "function Get-WinCareUnsafeRoot { New-Item -ItemType Directory -Path x }\n",
                encoding="utf-8",
            )
            report = read_only_state.validate(root)
            self.assertEqual("failed", report["status"])
            self.assertIn(
                "StateRootGetterCreatesDirectory",
                {item["code"] for item in report["errors"]},
            )

    def test_read_only_initialization_is_non_persistent(self) -> None:
        config = self.read("src/WinCare/Core/01-Config.ps1")
        logging = self.read("src/WinCare/Core/02-Logging.ps1")
        self.assertIn("$readOnlyLocked=$readOnlyRequested -or [bool]$effectivePolicy.ReadOnly", config)
        self.assertIn("Initialize-WinCareDataRootIdentity -Root $root -VerifyOnly:$readOnlyLocked", config)
        self.assertIn("if(-not $readOnlyLocked -and (Get-Command Repair-WinCareInterruptedOperation", config)
        self.assertIn("if(-not $SkipConfigSave -and -not $readOnlyLocked)", config)
        self.assertIn("if ($script:WinCareState.ReadOnlyLocked) { return }", config)
        self.assertGreaterEqual(logging.count("[bool]$script:WinCareState.ReadOnlyLocked"), 2)

    def test_logging_is_structurally_redacted_serialized_and_bounded(self) -> None:
        logging = self.read("src/WinCare/Core/02-Logging.ps1")
        self.assertIn("ConvertTo-WinCareRedactedValue", logging)
        self.assertIn("MaximumLogRecordBytes", logging)
        self.assertIn("Threading.Mutex", logging)
        self.assertIn("FileShare]::Read", logging)
        self.assertNotIn("Add-Content", logging)

    def test_release_pipeline_promotes_the_exact_validated_candidate(self) -> None:
        workflow = self.read(".github/workflows/release.yml")
        publish = workflow.split("  publish-release:", 1)[1]
        self.assertIn("promotion-manifest.json", workflow)
        self.assertIn("actions/download-artifact", publish)
        self.assertNotIn("actions/checkout", publish)
        self.assertNotIn("Build-Release.ps1", publish)
        self.assertNotIn("build_release.py", publish)

    def test_install_and_uninstall_bind_identity_to_verified_release_content(self) -> None:
        install = self.read("Install-WinCare.ps1")
        uninstall = self.read("Uninstall-WinCare.ps1")
        for source in (install, uninstall):
            self.assertIn("InstallationPathSha256", source)
            self.assertIn("ReleaseManifestSha256", source)
            self.assertIn("BuildReceiptSha256", source)
        self.assertIn("Test-InstalledReleaseTree", uninstall)
        self.assertIn("Assert-DataRootIdentity", uninstall)
        self.assertIn("source-built-and-verified", install)
        self.assertIn("source-built-and-verified", uninstall)

    def test_native_manifest_binds_artifacts_to_deterministic_source_provenance(self) -> None:
        native = self.read("src/WinCare/Native/Build-WinCareNativePolyglot.ps1")
        self.assertIn("SchemaVersion=2", native)
        self.assertIn("SOURCE_DATE_EPOCH", native)
        self.assertIn("SourceTreeSha256", native)
        self.assertIn("DotNetHostSha256", native)
        self.assertIn("ContinuousIntegrationBuild", native)

    def test_workflow_dependencies_are_commit_pinned(self) -> None:
        workflow_text = "\n".join(
            path.read_text(encoding="utf-8")
            for path in sorted(_workflow_files())
        )
        for line in workflow_text.splitlines():
            stripped = line.strip()
            if stripped.startswith("uses:"):
                target = stripped[len("uses:"):].strip()
                # Local composite actions (./path, ../path) and container actions
                # (docker://image@sha256:...) are not GitHub commit refs, so the
                # 40-hex-SHA rule below cannot apply to them. Container images must
                # still be digest-pinned rather than tag-pinned.
                if target.startswith("./") or target.startswith("../"):
                    continue
                if target.startswith("docker://"):
                    self.assertIn(
                        "@sha256:", target,
                        f"Container action must be digest-pinned, not tag-pinned: {stripped}",
                    )
                    continue
                self.assertIn("@", target, f"Unpinnable non-local action reference: {stripped}")
                ref = target.split("@", 1)[1]
                self.assertRegex(ref, r"^[0-9a-f]{40}(?:\s+#.*)?$")
        self.assertNotIn("-MinimumVersion", workflow_text)
        self.assertIn("-RequiredVersion 5.5.0", workflow_text)
        self.assertIn("-RequiredVersion 1.22.0", workflow_text)


    def test_native_builder_closes_source_membership_and_bounds_all_capture(self) -> None:
        native = self.read("src/WinCare/Native/Build-WinCareNativePolyglot.ps1")
        self.assertNotIn("Get-Content", native)
        self.assertNotIn("& $", native)
        self.assertIn("WinCareNativeSourceNames", native)
        self.assertIn("maximumTotalSourceBytes", native)
        self.assertIn("maximumMetadataBytes", native)
        self.assertIn("MaximumCapturedOutputBytes", native)
        self.assertIn("ArgumentList.Add", native)
        self.assertIn("IncrementalHash", native)
        self.assertIn("Native output must be absent or empty", native)
        self.assertIn("SourceCount=$sourceEvidence.SourceCount", native)

    def test_native_and_runtime_io_and_process_gates_cover_the_native_builder(self) -> None:
        bounded = bounded_io.validate(ROOT)
        processes = external_processes.validate(ROOT)
        self.assertEqual("passed", bounded["status"], bounded["errors"])
        self.assertEqual("passed", processes["status"], processes["errors"])
        self.assertGreaterEqual(bounded["filesChecked"], 166)
        self.assertGreaterEqual(processes["filesChecked"], 166)

    def test_release_governance_rejects_publisher_bypass_and_asset_overwrite(self) -> None:
        workflows = "\n".join(
            path.read_text(encoding="utf-8")
            for path in sorted(_workflow_files())
        )
        release = self.read(".github/workflows/release.yml")
        self.assertNotIn("SkipPublisherCheck", workflows)
        self.assertNotIn("--clobber", release)
        self.assertIn("gh release view $tag", release)
        self.assertIn("promotion-manifest.json", release)
        self.assertIn("windows-validation-report.json", release)
        self.assertIn("@('--target',$env:SOURCE_COMMIT)", release)


    def test_launchers_reuse_or_pin_powershell_without_path_or_policy_weakening(self) -> None:
        powershell = self.read("WinCare.ps1") + "\n" + self.read("WinCare-GUI.ps1")
        native = self.read("src/WinCare/Native/WinCareLauncher.cs") + "\n" + self.read("src/WinCare/Native/WinCareTuiLauncher.cs")
        self.assertIn("GetCurrentProcess().MainModule.FileName", powershell)
        self.assertNotIn("Get-Command pwsh", powershell)
        self.assertNotIn("-ExecutionPolicy", powershell + native)
        self.assertNotIn("RemoteSigned", powershell + native)
        self.assertIn("SpecialFolder.ProgramFiles", native)
        self.assertIn("FileAttributes.ReparsePoint", native)
        self.assertNotIn("EnvironmentVariable(\"PATH\")", native)

    def test_native_policy_parser_is_bounded_strict_and_recoverable(self) -> None:
        policy = self.read("src/WinCare/Native/WinCare.PolicyEngine.cs")
        for token in (
            "MaximumFileBytes", "MaximumEntries", "MaximumStringCharacters",
            "MaximumEntryDataBytes", "MaximumTotalDataBytes", "RequireCharacter",
            "EnsureNoReparseAncestors", "alternate data streams", "File.Replace",
            "Flush(flushToDisk: true)", "AggregateException", "expectedLength",
        ):
            self.assertIn(token, policy)
        self.assertNotIn("File.ReadAllBytes", policy)
        self.assertNotIn("File.WriteAllBytes", policy)
        self.assertIn("(uint)data.Length != rawSize", policy)

    def test_native_audio_and_network_helpers_release_and_bound_resources(self) -> None:
        audio = self.read("src/WinCare/Native/WinCare.Audio.cs")
        network = self.read("src/WinCare/Native/WinCare.DpiHelper.cs")
        self.assertGreaterEqual(audio.count("[PreserveSig]"), 15)
        self.assertIn("FinalReleaseComObject", audio)
        self.assertIn("float.IsFinite", audio)
        self.assertIn("MaximumHeaderPayloadBytes", network)
        self.assertIn("MaximumTcpPayloadBytes", network)
        self.assertIn("MaximumTcpFragments", network)
        self.assertIn("CancellationTokenSource", network)
        self.assertIn("SendAsync", network)
        self.assertIn("WaitAsync(DiagnosticTimeout)", network)

    def test_shared_tooling_io_and_process_boundary_is_bounded(self) -> None:
        tooling = self.read("tools/WinCare.Tooling.ps1")
        self.assertIn("Read-WinCareToolingBoundedBytes", tooling)
        self.assertIn("MaximumCapturedOutputBytes", tooling)
        self.assertIn("ArgumentList.Add", tooling)
        self.assertIn("IncrementalHash", tooling)
        self.assertIn("Flush($true)", tooling)
        self.assertNotIn("Get-Content", tooling)
        self.assertNotIn("ReadToEnd", tooling)
        self.assertNotIn("Start-Process", tooling)


    def test_process_broker_verifies_executable_identity_and_redacts_arguments(self) -> None:
        process = self.read("src/WinCare/Core/03-Process.ps1")
        self.assertIn("Resolve-WinCareProcessExecutablePath", process)
        self.assertIn("CommandType Application", process)
        self.assertIn("ExpectedSha256", process)
        self.assertIn("CryptographicOperations]::FixedTimeEquals", process)
        self.assertIn("Working directory must be a regular non-reparse directory", process)
        self.assertIn("argumentsSha256", process)
        self.assertIn("Assert-WinCareProcessExecutableUnchanged", process)
        self.assertIn("ConvertTo-WinCareRedactedScalar -Value $execution.StdOut", process)
        self.assertNotIn("Arguments=@($ArgumentList)", process)
        self.assertIn("if(-not [bool]$script:WinCareState.ReadOnlyLocked)", process)
        self.assertIn("if($process) { $process.Dispose() }", process)

    def test_digest_pinned_providers_bind_hashes_at_the_process_boundary(self) -> None:
        pqc = self.read("src/WinCare/Providers/121-PostQuantumCrypto.ps1")
        wasi = self.read("src/WinCare/Providers/128-WasiPluginRuntime.ps1")
        ebpf = self.read("src/WinCare/Providers/116-KernelFilterEngine.ps1")
        self.assertIn("-ExpectedSha256 $ExpectedOpenSslSha256", pqc)
        self.assertIn("-ExpectedSha256 $ExpectedRuntimeSha256", wasi)
        self.assertIn("WASI plugin changed after admission", wasi)
        self.assertIn("-ExpectedSha256 $toolHash", ebpf)
        self.assertIn("eBPF program changed after admission", ebpf)

    def test_installer_and_uninstaller_bind_shortcuts_and_revalidate_tombstones(self) -> None:
        install = self.read("Install-WinCare.ps1")
        uninstall = self.read("Uninstall-WinCare.ps1")
        self.assertIn("SchemaVersion=4", install)
        self.assertIn("LauncherExecutablePathSha256", install)
        self.assertIn("LauncherExecutableSha256", install)
        self.assertIn("Test-WinCareInstallerShortcutIdentity", install)
        self.assertGreaterEqual(install.count("FinalReleaseComObject"), 5)
        self.assertIn("Test-WinCareShortcutIdentity", uninstall)
        self.assertIn("ExpectedArguments", uninstall)
        self.assertIn("ExpectedWorkingDirectory", uninstall)
        self.assertIn("Test-WinCareUninstallerDataTree", uninstall)
        self.assertIn("Test-InstalledReleaseTree -Root $safeTombstone", uninstall)
        self.assertGreaterEqual(uninstall.count("FinalReleaseComObject"), 2)

    def test_context_menu_commands_are_materialized_to_verified_executables(self) -> None:
        provider = self.read("src/WinCare/Providers/81-Customization.ps1")
        self.assertIn("Resolve-WinCareProcessExecutablePath", provider)
        self.assertIn("ConvertTo-WinCareContextMenuRegistryValue", provider)
        self.assertIn("Executables $compat.Executables", provider)
        self.assertIn("return '\"'+$resolved+'\"'", provider)
        self.assertNotIn("Get-Command $Name -ErrorAction", provider)

    def test_gui_validation_and_provisioning_reuse_pinned_powershell_hosts(self) -> None:
        gui = self.read("src/WinCare/UI/Gui/10-GuiRuntime.ps1")
        validation = self.read("tools/Invoke-WindowsValidation.ps1")
        provisioning = self.read("src/WinCare/Providers/72-Provisioning.ps1")
        self.assertIn("Get-WinCareCurrentPowerShellExecutable", gui)
        self.assertNotIn("Get-Command pwsh.exe", gui)
        self.assertIn("GetCurrentProcess().MainModule.FileName", validation)
        self.assertNotIn("Get-Command pwsh.exe", validation)
        self.assertIn("%ProgramFiles%\\PowerShell\\7\\pwsh.exe", provisioning)
        self.assertNotIn("where pwsh.exe", provisioning)
        self.assertIn("Write-WinCareAtomicJson", provisioning)


    def test_native_runtime_owns_command_line_process_memory_and_binary_intelligence(self) -> None:
        project = self.read("src/WinCare/Native/WinCare.Native.csproj")
        builder = self.read("src/WinCare/Native/Build-WinCareNativePolyglot.ps1")
        command_line = self.read("src/WinCare/Native/WinCare.CommandLine.cs")
        process_memory = self.read("src/WinCare/Native/WinCare.ProcessMemory.cs")
        intelligence = self.read("src/WinCare/Native/WinCare.FileIntelligence.cs")
        providers = "\n".join(
            self.read(path)
            for path in (
                "src/WinCare/Providers/10-Applications.ps1",
                "src/WinCare/Providers/56-MemoryManagement.ps1",
                "src/WinCare/Providers/114-StorageIntelligence.ps1",
                "src/WinCare/Providers/129-BinaryIntelligence.ps1",
            )
        )
        for name in (
            "WinCare.CommandLine.cs",
            "WinCare.ProcessMemory.cs",
            "WinCare.FileIntelligence.cs",
        ):
            self.assertIn(name, project)
            self.assertIn(name, builder)
        self.assertIn("CommandLineToArgvW", command_line)
        self.assertIn("LocalFree", command_line)
        self.assertIn("MaximumWindowsCommandLineCharacters", command_line)
        self.assertIn("SafeProcessHandle", process_memory)
        self.assertIn("expectedStartTimeUtcTicks", process_memory)
        self.assertIn("minimumWorkingSetBytes", process_memory)
        self.assertIn("PEReader", intelligence)
        self.assertIn("maximumFileBytes", intelligence)
        self.assertIn("maximumSections", intelligence)
        self.assertIn("CryptographicOperations.ZeroMemory", intelligence)
        self.assertNotIn("Add-Type -TypeDefinition", providers)

    def test_advanced_capabilities_are_exposed_without_covert_or_automatic_authority(self) -> None:
        headless = self.read("src/WinCare/UI/97-AdvancedCapabilities.ps1")
        ebpf = self.read("src/WinCare/Providers/131-EbpfSocketGovernance.ps1")
        network = self.read("src/WinCare/Providers/136-NetworkAcceleration.ps1")
        isolation = self.read("src/WinCare/Providers/124-HypervMicroVmSandbox.ps1")
        for command in (
            "fleet-inventory", "forensics-timeline", "vbs-assurance",
            "telemetry-lake", "binary-intelligence", "ebpf-programs",
            "display-pipeline", "wireguard-tunnels", "quic-capability",
        ):
            self.assertIn(f"'{command}'", headless)
        for mutation in (
            "vbs-harden", "sandbox-config", "telemetry-ingest",
            "telemetry-retention", "display-calibrate",
        ):
            self.assertIn(f"'{mutation}'", headless)
            self.assertIn("Invoke-WinCareHeadlessPlan", headless)
        self.assertIn("'ebpf-admit'", headless)
        self.assertIn("New-WinCareEbpfSocketProgramAdmission @parameters", headless)
        self.assertIn("AutomaticPacketRedirectionSupported=$false", ebpf)
        self.assertIn("TransparentInterceptionSupported=$false", ebpf)
        self.assertIn("TrafficEvasionSupported=$false", network)
        self.assertIn("will not install or enable it implicitly", isolation)


if __name__ == "__main__":
    unittest.main()
