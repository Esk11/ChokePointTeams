using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Win32;

record AppId(string Name, string Key, bool IsService, string? Path = null);

/// <summary>
/// Turns a process id into an "app" (all processes of the same .exe) and says whether it is a
/// background service. Thread-safe: used by both the traffic monitor and the limiter.
/// </summary>
static class Apps
{
    static readonly ConcurrentDictionary<int, AppId> cache = new();
    static readonly HashSet<string> serviceImages = LoadServiceImages();

    public static AppId Resolve(int pid)
    {
        if (cache.TryGetValue(pid, out var hit)) return hit;

        var id = pid is 0 or 4
            ? new AppId("System", "system", true)
            : new AppId("Unknown", "unknown", true);
        try
        {
            using var p = Process.GetProcessById(pid);
            string? path = null;
            try { path = p.MainModule?.FileName; } catch { /* protected process */ }
            var key = (path ?? p.ProcessName).ToLowerInvariant();
            // Services run in session 0, or are registered as services in the registry.
            var svc = p.SessionId == 0 || (path != null && serviceImages.Contains(Normalize(path)));
            id = new AppId(p.ProcessName, key, svc, path);
        }
        catch { /* process already gone */ }

        cache[pid] = id;
        return id;
    }

    public static void Prune(HashSet<int> alive)
    {
        foreach (var pid in cache.Keys)
            if (!alive.Contains(pid)) cache.TryRemove(pid, out _);
    }

    static string Normalize(string path)
    {
        try { return Path.GetFullPath(path).ToLowerInvariant(); }
        catch { return path.ToLowerInvariant(); }
    }

    // Executable paths of every registered Windows service.
    static HashSet<string> LoadServiceImages()
    {
        var set = new HashSet<string>();
        try
        {
            using var root = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services");
            if (root is null) return set;
            foreach (var name in root.GetSubKeyNames())
            {
                try
                {
                    using var k = root.OpenSubKey(name);
                    if (k?.GetValue("Type") is not int type || (type & 0x30) == 0) continue;  // own/shared process only
                    if (k.GetValue("ImagePath") is not string raw) continue;
                    var path = ExtractExe(raw);
                    if (path != null) set.Add(Normalize(path));
                }
                catch { }
            }
        }
        catch { }
        return set;
    }

    static string? ExtractExe(string raw)
    {
        var s = raw.Trim();
        if (s.StartsWith(@"\??\")) s = s[4..];
        string path;
        if (s.StartsWith('"'))
        {
            var end = s.IndexOf('"', 1);
            if (end < 0) return null;
            path = s[1..end];
        }
        else
        {
            var i = s.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (i < 0) return null;
            path = s[..(i + 4)];
        }
        var win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (path.StartsWith(@"\SystemRoot\", StringComparison.OrdinalIgnoreCase)) path = Path.Combine(win, path[12..]);
        else if (!Path.IsPathRooted(path)) path = Path.Combine(win, path);
        return path;
    }
}

/// <summary>Reads an app's own icon from its .exe and returns it as a small PNG "data URL" for the page.</summary>
static class AppIcons
{
    static readonly Dictionary<string, string?> cache = new();   // app key -> data URL, or null if it has none

    public static string? Get(AppId id)
    {
        if (id.Path == null) return null;
        if (cache.TryGetValue(id.Key, out var hit)) return hit;

        string? url = null;
        try
        {
            using var icon = Icon.ExtractAssociatedIcon(id.Path);
            if (icon != null)
            {
                using var bmp = icon.ToBitmap();
                using var ms = new MemoryStream();
                bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                url = "data:image/png;base64," + Convert.ToBase64String(ms.ToArray());
            }
        }
        catch { /* no icon available: the page shows the app's first letter instead */ }

        cache[id.Key] = url;
        return url;
    }
}
