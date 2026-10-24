using System.Buffers;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

// ---- status sent back to the page ----
record EngineStatus(string State, string? Error, long Packets, long Delayed, long Dropped, long Unmapped, long Teams = 0, int Level = 0);

/// <summary>One disturbance level (upload only). RateKBs: -1 = no cap, 0 = blocked. Blackouts drop everything for a while.</summary>
record Profile(string Name, double RateKBs, double LossPct, int JitterMs, double BlackoutSec, double ClearSec);
sealed record ProfileValues
{
    public required double RateKBs { get; init; }
    public required double LossPct { get; init; }
    public required int JitterMs { get; init; }
    public required double BlackoutSec { get; init; }
    public required double ClearSec { get; init; }
}

static class ProfileSettings
{
    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };
    static readonly Profile[] Defaults =
    {
        new("No effect",   -1,   0,   0,  0,    0),
        new("Minor",      300,   1,  80,  0,    0),
        new("Moderate",    80,   6, 250,  0.5,  6),
        new("Significant", 12,  20, 400,  1.5,  4),
        new("Major",        4,  50, 800, 12,    5),
        new("Offline",      0, 100,   0,  0,    0),
    };

    static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ChokePointTeams", "profiles.json");

    public static Profile[] Load(out string? warning)
    {
        warning = null;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            if (!File.Exists(FilePath))
            {
                var initial = Defaults.Select(p =>
                    new ProfileValues
                    {
                        RateKBs = p.RateKBs,
                        LossPct = p.LossPct,
                        JitterMs = p.JitterMs,
                        BlackoutSec = p.BlackoutSec,
                        ClearSec = p.ClearSec
                    });
                File.WriteAllText(FilePath, JsonSerializer.Serialize(initial, Json));
            }

            var values = JsonSerializer.Deserialize<ProfileValues[]>(File.ReadAllText(FilePath), Json);
            if (values is null || values.Length != Defaults.Length)
                throw new InvalidDataException($"Expected exactly {Defaults.Length} profiles, in button order.");

            for (int i = 0; i < values.Length; i++)
            {
                var p = values[i];
                if (p is null ||
                    !double.IsFinite(p.RateKBs) || (p.RateKBs < 0 && p.RateKBs != -1) ||
                    !double.IsFinite(p.LossPct) || p.LossPct is < 0 or > 100 ||
                    p.JitterMs < 0 ||
                    !double.IsFinite(p.BlackoutSec) || p.BlackoutSec is < 0 or > 31_536_000 ||
                    !double.IsFinite(p.ClearSec) || p.ClearSec is < 0 or > 31_536_000)
                    throw new InvalidDataException($"Profile {i + 1} contains an invalid value.");
            }

            return values.Select((p, i) => Defaults[i] with
            {
                RateKBs = p.RateKBs,
                LossPct = p.LossPct,
                JitterMs = p.JitterMs,
                BlackoutSec = p.BlackoutSec,
                ClearSec = p.ClearSec
            }).ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            warning = $"Could not load profile settings from:\n{FilePath}\n\n" +
                      $"The built-in values will be used. Check the file and try again.\n\n{ex.Message}";
            return Defaults;
        }
    }
}

readonly record struct Held(byte[] Data, int Len, WinDivertAddress Addr);

/// <summary>
/// Packet limiter built on WinDivert. It only runs while at least one limit is set; with no limits
/// no packet is touched. Traffic is paced per app, per group and for everything combined.
/// Closing the app (or removing every limit) immediately restores normal traffic.
/// </summary>
sealed class Limiter : IDisposable
{
    enum Verdict { Pass, Delay, Drop }

    static readonly IntPtr Invalid = new(-1);
    static readonly long Freq = Stopwatch.Frequency;
    static readonly long Burst = Freq / 25;              // 40 ms of burst allowance
    static readonly long MaxDelay = Freq * 35 / 100;     // packets that would wait longer than 350 ms are dropped

    readonly Profile[] profiles;
    volatile Profile prof;
    int level;
    // used from the packet thread only:
    Profile? lastProf;
    bool inBlackout;
    long tat, phaseEnd;
    readonly Random rng = new();
    readonly object life = new();

    IntPtr net = Invalid, flow = Invalid;
    Thread? recvThread, flowThread, sendThread;
    volatile bool running;
    bool timerRaised;
    string state = "idle";
    string? error;

    // (protocol << 16 | local port) -> owning process id
    readonly ConcurrentDictionary<int, int> flowMap = new();
    DateTime lastSeed = DateTime.MinValue;
    readonly object seedLock = new();

