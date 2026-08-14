using System;
using System.Collections.Generic;

namespace WinCare.Domain.Sync
{
    public sealed class CloudProfilePayload
    {
        public int SchemaVersion { get; set; } = 1;
        public string ProfileId { get; set; } = Guid.NewGuid().ToString("N");
        public string DeviceName { get; set; } = Environment.MachineName;
        public DateTime ExportedAtUtc { get; set; } = DateTime.UtcNow;
        public Dictionary<string, string> Settings { get; set; } = new();
        public List<string> InstalledPluginIds { get; set; } = new();
        public List<string> FavoriteCommandIds { get; set; } = new();
    }
}
