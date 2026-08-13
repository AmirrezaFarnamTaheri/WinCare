using System.Text.Json;
using System.Text.Json.Nodes;
using WinCare.Application.Commands;
namespace WinCare.Infrastructure.Commands;

public sealed partial class WindowsCommandExecutor
{
    private async Task AppendStateItemAsync(string key, JsonElement item, CancellationToken cancellationToken)
    {
        JsonElement fallback = JsonSerializer.SerializeToElement(Array.Empty<object>());
        await _state.UpdateAsync(key, fallback, current =>
        {
            var list = current.ValueKind == JsonValueKind.Array
                ? current.EnumerateArray().Select(x => JsonNode.Parse(x.GetRawText())).ToList()
                : new List<JsonNode?>();
            if (item.ValueKind == JsonValueKind.Object &&
                item.TryGetProperty("id", out JsonElement idElement) &&
                idElement.ValueKind == JsonValueKind.String &&
                idElement.GetString() is string id &&
                list.OfType<JsonObject>().Any(existing => string.Equals(existing["id"]?.GetValue<string>(), id, StringComparison.Ordinal)))
            {
                throw new CommandParameterException("Id", $"State item '{id}' already exists.");
            }
            list.Add(JsonNode.Parse(item.GetRawText()));
            return JsonSerializer.SerializeToElement(list, JsonOptions);
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task TransitionStateItemAsync(string key, string id, JsonElement updatedItem, CancellationToken cancellationToken)
    {
        JsonElement fallback = JsonSerializer.SerializeToElement(Array.Empty<object>());
        await _state.UpdateAsync(key, fallback, current =>
        {
            var list = current.ValueKind == JsonValueKind.Array
                ? current.EnumerateArray().Select(x => JsonNode.Parse(x.GetRawText())).ToList()
                : new List<JsonNode?>();

            int existingIndex = list.FindIndex(item => item is JsonObject obj && string.Equals(obj["id"]?.GetValue<string>(), id, StringComparison.Ordinal));
            JsonNode? newNode = JsonNode.Parse(updatedItem.GetRawText());
            if (existingIndex >= 0)
            {
                list[existingIndex] = newNode;
            }
            else
            {
                list.Add(newNode);
            }
            return JsonSerializer.SerializeToElement(list, JsonOptions);
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<CommandHandlerOutcome> UpsertStateItemAsync(string key, CommandParameters p, CancellationToken cancellationToken)
    {
        string id = p.String("Id").Trim();
        if (id.Length == 0) id = Guid.NewGuid().ToString("N");
        JsonObject item = p.OptionalElement("Record") is JsonElement record && record.ValueKind == JsonValueKind.Object
            ? JsonNode.Parse(record.GetRawText())!.AsObject()
            : new JsonObject();
        item["id"] = id;
        item["updatedAt"] = DateTimeOffset.UtcNow;

        if (p.OptionalElement("Name") is JsonElement nameElement && nameElement.ValueKind == JsonValueKind.String) item["name"] = nameElement.GetString();
        if (p.OptionalElement("Path") is JsonElement pathElement && pathElement.ValueKind == JsonValueKind.String) item["path"] = pathElement.GetString();
        if (p.OptionalElement("Value") is JsonElement valueElement) item["value"] = JsonNode.Parse(valueElement.GetRawText());
        if (p.OptionalElement("Data") is JsonElement dataElement) item["data"] = JsonNode.Parse(dataElement.GetRawText());

        JsonElement fallback = JsonSerializer.SerializeToElement(Array.Empty<object>());
        await _state.UpdateAsync(key, fallback, current =>
        {
            var list = current.ValueKind == JsonValueKind.Array
                ? current.EnumerateArray().Select(x => JsonNode.Parse(x.GetRawText())).Where(x => x is not null).Cast<JsonNode>().ToList()
                : new List<JsonNode>();
            int index = list.FindIndex(node => node is JsonObject obj && string.Equals(obj["id"]?.GetValue<string>(), id, StringComparison.Ordinal));
            if (index >= 0) list[index] = item; else list.Add(item);
            return JsonSerializer.SerializeToElement(list, JsonOptions);
        }, cancellationToken).ConfigureAwait(false);

        return Success(key, $"Saved '{id}' in WinCare state '{key}'.", item, undo: false);
    }

    private async Task<CommandHandlerOutcome> RemoveStateItemAsync(string key, string id, CancellationToken cancellationToken)
    {
        bool found = false;
        JsonElement fallback = JsonSerializer.SerializeToElement(Array.Empty<object>());
        await _state.UpdateAsync(key, fallback, current =>
        {
            if (current.ValueKind != JsonValueKind.Array) return current;
            var list = new List<JsonNode?>();
            foreach (JsonElement x in current.EnumerateArray())
            {
                if (x.ValueKind == JsonValueKind.Object && x.TryGetProperty("id", out JsonElement idElement) && string.Equals(idElement.GetString(), id, StringComparison.Ordinal))
                {
                    found = true;
                    continue;
                }
                list.Add(JsonNode.Parse(x.GetRawText()));
            }
            return JsonSerializer.SerializeToElement(list, JsonOptions);
        }, cancellationToken).ConfigureAwait(false);

        if (!found) return Block(key, $"State item '{id}' was not found.");
        return Success(key, $"Removed '{id}' from WinCare state '{key}'.", new { id, removed = true }, undo: false);
    }

    private async Task PatchStateItemAsync(string key, string id, Action<JsonObject> patch, CancellationToken cancellationToken)
    {
        JsonElement fallback = JsonSerializer.SerializeToElement(Array.Empty<object>());
        bool found = false;
        await _state.UpdateAsync(key, fallback, current =>
        {
            var list = current.ValueKind == JsonValueKind.Array
                ? current.EnumerateArray().Select(x => JsonNode.Parse(x.GetRawText())).Where(x => x is not null).Cast<JsonNode>().ToList()
                : new List<JsonNode>();
            JsonObject? item = list.OfType<JsonObject>().FirstOrDefault(obj => string.Equals(obj["id"]?.GetValue<string>(), id, StringComparison.Ordinal));
            if (item is null) throw new CommandParameterException("Id", $"State item '{id}' was not found.");
            found = true;
            patch(item);
            return JsonSerializer.SerializeToElement(list, JsonOptions);
        }, cancellationToken).ConfigureAwait(false);

        if (!found) throw new CommandParameterException("Id", $"State item '{id}' was not found.");
    }

    private async Task<CommandHandlerOutcome> ExportStateAsync(string key, CommandParameters p, string defaultName, CancellationToken cancellationToken)
    {
        JsonElement data = await _state.ReadArrayAsync(key, cancellationToken).ConfigureAwait(false);
        string path = _state.ResolveExportPath(p.String("Path"), defaultName);
        await WriteJsonExportAsync(path, data, cancellationToken).ConfigureAwait(false);
        return Success(key + "-export", $"Exported WinCare state '{key}'.", new { path, count = data.ValueKind == JsonValueKind.Array ? data.GetArrayLength() : 1 });
    }

    private static async Task WriteJsonExportAsync(string path, JsonElement data, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using FileStream stream = new(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true);
            await JsonSerializer.SerializeAsync(stream, data, JsonOptions, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch (IOException) { }
        }
    }

    private async Task<CommandHandlerOutcome> MaintenanceCreateAsync(CommandParameters p, CancellationToken cancellationToken)
    {
        string id = p.String("Id").Trim(); if (id.Length == 0) id = Guid.NewGuid().ToString("N");
        string name = p.RequiredString("Name");
        DateTimeOffset start = p.DateTimeOffset("StartAt", DateTimeOffset.UtcNow);
        DateTimeOffset end = p.DateTimeOffset("EndAt", start.AddHours(1));
        if (end <= start) throw new CommandParameterException("EndAt", "EndAt must be later than StartAt.");
        JsonElement record = Data(new
        {
            id,
            name,
            startAt = start,
            endAt = end,
            description = p.String("Description"),
            playbookId = p.String("PlaybookId"),
            tags = p.StringArray("Tags"),
            requiresRestart = p.Boolean("RequiresRestart"),
            state = "Scheduled",
            createdAt = DateTimeOffset.UtcNow,
        });
        await AppendStateItemAsync("maintenance-windows", record, cancellationToken).ConfigureAwait(false);
        return Success("maintenance-create", "Maintenance window created.", record, undo: false);
    }

    private async Task<CommandHandlerOutcome> MaintenanceTransitionAsync(CommandParameters p, CancellationToken cancellationToken)
    {
        string id = p.RequiredString("Id");
        string state = p.RequiredString("State");
        string[] allowed = ["Scheduled", "Running", "Completed", "Cancelled"];
        if (!allowed.Contains(state, StringComparer.OrdinalIgnoreCase)) throw new CommandParameterException("State", "State must be Scheduled, Running, Completed, or Cancelled.");
        await PatchStateItemAsync("maintenance-windows", id, item =>
        {
            item["state"] = allowed.First(x => x.Equals(state, StringComparison.OrdinalIgnoreCase));
            item["updatedAt"] = DateTimeOffset.UtcNow;
        }, cancellationToken).ConfigureAwait(false);
        return Success("maintenance-transition", $"Maintenance window '{id}' changed to {state}.", new { id, state });
    }

    private async Task<CommandHandlerOutcome> MaintenanceMetricsAsync(CancellationToken cancellationToken)
    {
        JsonElement data = await _state.ReadArrayAsync("maintenance-windows", cancellationToken).ConfigureAwait(false);
        var states = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (data.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in data.EnumerateArray())
            {
                string state = item.TryGetProperty("state", out JsonElement value) ? value.GetString() ?? "Unknown" : "Unknown";
                states[state] = states.GetValueOrDefault(state) + 1;
            }
        }
        return Success("maintenance-metrics", "Maintenance metrics calculated from durable WinCare state.", new { total = data.ValueKind == JsonValueKind.Array ? data.GetArrayLength() : 0, byState = states });
    }

    private async Task<CommandHandlerOutcome> NoteSaveAsync(CommandParameters p, CancellationToken cancellationToken)
    {
        string id = p.String("Id").Trim(); if (id.Length == 0) id = Guid.NewGuid().ToString("N");
        string text = p.RequiredString("Text");
        if (text.Length > 256 * 1024) throw new CommandParameterException("Text", "Note text exceeds 256 KiB.");
        string title = p.String("Title", text.Split('\n')[0].Trim());
        JsonElement emptyFallback = Data(new object[0]);

        JsonNode? savedNode = null;
        await _state.UpdateAsync("notes", emptyFallback, current =>
        {
            var list = current.ValueKind == JsonValueKind.Array ? current.EnumerateArray().Select(x => JsonNode.Parse(x.GetRawText())).Where(x => x is not null).Cast<JsonNode>().ToList() : new List<JsonNode>();
            JsonObject? item = list.OfType<JsonObject>().FirstOrDefault(x => string.Equals(x["id"]?.GetValue<string>(), id, StringComparison.Ordinal));
            if (item is null)
            {
                item = new JsonObject { ["id"] = id, ["createdAt"] = DateTimeOffset.UtcNow };
                list.Add(item);
            }
            item["title"] = title;
            item["text"] = text;
            item["updatedAt"] = DateTimeOffset.UtcNow;
            savedNode = item;
            return JsonSerializer.SerializeToElement(list, JsonOptions);
        }, cancellationToken).ConfigureAwait(false);

        return Success("note-save", "Note saved.", savedNode, undo: false);
    }

    private async Task<CommandHandlerOutcome> RemoteConsentCreateAsync(CommandParameters p, CancellationToken cancellationToken)
    {
        string id = Guid.NewGuid().ToString("N");
        int minutes = p.Int32("DurationMinutes", 30, 1, 1440);
        JsonElement record = Data(new
        {
            id,
            subject = p.RequiredString("Subject"),
            scope = p.String("Scope", "diagnostics"),
            state = "Active",
            createdAt = DateTimeOffset.UtcNow,
            expiresAt = DateTimeOffset.UtcNow.AddMinutes(minutes),
        });
        await AppendStateItemAsync("remote-consents", record, cancellationToken).ConfigureAwait(false);
        return Success("remote-consent-create", "Remote support consent token created in local WinCare state.", record, undo: false);
    }

    private async Task<CommandHandlerOutcome> RemoteConsentStateAsync(CommandParameters p, CancellationToken cancellationToken)
    {
        string id = p.RequiredString("Id");
        string state = p.RequiredString("State");
        if (!new[] { "Active", "Revoked", "Expired" }.Contains(state, StringComparer.OrdinalIgnoreCase)) throw new CommandParameterException("State", "State must be Active, Revoked, or Expired.");
        await PatchStateItemAsync("remote-consents", id, item => { item["state"] = state; item["updatedAt"] = DateTimeOffset.UtcNow; }, cancellationToken).ConfigureAwait(false);
        return Success("remote-consent-state", "Remote support consent state updated.", new { id, state });
    }

    private async Task<CommandHandlerOutcome> RemoteConsentExpireAsync(CommandParameters p, CancellationToken cancellationToken)
    {
        string id = p.RequiredString("Id");
        await PatchStateItemAsync("remote-consents", id, item => { item["state"] = "Expired"; item["expiresAt"] = DateTimeOffset.UtcNow; }, cancellationToken).ConfigureAwait(false);
        return Success("remote-consent-expire", "Remote support consent expired.", new { id, state = "Expired" });
    }

    private async Task<CommandHandlerOutcome> RemoteEmergencyAsync(CommandParameters p, CancellationToken cancellationToken)
    {
        JsonElement emptyFallback = Data(new object[0]);
        await _state.UpdateAsync("remote-consents", emptyFallback, current =>
        {
            if (current.ValueKind != JsonValueKind.Array) return current;
            var list = current.EnumerateArray().Select(x => JsonNode.Parse(x.GetRawText())!).ToList();
            foreach (JsonObject item in list.OfType<JsonObject>())
            {
                item["state"] = "Revoked";
                item["updatedAt"] = DateTimeOffset.UtcNow;
            }
            return JsonSerializer.SerializeToElement(list, JsonOptions);
        }, cancellationToken).ConfigureAwait(false);
        return Success("remote-emergency", "All local remote-support consent tokens were revoked.", new { revokedAt = DateTimeOffset.UtcNow });
    }

    private async Task<CommandHandlerOutcome> TelemetryCaptureAsync(CancellationToken cancellationToken)
    {
        JsonElement snapshot = TelemetrySnapshot().Data ?? Data(new { });
        var node = JsonNode.Parse(snapshot.GetRawText())!.AsObject();
        node["id"] = Guid.NewGuid().ToString("N");
        node["capturedAt"] = DateTimeOffset.UtcNow;
        JsonElement item = JsonSerializer.SerializeToElement(node, JsonOptions);
        await AppendStateItemAsync("telemetry-history", item, cancellationToken).ConfigureAwait(false);
        return Success("telemetry-capture", "Local telemetry snapshot captured.", item);
    }

    private async Task<CommandHandlerOutcome> TelemetryIngestAsync(CommandParameters p, CancellationToken cancellationToken)
    {
        JsonElement record = p.Element("Record");
        JsonElement item = Data(new { id = Guid.NewGuid().ToString("N"), name = p.RequiredString("Name"), timestamp = p.DateTimeOffset("Timestamp", DateTimeOffset.UtcNow), record });
        await AppendStateItemAsync("telemetry-lake", item, cancellationToken).ConfigureAwait(false);
        return Success("telemetry-ingest", "Telemetry record ingested into the local WinCare lake.", item);
    }

    private async Task<CommandHandlerOutcome> TelemetryRetentionAsync(CommandParameters p, CancellationToken cancellationToken)
    {
        int days = p.Int32("RetentionDays", 30, 1, 3650);
        DateTimeOffset threshold = DateTimeOffset.UtcNow.AddDays(-days);
        JsonElement emptyFallback = Data(new object[0]);
        int removed = 0;
        int remaining = 0;

        await _state.UpdateAsync("telemetry-lake", emptyFallback, current =>
        {
            var kept = new List<JsonNode?>();
            removed = 0;
            if (current.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in current.EnumerateArray())
                {
                    DateTimeOffset timestamp = item.TryGetProperty("timestamp", out JsonElement ts) && ts.TryGetDateTimeOffset(out DateTimeOffset parsed) ? parsed : DateTimeOffset.UtcNow;
                    if (timestamp >= threshold) kept.Add(JsonNode.Parse(item.GetRawText())); else removed++;
                }
            }
            remaining = kept.Count;
            return JsonSerializer.SerializeToElement(kept, JsonOptions);
        }, cancellationToken).ConfigureAwait(false);

        return Success("telemetry-retention", $"Telemetry retention applied; removed {removed} expired records.", new { retentionDays = days, removed, remaining });
    }

    private async Task<CommandHandlerOutcome> TelemetryLakeRecordsAsync(CommandParameters p, CancellationToken cancellationToken)
    {
        JsonElement current = await _state.ReadArrayAsync("telemetry-lake", cancellationToken).ConfigureAwait(false);
        DateTimeOffset since = p.DateTimeOffset("Since", DateTimeOffset.UtcNow.AddDays(-1));
        DateTimeOffset until = p.DateTimeOffset("Until", DateTimeOffset.UtcNow);
        int max = p.Int32("MaximumRecords", 10_000, 1, 100_000);
        string name = p.String("Name");
        var rows = new List<JsonElement>();
        if (current.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in current.EnumerateArray())
            {
                DateTimeOffset ts = item.TryGetProperty("timestamp", out JsonElement e) && e.TryGetDateTimeOffset(out DateTimeOffset parsed) ? parsed : DateTimeOffset.MinValue;
                string itemName = item.TryGetProperty("name", out JsonElement n) ? n.GetString() ?? string.Empty : string.Empty;
                if (ts >= since && ts <= until && (name.Length == 0 || itemName.Equals(name, StringComparison.OrdinalIgnoreCase))) rows.Add(item.Clone());
                if (rows.Count >= max) break;
            }
        }
        return Success("telemetry-lake-records", $"Returned {rows.Count} telemetry lake records.", rows);
    }

    private async Task<CommandHandlerOutcome> TelemetryLakeAggregateAsync(CommandParameters p, CancellationToken cancellationToken)
    {
        CommandHandlerOutcome records = await TelemetryLakeRecordsAsync(p, cancellationToken).ConfigureAwait(false);
        JsonElement data = records.Data ?? Data(Array.Empty<object>());
        var byName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (data.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in data.EnumerateArray())
            {
                string name = item.TryGetProperty("name", out JsonElement n) ? n.GetString() ?? "unknown" : "unknown";
                byName[name] = byName.GetValueOrDefault(name) + 1;
            }
        }
        return Success("telemetry-lake", "Telemetry lake aggregate calculated.", new { total = data.ValueKind == JsonValueKind.Array ? data.GetArrayLength() : 0, byName });
    }
}
