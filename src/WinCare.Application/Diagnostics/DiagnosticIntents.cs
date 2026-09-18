namespace WinCare.Application.Diagnostics
{
    /// <summary>
    /// Diagnostic intent keys produced by the rule-based classifier and consumed by intent
    /// translation and evidence collection. Every class that switches on or produces an intent
    /// refers to these constants so the three cannot drift apart and silently misroute evidence.
    /// </summary>
    internal static class DiagnosticIntents
    {
        /// <summary>Network or DNS connectivity and winsock state.</summary>
        public const string NetworkFlush = "intent.network.flush";

        /// <summary>Privacy, diagnostic telemetry and tracking surface review.</summary>
        public const string PrivacyHarden = "intent.privacy.harden";

        /// <summary>Application inventory and available updates.</summary>
        public const string AppsUpdate = "intent.apps.update";

        /// <summary>Storage pressure and cleanup candidates.</summary>
        public const string StorageCleanup = "intent.storage.cleanup";

        /// <summary>Memory pressure and working-set optimization.</summary>
        public const string MemoryOptimize = "intent.memory.optimize";

        /// <summary>Unspecific complaint; collect a general system baseline.</summary>
        public const string GeneralDiagnose = "intent.general.diagnose";
    }
}
