namespace WinCare.Infrastructure.Security;

using System;
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
        string prefix = token[..4];
        string suffix = token[^4..];
        return $"{prefix}****...{suffix}";
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
