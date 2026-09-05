using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinCare.Domain.Sync;
using WinCare.Infrastructure.Security;

namespace WinCare.Infrastructure.Sync
{
    public interface ICloudProfileSyncProvider
    {
        Task<string> UploadProfileAsync(CloudProfilePayload payload, string passphrase, string accessToken, CancellationToken cancellationToken = default);
        Task<CloudProfilePayload> DownloadProfileAsync(string gistId, string passphrase, string accessToken, CancellationToken cancellationToken = default);
    }

    public sealed class GitHubGistSyncProvider : ICloudProfileSyncProvider
    {
        private const int MaxResponseBytes = 2 * 1024 * 1024;
        private readonly HttpClient _httpClient;
        private readonly ICryptoService _cryptoService;

        public GitHubGistSyncProvider(HttpClient? httpClient = null, ICryptoService? cryptoService = null)
        {
            _httpClient = httpClient ?? new HttpClient();
            _cryptoService = cryptoService ?? new CryptoService();
        }

        public async Task<string> UploadProfileAsync(CloudProfilePayload payload, string passphrase, string accessToken, CancellationToken cancellationToken = default)
        {
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            if (string.IsNullOrWhiteSpace(passphrase)) throw new ArgumentException("Passphrase required", nameof(passphrase));
            if (string.IsNullOrWhiteSpace(accessToken)) throw new ArgumentException("Access token required", nameof(accessToken));

            var plainJson = JsonSerializer.Serialize(payload);
            var encryptedBase64 = _cryptoService.Encrypt(plainJson, passphrase);

            var gistRequest = new
            {
                description = "WinCare Encrypted Profile Sync",
                @public = false,
                files = new System.Collections.Generic.Dictionary<string, object>
                {
                    ["wincare-profile.enc"] = new { content = encryptedBase64 }
                }
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.github.com/gists")
            {
                Content = new StringContent(JsonSerializer.Serialize(gistRequest), Encoding.UTF8, "application/json")
            };
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("WinCare-Sync", "2.4.0"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            var responseJson = await ReadBoundedResponseAsync(response, cancellationToken);
            using var doc = JsonDocument.Parse(responseJson);
            return doc.RootElement.GetProperty("id").GetString() ?? string.Empty;
        }

        public async Task<CloudProfilePayload> DownloadProfileAsync(string gistId, string passphrase, string accessToken, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(gistId)) throw new ArgumentException("Gist ID required", nameof(gistId));
            if (gistId.Length > 64 || !gistId.All(Uri.IsHexDigit))
                throw new ArgumentException("Gist ID must contain only hexadecimal characters.", nameof(gistId));
            if (string.IsNullOrWhiteSpace(passphrase)) throw new ArgumentException("Passphrase required", nameof(passphrase));

            using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/gists/{gistId}");
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("WinCare-Sync", "2.4.0"));
            if (!string.IsNullOrWhiteSpace(accessToken))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            }

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            var responseJson = await ReadBoundedResponseAsync(response, cancellationToken);
            using var doc = JsonDocument.Parse(responseJson);
            var file = doc.RootElement
                .GetProperty("files")
                .GetProperty("wincare-profile.enc");
            if (file.TryGetProperty("truncated", out var truncated) && truncated.ValueKind == JsonValueKind.True)
                throw new InvalidOperationException("The remote profile is truncated and cannot be restored safely.");
            var content = file.GetProperty("content")
                .GetString() ?? string.Empty;

            var decryptedJson = _cryptoService.Decrypt(content, passphrase);
            return JsonSerializer.Deserialize<CloudProfilePayload>(decryptedJson)
                   ?? throw new InvalidOperationException("Failed to deserialize decrypted profile payload.");
        }

        private static async Task<string> ReadBoundedResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
        {
            if (response.Content.Headers.ContentLength > MaxResponseBytes)
                throw new InvalidOperationException("Profile response exceeds the size limit.");
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var buffer = new MemoryStream();
            byte[] chunk = new byte[8192];
            int read;
            while ((read = await source.ReadAsync(chunk, cancellationToken)) != 0)
            {
                if (buffer.Length + read > MaxResponseBytes)
                    throw new InvalidOperationException("Profile response exceeds the size limit.");
                buffer.Write(chunk, 0, read);
            }
            return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
        }
    }
}
