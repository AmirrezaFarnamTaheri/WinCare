using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace WinCare.App.ViewModels.Pages;

/// <summary>
/// Redacts sensitive fields in displayed command results. This does not change execution
/// requests, journal receipts, or files; it protects the UI's details pane.
/// </summary>
public static class SafeResultPresentation
{
    private static readonly Regex LinkPattern = new(
        @"https?://[^\s""'<>]+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(150));

    private static readonly HashSet<string> SecretKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "url", "uri", "sourceurl", "downloadurl", "signedurl", "querystring",
        "token", "accesstoken", "refreshtoken", "authorization", "auth",
        "password", "passwd", "secret", "clientsecret", "apikey",
        "cookie", "setcookie", "headers", "credentials", "signature"
    };

    public static string SanitizeText(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        try
        {
            return LinkPattern.Replace(value, match =>
            {
                string link = match.Value.TrimEnd('.', ',', ';', ')', ']');
                if (!Uri.TryCreate(link, UriKind.Absolute, out Uri? uri) ||
                    uri.Scheme is not ("http" or "https")) return "[link hidden]";
                return $"{uri.Scheme}://{uri.IdnHost}/[link hidden]";
            });
        }
        catch (RegexMatchTimeoutException) { return "[link hidden]"; }
    }

    public static JsonNode? SanitizeData(JsonElement? value) =>
        value is JsonElement element ? Scrub(element, null) : null;

    private static JsonNode? Scrub(JsonElement element, string? field)
    {
        if (IsSecret(field)) return JsonValue.Create("[redacted]");
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var obj = new JsonObject();
                foreach (JsonProperty property in element.EnumerateObject())
                    obj[property.Name] = Scrub(property.Value, property.Name);
                return obj;
            case JsonValueKind.Array:
                var items = new JsonArray();
                foreach (JsonElement child in element.EnumerateArray())
                    items.Add(Scrub(child, null));
                return items;
            case JsonValueKind.String:
                return JsonValue.Create(SanitizeText(element.GetString()));
            case JsonValueKind.Number:
                return JsonNode.Parse(element.GetRawText());
            case JsonValueKind.True:
            case JsonValueKind.False:
                return JsonValue.Create(element.GetBoolean());
            default:
                return null;
        }
    }

    private static bool IsSecret(string? field)
    {
        if (string.IsNullOrEmpty(field)) return false;
        return SecretKeys.Contains(new string(field.Where(char.IsLetterOrDigit).ToArray()));
    }
}
