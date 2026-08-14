using System;
using System.Collections.Generic;

namespace WinCare.Domain.Sync
{
    /// <summary>
    /// Represents the encrypted cloud backup payload for user profiles, plugin lists, and settings.
    /// </summary>
    public sealed class CloudProfilePayload
    {
        /// <summary>
        /// Gets or sets the schema version of the profile payload.
        /// </summary>
        public int SchemaVersion { get; set; } = 1;

        /// <summary>
        /// Gets or sets the unique identifier for the profile.
        /// </summary>
        public string ProfileId { get; set; } = Guid.NewGuid().ToString("N");

        /// <summary>
        /// Gets or sets the device name where the profile was exported.
        /// </summary>
        public string DeviceName { get; set; } = Environment.MachineName;

        /// <summary>
        /// Gets or sets the UTC timestamp when the profile was exported.
        /// </summary>
        public DateTime ExportedAtUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Gets or sets the custom configuration key-value settings.
        /// </summary>
        public Dictionary<string, string> Settings { get; set; } = new();

        /// <summary>
        /// Gets or sets the list of installed plugin identifiers.
        /// </summary>
        public List<string> InstalledPluginIds { get; set; } = new();

        /// <summary>
        /// Gets or sets the list of favorite command identifiers.
        /// </summary>
        public List<string> FavoriteCommandIds { get; set; } = new();
    }
}