    // Packets that are being held back until their release time.
    readonly PriorityQueue<Held, (long, long)> queue = new();
    readonly object qlock = new();
    long seq;

    long nPackets, nDelayed, nDropped, nUnmapped, nTeams;

    public EngineStatus Status() => new(state, error, nPackets, nDelayed, nDropped, nUnmapped, nTeams, level);

    // ------------------------------------------------------------------ disturbance levels

    public Limiter(Profile[] profiles)
    {
        this.profiles = profiles;
        prof = profiles[0];
    }

    public void SetLevel(int newLevel)
    {
        lock (life)
        {
            level = Math.Clamp(newLevel, 0, profiles.Length - 1);
            prof = profiles[level];
            if (level > 0 && !running) StartLocked();
            else if (level == 0 && running) StopLocked();
        }
    }

    // ------------------------------------------------------------------ start / stop

    void StartLocked()
    {
        try
        {
            WinDivertNative.timeBeginPeriod(1);
            timerRaised = true;

            // Everything except local loopback and DNS (which we never slow down).
            net = WinDivertNative.WinDivertOpen(
                "outbound && !loopback && (tcp || (udp && udp.DstPort != 53))", 0, 0, 0);
            if (net == Invalid) throw new Win32Exception(Marshal.GetLastWin32Error());
            WinDivertNative.WinDivertSetParam(net, 0, 16384);               // queue length
            WinDivertNative.WinDivertSetParam(net, 1, 4000);                // queue time (ms)
            WinDivertNative.WinDivertSetParam(net, 2, 16UL * 1024 * 1024);  // queue size (bytes)

            // Flow layer: tells us which process owns each connection (listen only).
            flow = WinDivertNative.WinDivertOpen("true", 2, 0, 1 | 4);
            if (flow == Invalid) throw new Win32Exception(Marshal.GetLastWin32Error());

            Seed();
            running = true;
            recvThread = new Thread(RecvLoop) { IsBackground = true, Name = "Divert-net", Priority = ThreadPriority.AboveNormal };
            flowThread = new Thread(FlowLoop) { IsBackground = true, Name = "Divert-flow" };
            sendThread = new Thread(SendLoop) { IsBackground = true, Name = "Divert-send", Priority = ThreadPriority.AboveNormal };
            recvThread.Start(); flowThread.Start(); sendThread.Start();

            state = "active";
            error = null;
        }
        catch (Exception ex)
        {
            running = false;
            CleanupLocked();
            state = "error";
            error = Describe(ex);
        }
    }

    void StopLocked()
    {
        running = false;
        if (net != Invalid) WinDivertNative.WinDivertShutdown(net, 1);
        if (flow != Invalid) WinDivertNative.WinDivertShutdown(flow, 1);
        lock (qlock) Monitor.PulseAll(qlock);
        foreach (var t in new[] { recvThread, flowThread, sendThread }) t?.Join(1500);
        CleanupLocked();
        state = "idle";
        error = null;
    }

    void CleanupLocked()
    {
        if (net != Invalid) { WinDivertNative.WinDivertClose(net); net = Invalid; }
        if (flow != Invalid) { WinDivertNative.WinDivertClose(flow); flow = Invalid; }
        lock (qlock)
        {
            while (queue.TryDequeue(out var h, out _)) ArrayPool<byte>.Shared.Return(h.Data);
        }
        if (timerRaised) { WinDivertNative.timeEndPeriod(1); timerRaised = false; }
        flowMap.Clear();
    }

    // Called from a worker thread when something goes wrong: stop and show the reason.
    void Fail(string msg) => ThreadPool.QueueUserWorkItem(_ =>
    {
        lock (life)
        {
            if (!running) return;
            StopLocked();
            state = "error";
            error = msg;
        }
    });

    static string Describe(Exception ex) => ex switch
    {
        DllNotFoundException or BadImageFormatException =>
            "WinDivert.dll was not found (or is the wrong type) next to the app. Copy the 64-bit WinDivert.dll and WinDivert64.sys into the project folder.",
        Win32Exception w when w.NativeErrorCode == 5 => "Access denied. Run the app as administrator.",
        Win32Exception w when w.NativeErrorCode is 2 or 3 => "WinDivert64.sys was not found next to WinDivert.dll.",
        Win32Exception w when w.NativeErrorCode == 577 => "Windows rejected the WinDivert driver signature.",
        Win32Exception w when w.NativeErrorCode == 1275 => "Windows blocked the WinDivert driver (driver blocklist or a security setting).",
        _ => ex.Message
    };

