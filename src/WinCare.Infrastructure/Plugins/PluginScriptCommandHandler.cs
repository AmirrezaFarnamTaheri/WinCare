using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Application.Commands;
using WinCare.Application.Plugins;
using WinCare.Domain.Commands;
using WinCare.Infrastructure.Commands;

namespace WinCare.Infrastructure.Plugins
{
    /// <summary>
    /// Executes a declarative plugin script (.ps1/.cmd/.bat/binary) through the bounded,
    /// argument-list-only <see cref="BoundedProcessRunner"/>.
    /// </summary>
    /// <remarks>
    /// Two-phase safety: plugin-declared risk metadata is <em>advisory display metadata</em>.
    /// A mutating script tool (declared <c>readOnly=false</c>) is therefore never launched on
    /// a preview request (<see cref="CommandRequest.Apply"/> == <c>false</c>); the preview
    /// returns a non-mutating description of what <em>would</em> run so the caller can obtain
    /// an approved <see cref="ApprovedMutationPlan"/> before the process is started. Read-only
    /// script tools always run with <c>Apply=false</c> (the dispatcher rejects
    /// <c>Apply=true</c> for read-only definitions), so preview and execution coincide for them.
    /// </remarks>
    public sealed class PluginScriptCommandHandler : ICommandHandler
    {
        private readonly string _commandId;
        private readonly string _scriptFullPath;
        private readonly string _pluginDirectory;
        private readonly bool _requiresAdmission;
        private readonly bool _declaredReadOnly;
        private readonly IReadOnlyList<string> _declaredCapabilities;
        private readonly BoundedProcessRunner _processRunner;

        public string CommandId => _commandId;

        public PluginScriptCommandHandler(
            string commandId,
            string scriptRelativePath,
            string pluginDirectory,
            BoundedProcessRunner? processRunner = null,
            bool declaredReadOnly = false,
            IReadOnlyList<string>? declaredCapabilities = null)
        {
            if (string.IsNullOrWhiteSpace(commandId)) throw new ArgumentException("Command ID required", nameof(commandId));
            if (string.IsNullOrWhiteSpace(pluginDirectory)) throw new ArgumentException("Plugin directory required", nameof(pluginDirectory));

            _commandId = commandId;
            _pluginDirectory = Path.GetFullPath(pluginDirectory);
            _processRunner = processRunner ?? new BoundedProcessRunner();
            _declaredReadOnly = declaredReadOnly;
            _declaredCapabilities = declaredCapabilities ?? Array.Empty<string>();

            var combined = Path.GetFullPath(Path.Combine(_pluginDirectory, scriptRelativePath ?? string.Empty));
            var canonicalBase = _pluginDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

            if (!combined.StartsWith(canonicalBase, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"Security violation: Script path '{scriptRelativePath}' escapes plugin directory.", nameof(scriptRelativePath));
            }

            _scriptFullPath = combined;
            // Discovery only instantiates installed user plugins with an external trust
            // record. Pin this expectation so deleting the record after enable does not
            // silently downgrade execution to unverified bytes.
            _requiresAdmission = PluginAdmissionTrustStore.TryResolveAdmissionRecordPath(_pluginDirectory) is not null;
        }

