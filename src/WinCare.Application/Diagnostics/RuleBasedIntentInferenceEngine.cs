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

        // Source-Driven Development Citation:
        // Pattern: Return Task.FromResult for synchronous CPU-bound operations in asynchronous contracts
        // Source: https://learn.microsoft.com/en-us/dotnet/csharp/asynchronous-programming/async-scenarios
        // "Avoid Task.Yield() or artificial thread pool dispatches when operations are in-memory and non-blocking."
        public Task<bool> InitializeAsync(CancellationToken cancellationToken = default)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                _isInitialized = true;
                return Task.FromResult(true);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[RuleBasedIntentInferenceEngine] Init error: {ex.Message}");
                _isInitialized = false;
                return Task.FromResult(false);
            }
            finally
            {
                sw.Stop();
                _coldStartDuration = sw.Elapsed;
            }
        }

        public Task<string> PredictIntentAsync(string prompt, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
            cancellationToken.ThrowIfCancellationRequested();
            if (!_isInitialized)
            {
                _isInitialized = true;
            }

            // Deterministic heuristic intent parser mapping semantic tokens to domains.
            var lower = prompt.ToLowerInvariant();

            if (lower.Contains("dns") || lower.Contains("network") || lower.Contains("internet") || lower.Contains("ping") || lower.Contains("wifi") || lower.Contains("winsock"))
            {
                return Task.FromResult("intent.network.flush");
            }
            if (lower.Contains("privacy") || lower.Contains("telemetry") || lower.Contains("tracking") || lower.Contains("spy"))
            {
                return Task.FromResult("intent.privacy.harden");
            }
            if (lower.Contains("winget") || lower.Contains("outdated") || (lower.Contains("update") && !lower.Contains("clean_updates")))
            {
                return Task.FromResult("intent.apps.update");
            }
            if (lower.Contains("disk") || lower.Contains("drive") || lower.Contains("storage") || lower.Contains("full") || lower.Contains("temp") || lower.Contains("junk"))
            {
                return Task.FromResult("intent.storage.cleanup");
            }
            if (lower.Contains("ram") || lower.Contains("memory") || lower.Contains("slow") || lower.Contains("freeze") || lower.Contains("lag"))
            {
                return Task.FromResult("intent.memory.optimize");
            }

            return Task.FromResult("intent.general.diagnose");
        }
    }
}
