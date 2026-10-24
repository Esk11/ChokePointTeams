using System.Runtime.InteropServices;

/// <summary>WinDivert's address record (80 bytes). Only the fields we need are exposed.</summary>
[StructLayout(LayoutKind.Sequential, Size = 80)]
struct WinDivertAddress
{
    public long Timestamp;
    public uint Bits;          // Layer:8, Event:8, Sniffed, Outbound, Loopback, Impostor, IPv6, ...
    public uint Reserved2;
    public ulong U0, U1, U2, U3, U4, U5, U6, U7;   // the 64-byte union (flow / socket / network data)

    public readonly uint Event => (Bits >> 8) & 0xFF;
    public readonly bool Outbound => (Bits & (1u << 17)) != 0;

    // Flow layer fields: ProcessId @16, LocalPort @52, Protocol @56 (offsets inside the union).
    public readonly uint FlowPid => (uint)U2;
    public readonly ushort FlowLocalPort => (ushort)(U6 >> 32);
    public readonly byte FlowProtocol => (byte)(U7 & 0xFF);
}

static class WinDivertNative
{
    const string Dll = "WinDivert.dll";

    [DllImport(Dll, SetLastError = true, CharSet = CharSet.Ansi)]
    public static extern IntPtr WinDivertOpen(string filter, int layer, short priority, ulong flags);

    [DllImport(Dll, SetLastError = true)]
    public static extern bool WinDivertRecv(IntPtr handle, byte[]? packet, uint packetLen, out uint recvLen, ref WinDivertAddress addr);

    [DllImport(Dll, SetLastError = true)]
    public static extern bool WinDivertSend(IntPtr handle, byte[] packet, uint packetLen, out uint sendLen, ref WinDivertAddress addr);

    [DllImport(Dll, SetLastError = true)]
    public static extern bool WinDivertShutdown(IntPtr handle, int how);

    [DllImport(Dll, SetLastError = true)]
    public static extern bool WinDivertClose(IntPtr handle);

    [DllImport(Dll, SetLastError = true)]
    public static extern bool WinDivertSetParam(IntPtr handle, int param, ulong value);

    // IP Helper: which process owns which local port (used to seed connections that already exist).
    [DllImport("iphlpapi.dll", SetLastError = true)]
    public static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool sort, int af, int tableClass, uint reserved);

    [DllImport("iphlpapi.dll", SetLastError = true)]
    public static extern uint GetExtendedUdpTable(IntPtr table, ref int size, bool sort, int af, int tableClass, uint reserved);

    // Finer Windows timer so packets are released on time (1 ms instead of ~15 ms).
    [DllImport("winmm.dll")] public static extern uint timeBeginPeriod(uint ms);
    [DllImport("winmm.dll")] public static extern uint timeEndPeriod(uint ms);
}
