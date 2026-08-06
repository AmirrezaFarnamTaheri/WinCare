using System.Reflection;
using System.Text.Json;

namespace WinCare.CommandCatalog;

internal static class EmbeddedJsonResource
{
    public static JsonDocument Read(Assembly assembly, string resourceName, int maximumBytes)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceName);
        if (maximumBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        }

        using Stream source = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' is missing.");
        using MemoryStream bounded = new();
        byte[] buffer = new byte[64 * 1024];
        int total = 0;
        while (true)
        {
            int read = source.Read(buffer, 0, buffer.Length);
            if (read == 0)
            {
                break;
            }

            total = checked(total + read);
            if (total > maximumBytes)
            {
                throw new InvalidOperationException(
                    $"Embedded resource '{resourceName}' exceeds the {maximumBytes}-byte limit.");
            }
            bounded.Write(buffer, 0, read);
        }

        bounded.Position = 0;
        return JsonDocument.Parse(
            bounded,
            new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 64,
            });
    }
}