        public async Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
        {
            if (!File.Exists(_scriptFullPath))
            {
                return CommandHandlerOutcome.Failed(
                    "plugin.script_not_found",
                    $"The plugin script file was not found at '{Path.GetFileName(_scriptFullPath)}'.");
            }

            // Two-phase gate for mutating script tools: never launch the process on a
            // preview request. Return a non-mutating preview so the caller can obtain an
            // approved mutation plan before the script is allowed to run.
            if (!request.Apply && !_declaredReadOnly)
            {
                // A preview must not imply that tampered or untrusted plugin bytes
                // are ready for approval. Elevated users may inspect the preview,
                // but the executable apply path is always checked separately.
                CommandHandlerOutcome? previewIntegrity = await Task.Run(
                    () => VerifyAdmittedPayload(requireNonElevated: false), cancellationToken).ConfigureAwait(false);
                return previewIntegrity ?? BuildPreviewOutcome(request);
            }

            // Defense-in-depth: a mutating script must carry an approved mutation plan even
            // if a future caller bypasses the dispatcher's admission checks. Plugin-declared
            // risk/capabilities are advisory display metadata, so execution authority comes
            // solely from the dispatcher's two-phase approval, never from the manifest.
            if (request.Apply && !_declaredReadOnly && request.Approval is null)
            {
                return CommandHandlerOutcome.Blocked(
                    "plugin.approval_required",
                    $"Plugin script '{Path.GetFileName(_scriptFullPath)}' is a mutating tool and requires an approved mutation plan before execution.");
            }

            try
            {
                CommandHandlerOutcome? integrity = await Task.Run(
                    () => VerifyAdmittedPayload(requireNonElevated: true), cancellationToken).ConfigureAwait(false);
                if (integrity is not null) return integrity;

                // Hash a fresh copy of the complete admitted package and hold its
                // file handles read-only through interpreter execution. The original
                // user-writable plugin tree is no longer the executed code path.
                string? admissionPath = _requiresAdmission
                    ? PluginAdmissionTrustStore.TryResolveAdmissionRecordPath(_pluginDirectory) : null;
                PluginAdmissionRecord? admitted = admissionPath is not null
                    ? JsonSerializer.Deserialize<PluginAdmissionRecord>(File.ReadAllText(admissionPath))
                    : null;
                using VerifiedPluginExecutionSnapshot? snapshot = _requiresAdmission
                    ? await Task.Run(() => VerifiedPluginExecutionSnapshot.Create(
                        _pluginDirectory, _scriptFullPath,
                        admitted?.PackageTreeSha256 ?? string.Empty,
                        cancellationToken: cancellationToken), cancellationToken).ConfigureAwait(false)
                    : null;
                string executableScriptPath = snapshot?.ScriptFullPath ?? _scriptFullPath;
                string executionWorkingDirectory = snapshot?.Root ?? _pluginDirectory;

                var ext = Path.GetExtension(executableScriptPath).ToLowerInvariant();
                string executable;
                var arguments = new List<string>();

                if (ext == ".ps1")
                {
                    // Third-party .ps1 plugin tools run through powershell.exe. The interpreter
                    // name is written as a plain literal so that source-level audits and policy
                    // checks that grep for PowerShell invocations see it; hiding it would make
                    // any "PowerShell-free" claim silently false.
                    executable = "powershell.exe";
                    arguments.Add("-NoProfile");
                    arguments.Add("-NonInteractive");
                    arguments.Add("-ExecutionPolicy");
                    arguments.Add("Bypass");
                    arguments.Add("-File");
                    arguments.Add(executableScriptPath);

                    if (request.Parameters.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var prop in request.Parameters.EnumerateObject())
                        {
                            arguments.Add($"-{prop.Name}");
                            arguments.Add(prop.Value.ToString());
                        }
                    }
                }
                else if (ext is ".cmd" or ".bat")
                {
                    // cmd.exe parses shell operators even when ArgumentList is used.
                    // Reject expansion and control syntax before passing data to it.
                    const string shellSyntax = "&|<>^%!\"\r\n()";
                    if (executableScriptPath.IndexOfAny(shellSyntax.ToCharArray()) >= 0 ||
                        (request.Parameters.ValueKind == JsonValueKind.Object &&
                         request.Parameters.EnumerateObject().Any(prop =>
                             prop.Value.ToString().IndexOfAny(shellSyntax.ToCharArray()) >= 0)))
                    {
                        return CommandHandlerOutcome.Blocked("plugin.shell_syntax_rejected",
                            "Batch script paths and arguments cannot contain shell control or expansion characters.");
                    }
                    executable = "cmd.exe";
                    arguments.Add("/d");
                    arguments.Add("/c");
                    arguments.Add(executableScriptPath);

                    if (request.Parameters.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var prop in request.Parameters.EnumerateObject())
                        {
                            arguments.Add(prop.Value.ToString());
                        }
                    }
                }
                else
                {
                    executable = executableScriptPath;
                    if (request.Parameters.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var prop in request.Parameters.EnumerateObject())
                        {
                            arguments.Add(prop.Value.ToString());
                        }
                    }
                }

                var result = await _processRunner.RunAsync(
                    executable,
                    arguments,
                    cancellationToken,
                    timeout: TimeSpan.FromSeconds(60),
                    workingDirectory: executionWorkingDirectory).ConfigureAwait(false);

