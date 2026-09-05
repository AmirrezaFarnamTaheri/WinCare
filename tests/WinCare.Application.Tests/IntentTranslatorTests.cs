using System;
using System.Linq;
using System.Threading.Tasks;
using WinCare.Application.Diagnostics;
using WinCare.Application.Tools;
using Xunit;

namespace WinCare.Application.Tests
{
    public sealed class IntentTranslatorTests
    {
        private readonly ToolCatalogService _catalogService = new();
        private readonly RuleBasedIntentInferenceEngine _inferenceEngine = new();
        private readonly IntentTranslator _translator;

        public IntentTranslatorTests()
        {
            _translator = new IntentTranslator(_inferenceEngine, _catalogService);
        }

        [Fact]
        public async Task RuleBasedIntentInferenceEngine_initializes_within_latency_budget()
        {
            var success = await _inferenceEngine.InitializeAsync();
            Assert.True(success);
            Assert.True(_inferenceEngine.IsInitialized);
            Assert.True(_inferenceEngine.ColdStartDuration < TimeSpan.FromSeconds(1.5));
        }

        [Theory]
        [InlineData("My C drive is almost full, please clean it up", "intent.storage.cleanup")]
        [InlineData("RAM memory usage is high and games lag", "intent.memory.optimize")]
        [InlineData("Internet is slow, flush DNS cache", "intent.network.flush")]
        [InlineData("Disable telemetry and Windows tracking", "intent.privacy.harden")]
        public async Task PredictIntent_maps_natural_language_queries(string query, string expectedIntent)
        {
            var intent = await _inferenceEngine.PredictIntentAsync(query);
            Assert.Equal(expectedIntent, intent);
        }

        [Fact]
        public async Task TranslateAsync_produces_valid_action_plan_matching_catalog()
        {
            var plan = await _translator.TranslateAsync("Clean up junk and temporary files from my computer");

            Assert.NotNull(plan);
            Assert.StartsWith("plan_", plan.PlanId);
            Assert.NotEmpty(plan.Findings);
            Assert.NotEmpty(plan.ProposedSteps);

            // Verify all recommended action step command IDs strictly exist in ToolCatalogService
            foreach (var step in plan.ProposedSteps)
            {
                var match = _catalogService.All.FirstOrDefault(c => c.Id.Equals(step.CommandId, StringComparison.OrdinalIgnoreCase));
                Assert.NotNull(match);
                Assert.Equal(match.Id, step.CommandId);
                Assert.Equal(match.Risk, step.RiskLevel);
            }
        }

        [Fact]
        public async Task TranslateAsync_attaches_parameters_to_parameterized_recommendations()
        {
            var plan = await _translator.TranslateAsync("Clean up junk and temporary files from my computer");

            var mutatingStep = Assert.Single(plan.ProposedSteps, s => s.CommandId == "cleaner-disk-pressure");
            Assert.NotNull(mutatingStep.Parameters);
            Assert.True(mutatingStep.Parameters!.ContainsKey("OlderThanDays"));
            Assert.Equal("7", mutatingStep.Parameters["OlderThanDays"]);
        }

        [Fact]
        public async Task TranslateAsync_handles_general_diagnostic_query()
        {
            var plan = await _translator.TranslateAsync("Check overall system health");

            Assert.NotNull(plan);
            Assert.Equal(DiagnosticSeverity.Information, plan.OverallSeverity);
            Assert.DoesNotContain(plan.Findings, f => f.Severity == DiagnosticSeverity.Healthy);
            Assert.NotEmpty(plan.ProposedSteps);
            Assert.All(plan.ProposedSteps, step => Assert.True(step.IsReadOnly));
        }

        [Fact]
        public async Task Cancelled_classification_does_not_produce_an_intent()
        {
            using var cancellation = new System.Threading.CancellationTokenSource();
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                _inferenceEngine.PredictIntentAsync("system health", cancellation.Token));
            Assert.False(_inferenceEngine.IsInitialized);
        }

        [Fact]
        public async Task TranslateAsync_collects_live_telemetry_evidence()
        {
            var plan = await _translator.TranslateAsync("My computer memory is running slow and lagging");

            Assert.NotNull(plan);
            Assert.NotEmpty(plan.MeasuredEvidence);
            var memoryProbe = Assert.Single(plan.MeasuredEvidence, e => e.MetricName.Contains("Memory", StringComparison.OrdinalIgnoreCase));
            Assert.True(memoryProbe.HasMeasuredEvidence);
            Assert.False(string.IsNullOrEmpty(memoryProbe.MeasuredValue));
        }

        [Fact]
        public async Task TranslateAsync_attaches_provenance_to_findings()
        {
            var plan = await _translator.TranslateAsync("Check memory pressure");
            Assert.NotNull(plan);
            foreach (var evidence in plan.MeasuredEvidence)
            {
                Assert.NotNull(evidence.Collector);
                Assert.NotNull(evidence.CommandVersion);
                Assert.Contains(evidence.Collector, evidence.ProvenanceSummary);
            }
        }
    }
}