    // ------------------------------------------------------------------ packet path

    void RecvLoop()
    {
        var buf = new byte[65535 + 40];
        var addr = new WinDivertAddress();
        try
        {
            while (running)
            {
                if (!WinDivertNative.WinDivertRecv(net, buf, (uint)buf.Length, out var len, ref addr))
                {
                    var err = Marshal.GetLastWin32Error();
                    if (!running || err == 232) break;          // shut down
                    if (err == 122) continue;                   // buffer too small: skip this packet
                    Fail($"Packet capture stopped (error {err}).");
                    break;
                }
                Handle(buf, (int)len, ref addr);
            }
        }
        catch (Exception ex) { Fail(ex.Message); }
    }

    void Handle(byte[] buf, int len, ref WinDivertAddress addr)
    {
        nPackets++;
        switch (Decide(buf, len, ref addr, out var release))
        {
            case Verdict.Drop:
                nDropped++;
                return;
            case Verdict.Delay:
                nDelayed++;
                var copy = ArrayPool<byte>.Shared.Rent(len);
                Buffer.BlockCopy(buf, 0, copy, 0, len);
                lock (qlock)
                {
                    queue.Enqueue(new Held(copy, len, addr), (release, ++seq));
                    Monitor.Pulse(qlock);
                }
                return;
            default:
                WinDivertNative.WinDivertSend(net, buf, (uint)len, out _, ref addr);
                return;
        }
    }

    static long Secs(double s) => (long)(s * Freq);

    // Only outbound packets reach here (see the filter). Only Teams packets are ever touched.
    Verdict Decide(byte[] buf, int len, ref WinDivertAddress addr, out long release)
    {
        release = 0;
        if (!Parse(buf, len, out var proto, out var srcPort, out _)) return Verdict.Pass;

        int key = (proto << 16) | srcPort;
        if (!flowMap.TryGetValue(key, out var pid))
        {
            SeedIfDue();
            if (!flowMap.TryGetValue(key, out pid)) { nUnmapped++; return Verdict.Pass; }
        }
        if (!TeamsFilter.IsTeamsPid(pid)) return Verdict.Pass;
        nTeams++;

        var p = prof;
        long now = Stopwatch.GetTimestamp();
        if (!ReferenceEquals(p, lastProf))     // level changed: start fresh
        {
            lastProf = p; tat = 0; inBlackout = false;
            phaseEnd = now + Secs(p.ClearSec * (0.5 + rng.NextDouble()));
        }

        if (p.RateKBs == 0) return Verdict.Drop;                                   // Offline

        if (p.BlackoutSec > 0)                                                     // total silence, now and then
        {
            if (now >= phaseEnd)
            {
                inBlackout = !inBlackout;
                phaseEnd = now + Secs(inBlackout ? p.BlackoutSec * (0.5 + rng.NextDouble() * 0.5)
                                                 : p.ClearSec * (0.5 + rng.NextDouble()));
            }
            if (inBlackout) return Verdict.Drop;
        }

        if (p.LossPct > 0 && rng.NextDouble() * 100 < p.LossPct) return Verdict.Drop;

        long t = now;
        if (p.RateKBs > 0)                                                         // upload cap
        {
            t = Math.Max(now, tat - Burst);
            if (t - now > MaxDelay) return Verdict.Drop;                           // would wait too long
            tat = Math.Max(tat, t) + (long)(len / (p.RateKBs * 1000.0) * Freq);
        }
        if (p.JitterMs > 0) t += Secs(rng.NextDouble() * p.JitterMs / 1000.0);     // random extra delay

        release = t;
        return t - now < Freq / 2000 ? Verdict.Pass : Verdict.Delay;
    }

    // Reads protocol and ports from an IPv4/IPv6 packet. False = not something we can attribute.
    static bool Parse(byte[] b, int len, out byte proto, out ushort srcPort, out ushort dstPort)
    {
        proto = 0; srcPort = dstPort = 0;
        if (len < 24) return false;
        int ver = b[0] >> 4, off;
        if (ver == 4)
        {
            int ihl = (b[0] & 0x0F) * 4;
            if (ihl < 20 || len < ihl + 4) return false;
            if ((((b[6] & 0x1F) << 8) | b[7]) != 0) return false;   // later fragments carry no ports
            proto = b[9];
            off = ihl;
        }
        else if (ver == 6)
        {
            if (len < 44) return false;
            proto = b[6];
            off = 40;
        }
        else return false;

        if (proto != 6 && proto != 17) return false;
        srcPort = (ushort)((b[off] << 8) | b[off + 1]);
        dstPort = (ushort)((b[off + 2] << 8) | b[off + 3]);
        return true;
    }

