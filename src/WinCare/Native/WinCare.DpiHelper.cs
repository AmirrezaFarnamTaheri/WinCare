using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace WinCare.Native
{
    /// <summary>
    /// Bounded network-diagnostic helpers. These methods do not implement DPI bypass,
    /// protocol desynchronization, or censorship circumvention.
    /// </summary>
    public static class DpiHelper
    {
        private const int MaximumHeaderPayloadBytes = 1024 * 1024;
        private const int MaximumTcpPayloadBytes = 16 * 1024 * 1024;
        private const int MaximumTcpFragments = 4096;
        private static readonly TimeSpan DiagnosticTimeout = TimeSpan.FromSeconds(10);

        public static void SetSocketTtl(Socket socket, int ttl)
        {
            ArgumentNullException.ThrowIfNull(socket);
            if (ttl is < 1 or > 255)
                throw new ArgumentOutOfRangeException(nameof(ttl), ttl, "IPv4 TTL must be between 1 and 255.");
            if (socket.AddressFamily != AddressFamily.InterNetwork)
                throw new NotSupportedException("IPv4 TTL can only be applied to an IPv4 socket.");
            socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.IpTimeToLive, ttl);
        }

        public static byte[] MixHostHeaderCase(byte[] httpPayload)
        {
            ArgumentNullException.ThrowIfNull(httpPayload);
            if (httpPayload.Length == 0) return Array.Empty<byte>();
            if (httpPayload.Length > MaximumHeaderPayloadBytes)
                throw new ArgumentOutOfRangeException(nameof(httpPayload), $"HTTP diagnostic payload exceeds {MaximumHeaderPayloadBytes} bytes.");

            string value = Encoding.ASCII.GetString(httpPayload);
            int hostIndex = value.IndexOf("Host: ", StringComparison.OrdinalIgnoreCase);
            if (hostIndex < 0) return (byte[])httpPayload.Clone();
            int endIndex = value.IndexOf("\r\n", hostIndex, StringComparison.Ordinal);
            if (endIndex <= hostIndex) return (byte[])httpPayload.Clone();

            int valueStart = hostIndex + 6;
            string hostValue = value.Substring(valueStart, endIndex - valueStart);
            var mixed = new StringBuilder(hostValue.Length);
            for (int index = 0; index < hostValue.Length; index++)
            {
                char character = hostValue[index];
                mixed.Append(index % 2 == 0 ? char.ToUpperInvariant(character) : char.ToLowerInvariant(character));
            }
            string rewritten = value.Substring(0, hostIndex) + "hOsT: " + mixed + value.Substring(endIndex);
            return Encoding.ASCII.GetBytes(rewritten);
        }

        public static void SendFragmentedTcpPayload(Socket socket, byte[] payload, int fragmentSize)
        {
            ArgumentNullException.ThrowIfNull(socket);
            ArgumentNullException.ThrowIfNull(payload);
            if (payload.Length > MaximumTcpPayloadBytes)
                throw new ArgumentOutOfRangeException(nameof(payload), $"TCP diagnostic payload exceeds {MaximumTcpPayloadBytes} bytes.");
            if (fragmentSize <= 0 || fragmentSize > MaximumTcpPayloadBytes)
                throw new ArgumentOutOfRangeException(nameof(fragmentSize), fragmentSize, "Fragment size is outside the permitted range.");
            if (!socket.Connected)
                throw new InvalidOperationException("The TCP socket must be connected before sending a payload.");
            if (payload.Length == 0) return;
            int fragments = checked((payload.Length + fragmentSize - 1) / fragmentSize);
            if (fragments > MaximumTcpFragments)
                throw new ArgumentOutOfRangeException(nameof(fragmentSize), $"The request would exceed {MaximumTcpFragments} fragments.");

            using var cancellation = new CancellationTokenSource(DiagnosticTimeout);
            int offset = 0;
            while (offset < payload.Length)
            {
                int requested = Math.Min(fragmentSize, payload.Length - offset);
                int sent;
                try
                {
                    sent = socket.SendAsync(payload.AsMemory(offset, requested), SocketFlags.None, cancellation.Token)
                        .AsTask().GetAwaiter().GetResult();
                }
                catch (OperationCanceledException error)
                {
                    throw new TimeoutException("The TCP diagnostic send exceeded its total deadline.", error);
                }
                if (sent <= 0)
                    throw new IOException("The connected socket closed before the complete payload was sent.");
                offset += sent;
            }
        }

        [Obsolete("Use SendUdpDiagnosticDatagram. This method performs no DPI bypass or desynchronization.")]
        public static bool SendQuicDesyncDatagram(byte[] payload, string targetHost, int port) =>
            SendUdpDiagnosticDatagram(payload, targetHost, port) == payload.Length;

        public static int SendUdpDiagnosticDatagram(byte[] payload, string targetHost, int port)
        {
            ArgumentNullException.ThrowIfNull(payload);
            if (payload.Length is < 1 or > 65507)
                throw new ArgumentOutOfRangeException(nameof(payload), "UDP payload must contain 1 to 65,507 bytes.");
            if (string.IsNullOrWhiteSpace(targetHost) || targetHost.Length > 253 || targetHost.IndexOf('\0') >= 0)
                throw new ArgumentException("A valid bounded target host is required.", nameof(targetHost));
            if (port is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));

            IPAddress[] addresses;
            try
            {
                addresses = Dns.GetHostAddressesAsync(targetHost).WaitAsync(DiagnosticTimeout).GetAwaiter().GetResult();
            }
            catch (TimeoutException)
            {
                throw new TimeoutException("UDP diagnostic target resolution exceeded its deadline.");
            }
            if (addresses.Length == 0) throw new SocketException((int)SocketError.HostNotFound);
            var endpoint = new IPEndPoint(addresses[0], port);
            using var client = new UdpClient(endpoint.AddressFamily);
            using var cancellation = new CancellationTokenSource(DiagnosticTimeout);
            try
            {
                return client.SendAsync(payload, endpoint, cancellation.Token).AsTask().GetAwaiter().GetResult();
            }
            catch (OperationCanceledException error)
            {
                throw new TimeoutException("The UDP diagnostic send exceeded its deadline.", error);
            }
        }
    }
}
