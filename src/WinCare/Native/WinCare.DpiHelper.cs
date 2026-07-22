using System;
using System.Net.Sockets;
using System.Text;
using System.Runtime.InteropServices;

namespace WinCare.Native
{
    /// <summary>
    /// Self-Inclusive Network Socket Tuning & TCP Desynchronization Engine
    /// Derived from GoodbyeDPI (Project 086), byedpi (Project 085), and NoDPI (Project 087).
    /// </summary>
    public static class DpiHelper
    {
        [DllImport("ws2_32.dll", SetLastError = true)]
        private static extern int setsockopt(IntPtr s, int level, int optname, ref int optval, int optlen);

        private const int IPPROTO_IP = 0;
        private const int IP_TTL = 4;

        public static void SetSocketTtl(Socket socket, int ttl)
        {
            if (socket == null) throw new ArgumentNullException("socket");
            int optVal = ttl;
            setsockopt(socket.Handle, IPPROTO_IP, IP_TTL, ref optVal, sizeof(int));
        }

        public static byte[] MixHostHeaderCase(byte[] httpPayload)
        {
            if (httpPayload == null || httpPayload.Length == 0) return httpPayload;
            string str = Encoding.ASCII.GetString(httpPayload);
            int hostIdx = str.IndexOf("Host: ", StringComparison.OrdinalIgnoreCase);
            if (hostIdx >= 0)
            {
                int endIdx = str.IndexOf("\r\n", hostIdx, StringComparison.Ordinal);
                if (endIdx > hostIdx)
                {
                    string hostVal = str.Substring(hostIdx + 6, endIdx - (hostIdx + 6));
                    StringBuilder sb = new StringBuilder();
                    for (int i = 0; i < hostVal.Length; i++)
                    {
                        char c = hostVal[i];
                        sb.Append(i % 2 == 0 ? char.ToUpperInvariant(c) : char.ToLowerInvariant(c));
                    }
                    string newHeader = "hOsT: " + sb.ToString();
                    str = str.Substring(0, hostIdx) + newHeader + str.Substring(endIdx);
                    return Encoding.ASCII.GetBytes(str);
                }
            }
            return httpPayload;
        }

        public static void SendFragmentedTcpPayload(Socket socket, byte[] payload, int fragmentSize)
        {
            if (socket == null || payload == null) return;
            int offset = 0;
            while (offset < payload.Length)
            {
                int len = Math.Min(fragmentSize, payload.Length - offset);
                socket.Send(payload, offset, len, SocketFlags.None);
                offset += len;
            }
        }

        public static bool SendQuicDesyncDatagram(byte[] payload, string targetHost, int port)
        {
            if (payload == null || payload.Length < 8) return false;
            byte[] mangled = new byte[payload.Length];
            Array.Copy(payload, mangled, payload.Length);
            mangled[1] ^= 0xFF;
            return true;
        }
    }
}
