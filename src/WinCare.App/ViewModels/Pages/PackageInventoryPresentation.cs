using System.Text.Json;

namespace WinCare.App.ViewModels.Pages;

public static class PackageInventoryPresentation
{
    private const int MaximumPresentedRows = 200;

    public static string Format(string commandId, JsonElement? payload) => commandId switch
    {
        "storage-report" => FormatStorageReport(payload),
        "app-residual-discovery" => FormatResidualDiscovery(payload),
        "installer-cache-analysis" => FormatInstallerCache(payload),
        "winget-upgrade-inventory" => FormatWingetUpgrades(payload),
        _ => Format(payload),
    };

    public static string Format(JsonElement? payload)
    {
        if (payload is not { ValueKind: JsonValueKind.Object } data) return string.Empty;
        bool installed = data.TryGetProperty("packageFullNames", out JsonElement names);
        if (!installed && !data.TryGetProperty("packageNames", out names)) return FormatOutcomes(data);
        if (names.ValueKind != JsonValueKind.Array) return string.Empty;
        string[] packages = names.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString()!).ToArray();
        string heading = installed ? "Registered packages for the current user" : "Packages provisioned in the Windows image";
        string guidance = data.TryGetProperty("removalIdentity", out var identity) && identity.ValueKind == JsonValueKind.String
            ? identity.GetString()! : "Inspect the matching removal tool to review its scope and requirements.";
        string listing = packages.Length == 0 ? "No packages were returned." : string.Join("\n", packages.Take(500));
        if (packages.Length > 500) listing += "\n\nShowing the first 500 packages. The complete list is in Technical result.";
        return $"{heading}\n{packages.Length} packages\n\n{guidance}\n\n{listing}";
    }

    private static string FormatOutcomes(JsonElement data)
    {
        if (!data.TryGetProperty("packages", out var packages) || packages.ValueKind != JsonValueKind.Array) return string.Empty;
        var rows = new List<string>();
        foreach (var package in packages.EnumerateArray().Take(500))
        {
            if (package.ValueKind != JsonValueKind.Object) continue;
            string name = Read(package, "packageFullName");
            if (name.Length == 0) name = Read(package, "package");
            string outcome = Read(package, "outcome");
            if (name.Length == 0 || outcome.Length == 0) continue;
            string detail = Read(package, "errorText");
            if (detail.Length == 0) detail = Read(package, "exitCodeMeaning");
            rows.Add($"{name}\n{outcome}" + (detail.Length > 0 ? $" · {detail}" : string.Empty));
        }
        if (rows.Count == 0) return string.Empty;
        string scope = Read(data, "effect");
        if (scope.Length == 0 && Read(data, "scope") == "CurrentUser") scope = "Current-user app registrations only.";
        string result = "Package outcomes\n" + scope + "\n\n" + string.Join("\n\n", rows);
        if (packages.GetArrayLength() > 500) result += "\n\nShowing the first 500 outcomes. See Technical result for the complete receipt.";
        return result;
    }

    private static string Read(JsonElement item, string property) =>
        item.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : string.Empty;

    private static string FormatStorageReport(JsonElement? payload)
    {
        if (payload is not { ValueKind: JsonValueKind.Object } data ||
            !data.TryGetProperty("RootPath", out JsonElement root)) return string.Empty;
        string status = ReadBool(data, "IsPartial") ? "Partial evidence" : "Complete within the selected bounds";
        var lines = new List<string>
        {
            "Storage evidence",
            root.GetString() ?? string.Empty,
            $"{FormatBytes(ReadUInt64(data, "AccountedBytes"))} accounted · {ReadInt32(data, "FilesSeen"):N0} files · {ReadInt32(data, "DirectoriesSeen"):N0} folders",
            status,
            $"{ReadInt32(data, "ReparsePointsSkipped"):N0} reparse points skipped · {ReadInt32(data, "HardLinkDuplicatesSkipped"):N0} duplicate hard links excluded",
        };
        AppendRows(lines, data, "LargestFiles", item =>
            $"{FormatBytes(ReadUInt64(item, "Bytes"))}  {Read(item, "Path")}", "Largest files");
        AppendLimitNotice(lines, data, "LargestFiles");
        return string.Join("\n", lines.Where(line => line.Length > 0));
    }

    private static string FormatResidualDiscovery(JsonElement? payload)
    {
        if (payload is not { ValueKind: JsonValueKind.Object } data ||
            !data.TryGetProperty("Candidates", out JsonElement candidates) || candidates.ValueKind != JsonValueKind.Array) return string.Empty;
        var lines = new List<string>
        {
            "Application residual evidence",
            $"{ReadInt32(data, "ApplicationsInspected"):N0} installed-app records inspected · {candidates.GetArrayLength():N0} candidates",
            ReadBool(data, "IsPartial") ? "Partial evidence—review scan issues in Technical result." : "Discovery completed within its configured bounds.",
            "No files were selected, moved, or removed.",
        };
        lines.Add(string.Empty);
        foreach (JsonElement item in candidates.EnumerateArray().Take(MaximumPresentedRows))
        {
            string owner = item.TryGetProperty("Owner", out JsonElement ownerData) ? Read(ownerData, "DisplayName") : string.Empty;
            lines.Add(owner.Length == 0 ? "Unidentified application record" : owner);
            lines.Add($"{FormatBytes(ReadUInt64(item, "Bytes"))}  {Read(item, "Path")}");
            lines.Add($"Ownership confidence: {Read(item, "OwnerConfidence")} · Recovery: {(ReadBool(item, "RecoveryAvailable") ? "available" : "not available")}");
            lines.Add(Read(item, "Reason"));
            lines.Add(string.Empty);
        }
        if (candidates.GetArrayLength() > MaximumPresentedRows) lines.Add($"Showing the first {MaximumPresentedRows} candidates. The complete evidence is in Technical result.");
        return string.Join("\n", lines).TrimEnd();
    }

    private static string FormatInstallerCache(JsonElement? payload)
    {
        if (payload is not { ValueKind: JsonValueKind.Object } data ||
            !data.TryGetProperty("files", out JsonElement files) || files.ValueKind != JsonValueKind.Array) return string.Empty;
        int registered = 0;
        int unmatched = 0;
        foreach (JsonElement item in files.EnumerateArray())
        {
            if (Read(item, "registrationState") == "registered") registered++; else unmatched++;
        }
        var lines = new List<string>
        {
            "Windows Installer cache evidence",
            $"{files.GetArrayLength():N0} cache files inspected · {registered:N0} referenced · {unmatched:N0} unmatched",
            ReadBool(data, "scanComplete") ? "The bounded evidence scan completed." : "Evidence is incomplete—review errors and truncation in Technical result.",
            "Every file remains retained. An unmatched file is not a deletion recommendation.",
        };
        lines.Add(string.Empty);
        foreach (JsonElement item in files.EnumerateArray().Take(MaximumPresentedRows))
        {
            lines.Add($"{FormatBytes(ReadUInt64(item, "size"))}  {Read(item, "path")}");
            lines.Add($"{Read(item, "registrationState")} · {Read(item, "retentionReason")}");
            lines.Add(string.Empty);
        }
        if (files.GetArrayLength() > MaximumPresentedRows) lines.Add($"Showing the first {MaximumPresentedRows} files. The complete evidence is in Technical result.");
        return string.Join("\n", lines).TrimEnd();
    }

    private static string FormatWingetUpgrades(JsonElement? payload)
    {
        if (payload is not { ValueKind: JsonValueKind.Object } data ||
            !data.TryGetProperty("inventory", out JsonElement inventory)) return string.Empty;
        JsonElement[] packages = Descendants(inventory)
            .Where(item => ReadAny(item, "PackageIdentifier", "Id").Length > 0 && ReadAny(item, "AvailableVersion", "Version").Length > 0)
            .Take(MaximumPresentedRows + 1).ToArray();
        var lines = new List<string>
        {
            "Available app updates",
            packages.Length == 0 ? "WinGet returned no package upgrade rows." : $"{Math.Min(packages.Length, MaximumPresentedRows):N0} upgrade rows returned by WinGet.",
            "Read-only inventory. No package was installed, upgraded, removed, or pinned.",
        };
        foreach (JsonElement item in packages.Take(MaximumPresentedRows))
        {
            lines.Add(string.Empty);
            string name = ReadAny(item, "PackageName", "Name", "PackageIdentifier", "Id");
            string id = ReadAny(item, "PackageIdentifier", "Id");
            lines.Add(name == id ? id : $"{name}\n{id}");
            lines.Add($"{ReadAny(item, "InstalledVersion")} → {ReadAny(item, "AvailableVersion", "Version")}".Trim());
            string source = ReadAny(item, "Source", "SourceName");
            if (source.Length > 0) lines.Add($"Source: {source}");
        }
        if (packages.Length > MaximumPresentedRows) lines.Add($"\nShowing the first {MaximumPresentedRows} rows. The complete WinGet response is in Technical result.");
        return string.Join("\n", lines);
    }

    private static IEnumerable<JsonElement> Descendants(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            yield return element;
            foreach (JsonProperty property in element.EnumerateObject())
                foreach (JsonElement child in Descendants(property.Value)) yield return child;
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (JsonElement item in element.EnumerateArray())
                foreach (JsonElement child in Descendants(item)) yield return child;
    }

    private static void AppendRows(List<string> lines, JsonElement data, string property, Func<JsonElement, string> formatter, string heading)
    {
        if (!data.TryGetProperty(property, out JsonElement rows) || rows.ValueKind != JsonValueKind.Array || rows.GetArrayLength() == 0) return;
        lines.Add(string.Empty);
        lines.Add(heading);
        lines.AddRange(rows.EnumerateArray().Take(MaximumPresentedRows).Select(formatter));
    }

    private static void AppendLimitNotice(List<string> lines, JsonElement data, string property)
    {
        if (data.TryGetProperty(property, out JsonElement rows) && rows.ValueKind == JsonValueKind.Array && rows.GetArrayLength() > MaximumPresentedRows)
            lines.Add($"Showing the first {MaximumPresentedRows} entries. The complete evidence is in Technical result.");
    }

    private static string ReadAny(JsonElement item, params string[] properties)
    {
        if (item.ValueKind != JsonValueKind.Object) return string.Empty;
        foreach (string property in properties)
        {
            JsonProperty match = item.EnumerateObject().FirstOrDefault(candidate => candidate.Name.Equals(property, StringComparison.OrdinalIgnoreCase));
            if (match.Value.ValueKind == JsonValueKind.String) return match.Value.GetString() ?? string.Empty;
        }
        return string.Empty;
    }

    private static int ReadInt32(JsonElement item, string property) => item.TryGetProperty(property, out JsonElement value) && value.TryGetInt32(out int result) ? result : 0;
    private static ulong ReadUInt64(JsonElement item, string property) => item.TryGetProperty(property, out JsonElement value) && value.TryGetUInt64(out ulong result) ? result : 0;
    private static bool ReadBool(JsonElement item, string property) => item.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.True;
    private static string FormatBytes(ulong bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB", "PB"];
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return $"{value:0.#} {units[unit]}";
    }
}
