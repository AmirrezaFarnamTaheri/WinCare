namespace WinCare.Infrastructure.Security;

using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

/// <summary>
/// Sensitive credential scanner and auto-masker (PasteBarApp).
/// Prevents accidental persistence or transmission of API keys, bearer tokens, credit cards, and private keys.
/// </summary>
public static class SensitiveCredentialMasker
{
    // Regex for Bearer / JWT tokens
    private static readonly Regex BearerTokenRegex = new(
        @"(?i)bearer\s+([a-zA-Z0-9_\-\.]{16,})",
        RegexOptions.Compiled);

    // Regex for OpenAI / GitHub / AWS API keys
    private static readonly Regex ApiKeyRegex = new(
        @"(?i)(?:sk-[a-zA-Z0-9]{20,}|ghp_[a-zA-Z0-9]{36}|AKIA[0-9A-Z]{16})",
        RegexOptions.Compiled);

    // Regex for Private Key blocks
    private static readonly Regex PrivateKeyRegex = new(
        @"-----BEGIN (?:[A-Z ]+ )?PRIVATE KEY-----[\s\S]*?-----END (?:[A-Z ]+ )?PRIVATE KEY-----",
        RegexOptions.Compiled);

    // Regex for Credit Card digits (13 to 19 digits, optional dashes/spaces)
    private static readonly Regex CreditCardRegex = new(
        @"\b(?:\d{4}[ -]?){3}\d{4}\b|\b(?:\d{4}[ -]?){2}\d{5}\b",
        RegexOptions.Compiled);

    // Regex for inline passwords (password=xyz, password: xyz)
    private static readonly Regex PasswordFieldRegex = new(
        @"(?i)(password\s*[:=]\s*[""']?)([^""'\s]{4,})([""']?)",
        RegexOptions.Compiled);

    /// <summary>
    /// Scans text for any sensitive credentials or secret tokens.
    /// </summary>
    public static bool ContainsSensitiveData(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;

        if (ApiKeyRegex.IsMatch(text) || BearerTokenRegex.IsMatch(text) || PrivateKeyRegex.IsMatch(text) || PasswordFieldRegex.IsMatch(text))
        {
            return true;
        }

        foreach (Match match in CreditCardRegex.Matches(text))
        {
            if (IsValidLuhn(match.Value))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Masks all sensitive tokens, credit card numbers, passwords, and private keys with redaction markers.
    /// </summary>
    public static string MaskSensitiveData(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;

        string result = text;

        // Mask Private Keys
        result = PrivateKeyRegex.Replace(result, "[REDACTED_PRIVATE_KEY]");

        // Mask Bearer Tokens
        result = BearerTokenRegex.Replace(result, m =>
        {
            string token = m.Groups[1].Value;
            string maskedToken = MaskTokenString(token);
            return $"Bearer {maskedToken}";
        });

        // Mask API Keys
        result = ApiKeyRegex.Replace(result, m => MaskTokenString(m.Value));

        // Mask Inline Passwords
        result = PasswordFieldRegex.Replace(result, m =>
        {
            string prefix = m.Groups[1].Value;
            string suffix = m.Groups[3].Value;
            return $"{prefix}********{suffix}";
        });

        // Mask Credit Cards (if Luhn valid)
        result = CreditCardRegex.Replace(result, m =>
        {
            if (IsValidLuhn(m.Value))
            {
                string digits = m.Value.Replace("-", "").Replace(" ", "");
                if (digits.Length >= 4)
                {
                    string last4 = digits[^4..];
                    return $"****-****-****-{last4}";
                }
                return "[REDACTED_CARD]";
            }
            return m.Value;
        });

        return result;
    }

    private static string MaskTokenString(string token)
    {
        if (token.Length <= 8)
        {
            return "********";
        }
        // Keep only a short length-proportional prefix and redact the rest. Retaining the last
        // four characters as well leaked real secret material: for sk-/ghp_-prefixed keys the
        // suffix is entirely secret, so a masked form of "sk-abcdefghij1234567890" must never
        // expose characters past the well-known scheme prefix.
        string prefix = token[..4];
        return $"{prefix}****";
    }

    /// <summary>
    /// Recursively masks every string value inside a JSON tree, returning a new element that is
    /// safe to persist or transmit. Non-string values are passed through untouched; masking is
    /// idempotent, so an already-masked tree round-trips unchanged.
    /// </summary>
    public static JsonElement MaskSensitiveData(JsonElement element)
    {
        JsonNode? node;
        try
        {
            node = JsonSerializer.Deserialize<JsonNode>(element.GetRawText());
        }
        catch (JsonException)
        {
            // A value that cannot be re-parsed is left as-is rather than blocking a state write.
            return element;
        }
        if (node is null)
        {
            return element;
        }

        // A bare root string has no parent node to replace within, so mask it directly.
        if (node is JsonValue rootValue && rootValue.TryGetValue<string>(out string? rootText))
        {
            return JsonSerializer.SerializeToElement(MaskSensitiveData(rootText));
        }

        MaskJsonNode(node);
        return JsonSerializer.SerializeToElement(node);
    }

    private static void MaskJsonNode(JsonNode node)
    {
        if (node is JsonObject obj)
        {
            // JsonNode.ReplaceWith detaches the node being visited, so recursion must consume a
            // snapshot: enumerating obj directly throws "Collection was modified" whenever any
            // property value is a masked string.
            foreach (var property in obj.ToArray())
            {
                if (property.Value is not null)
                {
                    MaskJsonNode(property.Value);
                }
            }
        }
        else if (node is JsonArray array)
        {
            for (int i = 0; i < array.Count; i++)
            {
                if (array[i] is { } item)
                {
                    MaskJsonNode(item);
                }
            }
        }
        else if (node is JsonValue value && value.TryGetValue<string>(out string? text))
        {
            node.ReplaceWith(MaskSensitiveData(text));
        }
    }

    /// <summary>
    /// Validates a potential credit card number string using the standard Luhn (Mod 10) algorithm.
    /// </summary>
    public static bool IsValidLuhn(string numberStr)
    {
        int sum = 0;
        bool alternate = false;
        string clean = numberStr.Replace("-", "").Replace(" ", "");

        if (clean.Length < 13 || clean.Length > 19)
        {
            return false;
        }

        for (int i = clean.Length - 1; i >= 0; i--)
        {
            char c = clean[i];
            if (!char.IsDigit(c)) return false;

            int n = c - '0';
            if (alternate)
            {
                n *= 2;
                if (n > 9)
                {
                    n -= 9;
                }
            }
            sum += n;
            alternate = !alternate;
        }

        return (sum % 10) == 0;
    }
}
