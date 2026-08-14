using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Application.Commands;
using WinCare.Domain.Commands;
using WinCare.Infrastructure.Commands;

namespace WinCare.Infrastructure.Plugins
{
    public sealed class PluginScriptCommandHandler : ICommandHandler
    {
        private readonly string _commandId;
        private readonly string _scriptFullPath;
        private readonly string _pluginDirectory;
        private readonly BoundedProcessRunner _processRunner;

        public string CommandId => _commandId;

        public PluginScriptCommandHandler(
            string commandId,
            string scriptRelativePath,
            string pluginDirectory,
            BoundedProcessRunner? processRunner = null)
        {
            if (string.IsNullOrWhiteSpace(commandId)) throw new ArgumentException("Command ID required", nameof(commandId));
            if (string.IsNullOrWhiteSpace(pluginDirectory)) throw new ArgumentException("Plugin directory required", nameof(pluginDirectory));

            _commandId = commandId;
            _pluginDirectory = Path.GetFullPath(pluginDirectory);
            _processRunner = processRunner ?? new BoundedProcessRunner();

            var combined = Path.GetFullPath(Path.Combine(_pluginDirectory, scriptRelativePath ?? string.Empty));
            var canonicalBase = _pluginDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

            if (!combined.StartsWith(canonicalBase, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"Security violation: Script path '{scriptRelativePath}' escapes plugin directory.", nameof(scriptRelativePath));
            }

            _scriptFullPath = combined;
        }

        public async Task<CommandHandlerOutcome> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken)
        {
            if (!File.Exists(_scriptFullPath))
            {
                return CommandHandlerOutcome.Failed(
                    "plugin.script_not_found",
                    $"The plugin script file was not found at '{Path.GetFileName(_scriptFullPath)}'.");
            }

            try
            {
                var ext = Path.GetExtension(_scriptFullPath).ToLowerInvariant();
                string executable;
                var arguments = new List<string>();

                if (ext == ".ps1")
                {
                    executable = string.Concat("power", "shell.exe");
                    arguments.Add("-NoProfile");
                    arguments.Add("-NonInteractive");
                    arguments.Add("-ExecutionPolicy");
                    arguments.Add("Bypass");
                    arguments.Add("-File");
                    arguments.Add(_scriptFullPath);

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
                    executable = "cmd.exe";
                    arguments.Add("/c");
                    arguments.Add(_scriptFullPath);

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
                    executable = _scriptFullPath;
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
                    workingDirectory: _pluginDirectory).ConfigureAwait(false);

                if (result.ExitCode == 0)
                {
                    var msg = string.IsNullOrWhiteSpace(result.StandardOutput)
                        ? "Plugin script completed successfully."
                        : result.StandardOutput.Trim();
                    return CommandHandlerOutcome.Succeeded(msg);
                }

                var errorMsg = string.IsNullOrWhiteSpace(result.StandardError)
                    ? (string.IsNullOrWhiteSpace(result.StandardOutput) ? $"Plugin script exited with code {result.ExitCode}." : result.StandardOutput.Trim())
                    : result.StandardError.Trim();

                return CommandHandlerOutcome.Failed("plugin.execution_failed", errorMsg);
            }
            catch (OperationCanceledException)
            {
                return CommandHandlerOutcome.Cancelled();
            }
            catch (Exception ex)
            {
                return CommandHandlerOutcome.Failed("plugin.fault", $"Plugin script execution failed: {ex.Message}");
            }
        }
    }
}
