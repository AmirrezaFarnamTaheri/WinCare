namespace WinCare.Infrastructure.Security;

using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using WinCare.Infrastructure.Native;

/// <summary>
/// Cryptographic password policy engine and entropy estimator (pwsafe).
/// Supports EasyVision ambiguous character elimination, minimum symbol/digit rules, and Shannon entropy analysis.
/// </summary>
public static class PasswordPolicyGenerator
{
    private const string LowercaseStandard = "abcdefghijklmnopqrstuvwxyz";
    private const string UppercaseStandard = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string DigitsStandard = "0123456789";
    private const string SymbolsStandard = "!@#$%^&*()_+-=[]{}|;:,.<>?";

    // Ambiguous characters excluded in EasyVision mode: 1, l, I, 0, O, o, 5, S, 2, Z
    private const string LowercaseEasyVision = "abcdefghijkmnpqrstuvwxyz";
    private const string UppercaseEasyVision = "ABCDEFGHJKLMNPQRTUVWXY";
    private const string DigitsEasyVision = "346789";
    private const string SymbolsEasyVision = "!@#$%^&*+-=";

    private const string HexDigits = "0123456789abcdef";

    public enum PasswordStrengthTier
    {
        VeryWeak,
        Weak,
        Moderate,
        Strong,
        VeryStrong
    }

    public sealed record PasswordPolicyOptions(
        int Length = 16,
        bool UseLowercase = true,
        int MinLowercase = 1,
        bool UseUppercase = true,
        int MinUppercase = 1,
        bool UseDigits = true,
        int MinDigits = 1,
        bool UseSymbols = true,
        int MinSymbols = 1,
        bool UseEasyVision = false,
        bool HexadecimalOnly = false);

    public sealed record GeneratedPasswordResult(
        string Password,
        double ShannonEntropyBits,
        PasswordStrengthTier StrengthTier);

    /// <summary>
    /// Generates a cryptographically secure random password complying strictly with the supplied policy options.
    /// </summary>
    public static GeneratedPasswordResult GeneratePassword(PasswordPolicyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        int targetLength = Math.Clamp(options.Length, 8, 256);

        if (options.HexadecimalOnly)
        {
            var hexBytes = new byte[targetLength];
            RandomNumberGenerator.Fill(hexBytes);
            var sbHex = new StringBuilder(targetLength);
            for (int i = 0; i < targetLength; i++)
            {
                sbHex.Append(HexDigits[hexBytes[i] % HexDigits.Length]);
            }
            string hexPw = sbHex.ToString();
            double hexEntropy = CalculateShannonEntropy(hexPw);
            return new GeneratedPasswordResult(hexPw, hexEntropy, ClassifyStrength(hexEntropy));
        }

        string lowerPool = options.UseEasyVision ? LowercaseEasyVision : LowercaseStandard;
        string upperPool = options.UseEasyVision ? UppercaseEasyVision : UppercaseStandard;
        string digitsPool = options.UseEasyVision ? DigitsEasyVision : DigitsStandard;
        string symbolsPool = options.UseEasyVision ? SymbolsEasyVision : SymbolsStandard;

        var charList = new List<char>();

        // Ensure minimums
        if (options.UseLowercase && options.MinLowercase > 0)
        {
            for (int i = 0; i < options.MinLowercase; i++)
            {
                charList.Add(GetRandomChar(lowerPool));
            }
        }

        if (options.UseUppercase && options.MinUppercase > 0)
        {
            for (int i = 0; i < options.MinUppercase; i++)
            {
                charList.Add(GetRandomChar(upperPool));
            }
        }

        if (options.UseDigits && options.MinDigits > 0)
        {
            for (int i = 0; i < options.MinDigits; i++)
            {
                charList.Add(GetRandomChar(digitsPool));
            }
        }

        if (options.UseSymbols && options.MinSymbols > 0)
        {
            for (int i = 0; i < options.MinSymbols; i++)
            {
                charList.Add(GetRandomChar(symbolsPool));
            }
        }

        // Build combined pool for remaining length
        var combinedPool = new StringBuilder();
        if (options.UseLowercase) combinedPool.Append(lowerPool);
        if (options.UseUppercase) combinedPool.Append(upperPool);
        if (options.UseDigits) combinedPool.Append(digitsPool);
        if (options.UseSymbols) combinedPool.Append(symbolsPool);

        if (combinedPool.Length == 0)
        {
            combinedPool.Append(lowerPool);
        }

        string fullPool = combinedPool.ToString();
        while (charList.Count < targetLength)
        {
            charList.Add(GetRandomChar(fullPool));
        }

        // Trim if minimums exceeded requested length
        if (charList.Count > targetLength)
        {
            charList = charList.GetRange(0, targetLength);
        }

        // Fisher-Yates CSPRNG shuffle
        Shuffle(charList);

        string password = new(charList.ToArray());
        double entropy = CalculateShannonEntropy(password);

        return new GeneratedPasswordResult(password, entropy, ClassifyStrength(entropy));
    }

    /// <summary>
    /// Calculates the Shannon entropy in bits for the supplied password string.
    /// Tries the native C ABI export wincare_core_calculate_entropy first; falls back to pure C#.
    /// </summary>
    public static unsafe double CalculateShannonEntropy(string password)
    {
        if (string.IsNullOrEmpty(password)) return 0.0;

        byte[] utf8 = Encoding.UTF8.GetBytes(password);
        double nativeEntropy = 0.0;

        try
        {
            fixed (byte* pData = utf8)
            {
                int status = WinCareCoreNative.WinCareCoreCalculateEntropy(pData, (nuint)utf8.Length, &nativeEntropy);
                if (status == 0)
                {
                    // Convert bits per byte to total entropy bits = (bits/byte) * length
                    return nativeEntropy * utf8.Length;
                }
            }
        }
        catch
        {
            // Fallback to managed calculation
        }

        return CalculateManagedEntropy(utf8);
    }

    private static double CalculateManagedEntropy(byte[] data)
    {
        var counts = new int[256];
        foreach (byte b in data)
        {
            counts[b]++;
        }

        double n = data.Length;
        double entropyPerByte = 0.0;
        foreach (int count in counts)
        {
            if (count > 0)
            {
                double p = count / n;
                entropyPerByte -= p * Math.Log2(p);
            }
        }

        return entropyPerByte * data.Length;
    }

    private static PasswordStrengthTier ClassifyStrength(double totalEntropyBits)
    {
        return totalEntropyBits switch
        {
            < 28.0 => PasswordStrengthTier.VeryWeak,
            < 40.0 => PasswordStrengthTier.Weak,
            < 60.0 => PasswordStrengthTier.Moderate,
            < 80.0 => PasswordStrengthTier.Strong,
            _ => PasswordStrengthTier.VeryStrong
        };
    }

    private static char GetRandomChar(string pool)
    {
        int index = RandomNumberGenerator.GetInt32(0, pool.Length);
        return pool[index];
    }

    private static void Shuffle<T>(IList<T> list)
    {
        int n = list.Count;
        while (n > 1)
        {
            n--;
            int k = RandomNumberGenerator.GetInt32(0, n + 1);
            (list[k], list[n]) = (list[n], list[k]);
        }
    }
}