                if (result.ExitCode == 0)
                {
                    var msg = string.IsNullOrWhiteSpace(result.StandardOutput)
                        ? "Plugin script completed successfully."
                        : result.StandardOutput.Trim();
                    return CommandHandlerOutcome.Succeeded("plugin.script.ok", msg);
                }

                var errorMsg = string.IsNullOrWhiteSpace(result.StandardError)
                    ? (string.IsNullOrWhiteSpace(result.StandardOutput) ? $"Plugin script exited with code {result.ExitCode}." : result.StandardOutput.Trim())
                    : result.StandardError.Trim();

                return CommandHandlerOutcome.Failed("plugin.execution_failed", errorMsg);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException or InvalidOperationException or InvalidDataException)
            {
                return CommandHandlerOutcome.Failed("plugin.fault", $"Plugin script execution failed ({ex.GetType().Name}).");
            }
        }

        private CommandHandlerOutcome? VerifyAdmittedPayload(bool requireNonElevated)
        {
            if (!_requiresAdmission) return null;

            try
            {
                string? path = PluginAdmissionTrustStore.TryResolveAdmissionRecordPath(_pluginDirectory);
                if (path is null || !File.Exists(path))
                    return CommandHandlerOutcome.Blocked("plugin.payload_integrity_failed",
                        "The plugin admission record disappeared after registration. Reinstall the plugin.");

                var record = JsonSerializer.Deserialize<PluginAdmissionRecord>(File.ReadAllText(path));
                if (string.IsNullOrWhiteSpace(record?.PackageTreeSha256))
                    return CommandHandlerOutcome.Blocked("plugin.payload_integrity_failed",
                        "The plugin admission record does not bind script and dependency bytes. Reinstall the plugin.");

                string actualDigest = PluginAdmissionTrustStore.ComputePluginPayloadDigest(_pluginDirectory);
                if (!string.Equals(actualDigest, record.PackageTreeSha256, StringComparison.OrdinalIgnoreCase))
                    return CommandHandlerOutcome.Blocked("plugin.payload_integrity_failed",
                        "Plugin script or dependency bytes changed after approval. Reinstall and review the plugin again.");

                // User-writable script directories cannot provide an atomic, immutable
                // code snapshot to an elevated interpreter. Do not promote user-scope
                // script content into administrator execution on a timing race.
                if (requireNonElevated && PluginAdmissionTrustStore.IsCurrentProcessElevated())
                    return CommandHandlerOutcome.Blocked("plugin.elevated_script_unsupported",
                        "External script plugins cannot run elevated until protected immutable staging is available.");

                return null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                        JsonException or InvalidDataException or ArgumentException)
            {
                return CommandHandlerOutcome.Blocked("plugin.payload_integrity_failed",
                    $"Plugin integrity could not be verified ({ex.GetType().Name}).");
            }
        }

        /// <summary>
        /// Builds a non-mutating preview payload describing the script that would run on
        /// approval. No process is launched and no host state is changed.
        /// </summary>
        private CommandHandlerOutcome BuildPreviewOutcome(CommandRequest request)
        {
            JsonElement data = JsonSerializer.SerializeToElement(new
            {
                commandId = _commandId,
                action = "preview",
                interpreter = DescribeInterpreter(),
                script = Path.GetFileName(_scriptFullPath),
                declaredCapabilities = _declaredCapabilities,
                parameters = request.Parameters.ValueKind == JsonValueKind.Object
                    ? request.Parameters.Clone()
                    : JsonSerializer.SerializeToElement(new { }),
                note = "A third-party script would launch as a separate process under the current Windows session privileges, if permitted by admission policy. Review the declared capabilities before approving."
            });

            return CommandHandlerOutcome.Succeeded(
                _commandId + ".preview",
                $"Preview ready for plugin script '{Path.GetFileName(_scriptFullPath)}'. The script has NOT been executed and no host state was changed.",
                data);
        }

        private string DescribeInterpreter()
        {
            return Path.GetExtension(_scriptFullPath).ToLowerInvariant() switch
            {
                // Written as a plain literal: a PowerShell invocation must be visible to audits.
                ".ps1" => "powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File",
                ".cmd" or ".bat" => "cmd.exe /c",
                _ => "direct executable launch",
            };
        }
    }
}
