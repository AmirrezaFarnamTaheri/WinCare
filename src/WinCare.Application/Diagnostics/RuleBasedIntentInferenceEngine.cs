using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace WinCare.Application.Diagnostics
{
    /// <summary>
    /// Contract for classifying a free-text symptom description into a diagnostic intent.
    /// </summary>
    public interface IIntentInferenceEngine
    {
        bool IsInitialized { get; }
        TimeSpan ColdStartDuration { get; }
        Task<bool> InitializeAsync(CancellationToken cancellationToken = default);
        Task<string> PredictIntentAsync(string prompt, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Deterministic, on-device rule-based intent classifier for the AI System Doctor.
    /// </summary>
    /// <remarks>
    /// This classifier is intentionally a keyword/heuristic matcher. It performs no neural
    /// inference and requires no model weights — there is no ONNX Runtime or DirectML
    /// dependency in this application. "Initialization" therefore only records the
    /// (near-zero) cold-start cost so callers and diagnostics can report it honestly.
    /// </remarks>
    public sealed class RuleBasedIntentInferenceEngine : IIntentInferenceEngine
    {
        private bool _isInitialized;
        private TimeSpan _coldStartDuration;

        public bool IsInitialized => _isInitialized;
        public TimeSpan ColdStartDuration => _coldStartDuration;

        public async Task<bool> InitializeAsync(CancellationToken cancellationToken = default)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                // No model or accelerator session is loaded: classification is rule-based.
                await Task.Yield();
                cancellationToken.ThrowIfCancellationRequested();
                _isInitialized = true;
                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[RuleBasedIntentInferenceEngine] Init error: {ex.Message}");
                _isInitialized = false;
                return false;
            }
            finally
            {
                sw.Stop();
                _coldStartDuration = sw.Elapsed;
            }
        }

        public async Task<string> PredictIntentAsync(string prompt, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
            cancellationToken.ThrowIfCancellationRequested();
            if (!_isInitialized)
            {
                await InitializeAsync(cancellationToken);
            }

            // Deterministic heuristic intent parser mapping semantic tokens to domains.
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            var lower = prompt.ToLowerInvariant();

            if (lower.Contains("dns") || lower.Contains("network") || lower.Contains("internet") || lower.Contains("ping") || lower.Contains("wifi") || lower.Contains("winsock"))
            {
                return "intent.network.flush";
            }
            if (lower.Contains("privacy") || lower.Contains("telemetry") || lower.Contains("tracking") || lower.Contains("spy"))
            {
                return "intent.privacy.harden";
            }
            if (lower.Contains("winget") || lower.Contains("outdated") || (lower.Contains("update") && !lower.Contains("clean_updates")))
            {
                return "intent.apps.update";
            }
            if (lower.Contains("disk") || lower.Contains("drive") || lower.Contains("storage") || lower.Contains("full") || lower.Contains("temp") || lower.Contains("junk"))
            {
                return "intent.storage.cleanup";
            }
            if (lower.Contains("ram") || lower.Contains("memory") || lower.Contains("slow") || lower.Contains("freeze") || lower.Contains("lag"))
            {
                return "intent.memory.optimize";
            }

            return "intent.general.diagnose";
        }
    }
}
