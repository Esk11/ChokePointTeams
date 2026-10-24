using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;

record AppSample(string Key, string Name, int Procs, bool Service, double Down, double Up, string? Icon = null);

/// <summary>
/// Watches per-process TCP/UDP traffic using Windows kernel ETW events.
/// Needs administrator rights. Monitoring only: it never changes any traffic.
/// </summary>
sealed class NetMonitor : IDisposable
{
    // Bytes seen since the last snapshot. [0] = received (download), [1] = sent (upload).
    readonly ConcurrentDictionary<int, long[]> bytes = new();

    // Everything below is only touched from Snapshot(), which runs on the UI thread.
    readonly Dictionary<string, AppId> info = new();                // app key -> identity
    readonly Dictionary<string, HashSet<int>> pids = new();         // app key -> live process ids
    readonly Dictionary<string, DateTime> lastActive = new();       // app key -> last traffic
    readonly HashSet<string> iconsSent = new();                     // apps whose icon the page already has
    DateTime lastSnap = DateTime.UtcNow, lastPrune = DateTime.UtcNow;

    TraceEventSession? session;
    public string? Error { get; private set; }

    public void Start() => new Thread(Run) { IsBackground = true, Name = "ETW" }.Start();

    void Run()
    {
        // Try a private session name first, then the classic "NT Kernel Logger" name.
        foreach (var name in new[] { "ChokePointTeamsNetwork", KernelTraceEventParser.KernelSessionName })
        {
            try
            {
                using var s = new TraceEventSession(name) { StopOnDispose = true };
                session = s;
                // The kernel provider must be enabled BEFORE hooking Source.Kernel events.
                s.EnableKernelProvider(KernelTraceEventParser.Keywords.NetworkTCPIP);
                Hook(s.Source.Kernel);
                Error = null;
                s.Source.Process();     // blocks until the session is disposed
                return;
            }
            catch (Exception ex) { Error = ex.Message; }
        }
    }

    void Hook(KernelTraceEventParser k)
    {
        k.TcpIpRecv     += d => Add(d.ProcessID, d.size, 0);
        k.TcpIpRecvIPV6 += d => Add(d.ProcessID, d.size, 0);
        k.UdpIpRecv     += d => Add(d.ProcessID, d.size, 0);
        k.UdpIpRecvIPV6 += d => Add(d.ProcessID, d.size, 0);
        k.TcpIpSend     += d => Add(d.ProcessID, 0, d.size);
        k.TcpIpSendIPV6 += d => Add(d.ProcessID, 0, d.size);
        k.UdpIpSend     += d => Add(d.ProcessID, 0, d.size);
        k.UdpIpSendIPV6 += d => Add(d.ProcessID, 0, d.size);
    }

    void Add(int pid, long down, long up)
    {
        var a = bytes.GetOrAdd(pid, _ => new long[2]);
        Interlocked.Add(ref a[0], down);
        Interlocked.Add(ref a[1], up);
    }

    /// <summary>Rates (KB/s, 1 KB = 1000 bytes) per app since the previous call.</summary>
    public List<AppSample> Snapshot()
    {
        var now = DateTime.UtcNow;
        var secs = Math.Max(0.05, (now - lastSnap).TotalSeconds);
        lastSnap = now;

        var rate = new Dictionary<string, (double Down, double Up)>();
        foreach (var kv in bytes)
        {
            var d = Interlocked.Exchange(ref kv.Value[0], 0);
            var u = Interlocked.Exchange(ref kv.Value[1], 0);
            if (d == 0 && u == 0) continue;

            var id = Apps.Resolve(kv.Key);
            info[id.Key] = id;
            lastActive[id.Key] = now;
            if (!pids.TryGetValue(id.Key, out var set)) pids[id.Key] = set = new HashSet<int>();
            set.Add(kv.Key);

            rate.TryGetValue(id.Key, out var r);
            rate[id.Key] = (r.Down + d / secs / 1000.0, r.Up + u / secs / 1000.0);
        }

        if ((now - lastPrune).TotalSeconds > 10) { Prune(); lastPrune = now; }

        var list = new List<AppSample>();
        foreach (var (key, seen) in lastActive.ToList())
        {
            if ((now - seen).TotalSeconds > 30)          // idle for 30 s: drop the card
            {
                lastActive.Remove(key);
                info.Remove(key);
                pids.Remove(key);
                continue;
            }
            rate.TryGetValue(key, out var r);
            var id = info[key];
            var procs = Math.Max(1, pids.TryGetValue(key, out var s) ? s.Count : 1);
            var icon = iconsSent.Add(key) ? AppIcons.Get(id) : null;     // an icon is sent only once per app
            list.Add(new AppSample(key, id.Name, procs, id.IsService, r.Down, r.Up, icon));
        }
        return list;
    }

    // Forget processes that no longer exist.
    void Prune()
    {
        var alive = new HashSet<int>();
        foreach (var p in Process.GetProcesses()) { alive.Add(p.Id); p.Dispose(); }
        Apps.Prune(alive);
        foreach (var set in pids.Values) set.RemoveWhere(p => !alive.Contains(p));
        foreach (var pid in bytes.Keys) if (!alive.Contains(pid)) bytes.TryRemove(pid, out _);
    }

    public void ResendIcons() => iconsSent.Clear();     // the page just (re)loaded: send every icon again

    public void Dispose() => session?.Dispose();
}
