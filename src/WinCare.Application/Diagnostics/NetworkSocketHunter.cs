using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;

namespace WinCare.Application.Diagnostics;

public record NetworkSocketRecord(
    int ProcessId,
    string ProcessName,
    string Protocol,
    string LocalEndpoint,
    string RemoteEndpoint,
    string State,
    bool IsSystemOrElevated
);

public static class NetworkSocketHunter
{
    private const int AfInet = 2;
    private const int TcpTableOwnerPidAll = 5;

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(
        IntPtr pTcpTable,
        ref int pdwOutBufLen,
        [MarshalAs(UnmanagedType.Bool)] bool sort,
        int ipVersion,
        int tblClass,
        uint reserved
    );

    public static IReadOnlyList<NetworkSocketRecord> ProbeActiveSockets()
    {
        var records = new List<NetworkSocketRecord>();
        int bufferSize = 0;

        _ = GetExtendedTcpTable(IntPtr.Zero, ref bufferSize, true, AfInet, TcpTableOwnerPidAll, 0);
        if (bufferSize <= 0) return records;

        IntPtr tablePtr = Marshal.AllocHGlobal(bufferSize);
        try
        {
            if (GetExtendedTcpTable(tablePtr, ref bufferSize, true, AfInet, TcpTableOwnerPidAll, 0) == 0)
            {
                int rowCount = Marshal.ReadInt32(tablePtr);
                IntPtr rowPtr = tablePtr + 4;
                int structSize = Marshal.SizeOf<MIB_TCPROW_OWNER_PID>();

                for (int i = 0; i < rowCount; i++)
                {
                    var row = Marshal.PtrToStructure<MIB_TCPROW_OWNER_PID>(rowPtr);
                    string procName = ResolveProcessName(row.owningPid);

                    records.Add(new NetworkSocketRecord(
                        row.owningPid,
                        procName,
                        "TCP",
                        $"{new IPAddress(row.localAddr)}:{row.LocalPort}",
                        $"{new IPAddress(row.remoteAddr)}:{row.RemotePort}",
                        ResolveTcpState(row.state),
                        row.owningPid <= 4
                    ));
                    rowPtr += structSize;
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(tablePtr);
        }

        return records;
    }

    private static string ResolveProcessName(int pid)
    {
        if (pid <= 0) return "System Idle";
        if (pid == 4) return "System";
        try
        {
            using var proc = Process.GetProcessById(pid);
            return proc.ProcessName;
        }
        catch
        {
            return "Unknown/Terminated";
        }
    }

    private static string ResolveTcpState(uint state) => state switch
    {
        1 => "CLOSED",
        2 => "LISTEN",
        3 => "SYN_SENT",
        4 => "SYN_RCVD",
        5 => "ESTABLISHED",
        6 => "FIN_WAIT1",
        7 => "FIN_WAIT2",
        8 => "CLOSE_WAIT",
        9 => "CLOSING",
        10 => "LAST_ACK",
        11 => "TIME_WAIT",
        12 => "DELETE_TCB",
        _ => "UNKNOWN"
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct MIB_TCPROW_OWNER_PID
    {
        public uint state;
        public uint localAddr;
        public byte localPort1;
        public byte localPort2;
        public byte localPort3;
        public byte localPort4;
        public uint remoteAddr;
        public byte remotePort1;
        public byte remotePort2;
        public byte remotePort3;
        public byte remotePort4;
        public int owningPid;

        public ushort LocalPort => (ushort)((localPort1 << 8) | localPort2);
        public ushort RemotePort => (ushort)((remotePort1 << 8) | remotePort2);
    }
}
