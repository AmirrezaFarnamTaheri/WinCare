using System.Security;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;
using WinCare.Domain.Commands;

namespace WinCare.Application.Commands.Handlers;

/// <summary>
/// Implements the "experience-privacy-profiles" command — reads Windows telemetry and privacy registry keys.
/// </summary>
public sealed class PrivacyStatusCommandHandler : ICommandHandler
{
    /// <inheritdoc />
    public string CommandId => "experience-privacy-profiles";

    /// <inheritdoc />
    public async Task<CommandHandlerOutcome> ExecuteAsync(
        CommandRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        PrivacyRecord record;
        try
        {
            record = await Task.Run(ReadPrivacySettings, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            return CommandHandlerOutcome.Failed(
                "privacy.read_failed",
                "Windows privacy settings could not be read reliably.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        string json = JsonSerializer.Serialize(record, PrivacyStatusJsonContext.Default.PrivacyRecord);
        using JsonDocument doc = JsonDocument.Parse(json);

        return CommandHandlerOutcome.Succeeded(
            "privacy.ok",
            $"Telemetry level: {record.TelemetryLevel}, Advertising ID: {(record.AdvertisingIdEnabled ? "enabled" : "disabled")}.",
            doc.RootElement.Clone(),
            undoAvailable: false);
    }

    private static PrivacyRecord ReadPrivacySettings()
    {
        int telemetry = 1;
        bool adId = false;

        using (RegistryKey? key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows\DataCollection"))
        {
            if (key?.GetValue("AllowTelemetry") is int value)
            {
                telemetry = value;
            }
        }

        using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\AdvertisingInfo"))
        {
            if (key?.GetValue("Enabled") is int value)
            {
                adId = value != 0;
            }
        }

        return new PrivacyRecord(telemetry, adId);
    }

    /// <summary>
    /// Represents privacy settings record.
    /// </summary>
    public sealed record PrivacyRecord(int TelemetryLevel, bool AdvertisingIdEnabled);
}

[JsonSerializable(typeof(PrivacyStatusCommandHandler.PrivacyRecord))]
internal sealed partial class PrivacyStatusJsonContext : JsonSerializerContext
{
}