    // Sends delayed packets when their time comes.
    void SendLoop()
    {
        try
        {
            while (running)
            {
                Held item;
                lock (qlock)
                {
                    while (true)
                    {
                        if (!running) return;
                        if (queue.TryPeek(out var h, out var pri))
                        {
                            var now = Stopwatch.GetTimestamp();
                            if (pri.Item1 <= now) { queue.Dequeue(); item = h; break; }
                            Monitor.Wait(qlock, (int)Math.Clamp((pri.Item1 - now) * 1000 / Freq, 1L, 50L));
                        }
                        else Monitor.Wait(qlock, 50);
                    }
                }
                var addr = item.Addr;
                WinDivertNative.WinDivertSend(net, item.Data, (uint)item.Len, out _, ref addr);
                ArrayPool<byte>.Shared.Return(item.Data);
            }
        }
        catch (Exception ex) { Fail(ex.Message); }
    }

    // ------------------------------------------------------------------ which process owns a connection

    void FlowLoop()
    {
        var addr = new WinDivertAddress();
        try
        {
            while (running)
            {
                if (!WinDivertNative.WinDivertRecv(flow, null, 0, out _, ref addr))
                {
                    var err = Marshal.GetLastWin32Error();
                    if (!running || err == 232) break;
                    continue;
                }
                int key = (addr.FlowProtocol << 16) | addr.FlowLocalPort;
                int pid = (int)addr.FlowPid;
                if (addr.Event == 1) flowMap[key] = pid;                                    // flow established
                else if (addr.Event == 2 && flowMap.TryGetValue(key, out var p) && p == pid) // flow deleted
                    flowMap.TryRemove(key, out _);
            }
        }
        catch (Exception ex) { Fail(ex.Message); }
    }

    void SeedIfDue()
    {
        if ((DateTime.UtcNow - lastSeed).TotalMilliseconds < 1000) return;
        Seed();
    }

    // Reads Windows' connection tables so connections that already exist are attributed too.
    void Seed()
    {
        lock (seedLock)
        {
            lastSeed = DateTime.UtcNow;
            ReadTable(true, 2); ReadTable(true, 23);     // TCP IPv4 / IPv6
            ReadTable(false, 2); ReadTable(false, 23);   // UDP IPv4 / IPv6
        }
    }

    static uint Table(IntPtr p, ref int size, bool tcp, int af) =>
        tcp ? WinDivertNative.GetExtendedTcpTable(p, ref size, false, af, 5, 0)    // TCP_TABLE_OWNER_PID_ALL
            : WinDivertNative.GetExtendedUdpTable(p, ref size, false, af, 1, 0);   // UDP_TABLE_OWNER_PID

    void ReadTable(bool tcp, int af)
    {
        int size = 0;
        uint rc = Table(IntPtr.Zero, ref size, tcp, af);
        for (int attempt = 0; attempt < 3 && rc == 122; attempt++)    // 122 = buffer too small
        {
            var p = Marshal.AllocHGlobal(size);
            try
            {
                rc = Table(p, ref size, tcp, af);
                if (rc != 0) continue;

                int n = Marshal.ReadInt32(p);
                int row = tcp ? (af == 2 ? 24 : 56) : (af == 2 ? 12 : 28);
                int portOff = tcp ? (af == 2 ? 8 : 20) : (af == 2 ? 4 : 20);
                int pidOff = tcp ? (af == 2 ? 20 : 52) : (af == 2 ? 8 : 24);
                for (int i = 0; i < n; i++)
                {
                    int o = 4 + i * row;
                    int raw = Marshal.ReadInt32(p, o + portOff);
                    int pid = Marshal.ReadInt32(p, o + pidOff);
                    int port = ((raw & 0xFF) << 8) | ((raw >> 8) & 0xFF);     // network -> host byte order
                    flowMap[((tcp ? 6 : 17) << 16) | port] = pid;
                }
                return;
            }
            finally { Marshal.FreeHGlobal(p); }
        }
    }

    public void Dispose()
    {
        lock (life) { if (running) StopLocked(); }
    }
}
