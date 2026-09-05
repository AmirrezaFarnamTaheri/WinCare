using System.Text.Json;
using System.Text.Json.Nodes;
using WinCare.Domain.Commands;

namespace WinCare.Application.Commands;

/// <summary>
/// Normalizes narrowly-scoped legacy parameter aliases at the stable command-handler boundary.
/// New UI contracts expose canonical fields while older automation payloads remain deterministic.
/// </summary>
internal static class CommandRequestCompatibility
{
    public static bool TryNormalize(CommandRequest request, out CommandRequest normalized, out string? error)
    {
        normalized = request;
        error = null;
        if (!string.Equals(request.CommandId, "offline-feature-set", StringComparison.OrdinalIgnoreCase))
            return true;

        JsonObject parameters;
        try
        {
            parameters = JsonNode.Parse(request.Parameters.GetRawText())?.AsObject() ?? new JsonObject();
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            error = "Offline feature parameters must be a JSON object.";
            return false;
        }

        bool hasEnabled = parameters.TryGetPropertyValue("Enabled", out JsonNode? enabledNode) && enabledNode is not null;
        bool hasState = parameters.TryGetPropertyValue("State", out JsonNode? stateNode) && stateNode is not null;

        bool enabled = true;
        if (hasEnabled)
        {
            try
            {
                enabled = enabledNode!.GetValue<bool>();
            }
            catch (InvalidOperationException)
            {
                error = "Enabled must be true or false.";
                return false;
            }
        }

        if (hasState)
        {
            string state;
            try
            {
                state = stateNode!.GetValue<string>();
            }
            catch (InvalidOperationException)
            {
                error = "State must be 'Enable' or 'Disable'.";
                return false;
            }

            bool stateEnabled;
            if (state.Equals("Enable", StringComparison.OrdinalIgnoreCase)) stateEnabled = true;
            else if (state.Equals("Disable", StringComparison.OrdinalIgnoreCase)) stateEnabled = false;
            else
            {
                error = "State must be 'Enable' or 'Disable'.";
                return false;
            }

            if (hasEnabled && stateEnabled != enabled)
            {
                error = "Enabled and legacy State parameters conflict. Provide one intent only.";
                return false;
            }
            enabled = stateEnabled;
        }

        // WindowsCommandExecutor historically validates State but executes Enabled. Keep both
        // synchronized at the command boundary so old and new callers cannot express two intents.
        parameters["Enabled"] = enabled;
        parameters["State"] = enabled ? "Enable" : "Disable";
        using JsonDocument document = JsonDocument.Parse(parameters.ToJsonString());
        normalized = request with { Parameters = document.RootElement.Clone() };
        return true;
    }
}
