using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

/// <summary>
/// Remembers your groups, group names and speed limits in %AppData%\ChokePoint\settings.json.
/// The page decides what goes in the file; this class just stores and returns it.
/// (Blocks and disruption are never saved.)
/// </summary>
static class AppSettings
{
    static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ChokePointTeams");
    static string FilePath => Path.Combine(Dir, "settings.json");

    public static JsonElement? Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(FilePath));
            return doc.RootElement.Clone();
        }
        catch { return null; }
    }

    public static void Save(JsonElement settings)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, Pretty));
        }
        catch { /* not being able to save must never break the app */ }
    }
}

record SavedWindow(int X, int Y, int Width, int Height, bool Maximized, string? Monitor);
record Placement(Rectangle Bounds, Screen Screen, bool Maximized);

/// <summary>Remembers where the window was (size, position, maximised, which monitor) in %AppData%\ChokePoint\window.json.</summary>
static class WindowSettings
{
    static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };
    static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ChokePointTeams", "window.json");

    public static SavedWindow? Load()
    {
        try { return File.Exists(FilePath) ? JsonSerializer.Deserialize<SavedWindow>(File.ReadAllText(FilePath)) : null; }
        catch { return null; }
    }

    public static void Save(SavedWindow w)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(w, Opts));
        }
        catch { /* never break the app over this */ }
    }

    // Works out where the window should go, given the monitors that exist right now.
    public static Placement? Resolve(SavedWindow? saved)
    {
        if (saved == null || saved.Width < 400 || saved.Height < 300) return null;
        var screens = Screen.AllScreens;
        var rect = new Rectangle(saved.X, saved.Y, saved.Width, saved.Height);

        // Same monitor as last time, if it is still connected.
        var screen = screens.FirstOrDefault(s => s.DeviceName == saved.Monitor);
        if (screen == null)
        {
            // That monitor is gone: keep the old spot only if it is still clearly visible on another one.
            screen = screens.FirstOrDefault(s => Rectangle.Intersect(s.WorkingArea, rect) is { Width: >= 200, Height: >= 100 });
            if (screen == null) return null;     // otherwise use the normal default placement
        }

        // Keep it fully on that monitor (the resolution may have changed).
        var wa = screen.WorkingArea;
        int w = Math.Min(rect.Width, wa.Width), h = Math.Min(rect.Height, wa.Height);
        int x = Math.Clamp(rect.X, wa.Left, wa.Right - w), y = Math.Clamp(rect.Y, wa.Top, wa.Bottom - h);
        return new Placement(new Rectangle(x, y, w, h), screen, saved.Maximized);
    }
}

static class Program
{
    [STAThread]
    static void Main()
    {
        // Only one copy at a time: a second launch just brings the first window to the front.
        using var mutex = new Mutex(true, @"Global\ChokePointTeams.SingleInstance", out var firstInstance);
        if (!firstInstance) { WindowFinder.BringToFront("ChokePoint for Teams"); return; }

        ApplicationConfiguration.Initialize();
        var place = WindowSettings.Resolve(WindowSettings.Load());   // where the window was last time
        var splash = new SplashForm(place?.Screen);
        splash.Show();
        splash.Refresh();
        Application.Run(new MainForm(splash, place));
    }
}

static class WindowFinder
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string? className, string title);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd, int command);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);

    public static void BringToFront(string title)
    {
        var h = FindWindow(null, title);
        if (h == IntPtr.Zero) return;
        if (IsIconic(h)) ShowWindow(h, 9);                 // 9 = restore
        else if (!IsWindowVisible(h)) ShowWindow(h, 5);    // 5 = show (it was hidden in the tray)
        SetForegroundWindow(h);
    }
}

static class Theme
{
    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    static int Rgb(int r, int g, int b) => r | (g << 8) | (b << 16);

    // Dark title bar: Windows 10 (1809+) and 11. On Windows 11 the thin window border is removed.
    public static void Apply(IntPtr hwnd)
    {
        int on = 1;
        if (DwmSetWindowAttribute(hwnd, 20, ref on, 4) != 0)
            DwmSetWindowAttribute(hwnd, 19, ref on, 4);   // older Windows 10 builds

        int caption = Rgb(14, 11, 18);      // title bar background
        int text    = Rgb(246, 239, 255);   // title bar text
        int noBorder = unchecked((int)0xFFFFFFFE);   // "no border color" (Windows 11 only; ignored on 10)
        DwmSetWindowAttribute(hwnd, 35, ref caption, 4);
        DwmSetWindowAttribute(hwnd, 36, ref text, 4);
        DwmSetWindowAttribute(hwnd, 34, ref noBorder, 4);
    }
}

class SplashForm : Form
{
    [DllImport("gdi32.dll")] static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int ellipseW, int ellipseH);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);

    // Rounded corners on a borderless window. The last two numbers are the corner ellipse size: 6 = a 3 px radius.
    protected override void OnSizeChanged(EventArgs e)
{
    base.OnSizeChanged(e);
    var h = CreateRoundRectRgn(0, 0, Width + 1, Height + 1, 6, 6);
    Region = Region.FromHrgn(h);
    DeleteObject(h);
}

    public SplashForm(Screen? screen = null)
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(240, 240);          // half the previous size
        if (screen != null)                       // show it on the monitor the app will open on
        {
            StartPosition = FormStartPosition.Manual;
            var wa = screen.WorkingArea;
            Location = new Point(wa.Left + (wa.Width - Width) / 2, wa.Top + (wa.Height - Height) / 2);
        }
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = Color.FromArgb(14, 11, 18);

        var logo = new PictureBox
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(14, 11, 18),
            SizeMode = PictureBoxSizeMode.Zoom       // keeps the icon centered both ways
        };
        var file = Path.Combine(AppContext.BaseDirectory, "assets", "choke-point-splash.png");
        if (File.Exists(file)) logo.Image = Image.FromFile(file);
        Controls.Add(logo);
    }
}

class MainForm : Form
{
    static readonly Color Dark = Color.FromArgb(14, 11, 18);

    readonly WebView2 web = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Dark };
    readonly SplashForm splash;
    readonly NetMonitor monitor = new();
    readonly Limiter limiter;
    readonly NotifyIcon tray = new() { Text = "ChokePoint for Teams" };

    // Tray menu. Keep these names in step with the L array in ui\index.html.
    static readonly string[] LevelNames =
    {
        "No Effect", "Minor Turbulence", "Moderate Disturbance",
        "Significant Disruption", "Major Disruption", "Appear Offline"
    };
    readonly ToolStripMenuItem openItem = new("Open");
    readonly ToolStripMenuItem[] levelItems = new ToolStripMenuItem[LevelNames.Length];
    ContextMenuStrip? trayMenu;
    Font? boldFont;
    int currentLevel;                                  // the level in use, whoever set it (page or tray menu)
    bool exiting;                                      // true only when we really want to quit (the X button just hides)
    static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };
    readonly System.Windows.Forms.Timer timer = new() { Interval = 500 };
    readonly DateTime started = DateTime.UtcNow;
    bool ready;
    Rectangle normal;                                  // the window's size and position when not maximised
    FormWindowState lastState = FormWindowState.Normal;

    public MainForm(SplashForm splash, Placement? place)
    {
        this.splash = splash;
        limiter = new Limiter(ProfileSettings.Load(out var profileWarning));
        if (profileWarning is not null)
            Shown += (_, _) => MessageBox.Show(this, profileWarning, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        Text = "ChokePoint for Teams";
        if (place is { } p)
        {
            // Put the window back exactly where it was: same monitor, same size, same maximised state.
            StartPosition = FormStartPosition.Manual;
            Bounds = p.Bounds;
            normal = p.Bounds;
            if (p.Maximized) { WindowState = FormWindowState.Maximized; lastState = FormWindowState.Maximized; }
        }
        else
        {
            StartPosition = FormStartPosition.CenterScreen;
            var area = Screen.PrimaryScreen!.WorkingArea;
            Width = Math.Min(700, area.Width - 40);
            Height = Math.Min(900, area.Height - 40);
            normal = Bounds;
        }
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        tray.Icon = Icon;
        BuildTrayMenu();
        tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) RestoreFromTray(); };
        tray.Visible = true;
        ShowInTaskbar = false;                        // the app lives in the system tray only
        Opacity = 0;                                  // hidden until the page is ready

        BackColor = Dark;

        Controls.Add(web);

        Load += async (_, _) =>
        {
            web.CreationProperties = new CoreWebView2CreationProperties
            {
                UserDataFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "ChokePointTeams", "WebView2")
            };
            try { await web.EnsureCoreWebView2Async(); }
            catch (WebView2RuntimeNotFoundException)
            {
                splash.Close();
                MessageBox.Show(
                    "ChokePoint needs the Microsoft Edge WebView2 Runtime, and it is not installed on this PC.\n\n" +
                    "Click OK to open Microsoft's download page. Install the \"Evergreen\" version, then start ChokePoint again.",
                    "ChokePoint - missing component", MessageBoxButtons.OK, MessageBoxIcon.Information);
                OpenLink("https://developer.microsoft.com/microsoft-edge/webview2/");
                ExitApp();
                return;
            }
            catch (Exception ex)
            {
                splash.Close();
                MessageBox.Show("ChokePoint could not start its display:\n\n" + ex.Message,
                    "ChokePoint", MessageBoxButtons.OK, MessageBoxIcon.Error);
                ExitApp();
                return;
            }

            monitor.Start();

            // The page sends its limits here whenever something changes.
            web.CoreWebView2.WebMessageReceived += (_, e) =>
            {
                try
                {
                    using var doc = JsonDocument.Parse(e.WebMessageAsJson);
                    var type = doc.RootElement.TryGetProperty("type", out var t) ? t.GetString() : "config";
                    switch (type)
                    {
                        case "hello":      // the page just loaded: send back the saved settings
                            monitor.ResendIcons();
                            web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(
                                new { settings = AppSettings.Load() }, Json));
                            break;
                        case "about":      // the About link in the page
                            BeginInvoke(new Action(ShowAbout));
                            break;
                        case "settings":   // groups, speed limits or display options changed: save them
                            AppSettings.Save(doc.RootElement.GetProperty("settings").Clone());
                            break;
                        case "level":      // the disturbance buttons
                            ApplyLevel(doc.RootElement.GetProperty("level").GetInt32(), false);
                            break;
                    }
                }
                catch { /* ignore malformed messages */ }
            };
            timer.Tick += (_, _) => Push();
            timer.Start();

            web.CoreWebView2.NavigationCompleted += async (_, _) =>
            {
                if (ready) return;
                ready = true;
                var wait = 1800 - (int)(DateTime.UtcNow - started).TotalMilliseconds;
                if (wait > 0) await Task.Delay(wait);   // show the splash for at least ~1.8 s
                Opacity = 1;
                splash.Close();
                Activate();
            };
            var page = Path.Combine(AppContext.BaseDirectory, "ui", "index.html");
            web.CoreWebView2.Navigate(new Uri(page).AbsoluteUri);
        };
    }

    // Send the latest per-app rates to the page twice a second.
    void Push()
    {
        if (web.CoreWebView2 is null) return;
        var json = JsonSerializer.Serialize(
            new { apps = monitor.Snapshot().Where(a => TeamsFilter.IsTeams(a.Key)).ToList(), error = monitor.Error, engine = limiter.Status() }, Json);
        web.CoreWebView2.PostWebMessageAsJson(json);
    }

    // ---- About ----
    static string AppVersion()
    {
        var v = FileVersionInfo.GetVersionInfo(Application.ExecutablePath).ProductVersion ?? "1.0.0";
        var plus = v.IndexOf('+');
        return plus > 0 ? v[..plus] : v;
    }

    static void OpenLink(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }

    void ShowAbout()
    {
        using var f = new AboutForm(AppVersion(), BuildDiagnostics);
        f.ShowDialog(this);
    }

    // The text the "Copy diagnostic info" button puts on the clipboard (handy for bug reports).
    string BuildDiagnostics()
    {
        var s = limiter.Status();
        bool admin = new System.Security.Principal.WindowsPrincipal(
            System.Security.Principal.WindowsIdentity.GetCurrent())
            .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        string webview = "unknown";
        try { webview = web.CoreWebView2?.Environment.BrowserVersionString ?? "unknown"; } catch { }
        var settingsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ChokePointTeams");
        return string.Join(Environment.NewLine, new[]
        {
            $"ChokePoint Teams {AppVersion()}",
            $"Windows: {RuntimeInformation.OSDescription} (build {Environment.OSVersion.Version.Build})",
            $".NET: {RuntimeInformation.FrameworkDescription}",
            $"WebView2: {webview}",
            $"Administrator: {admin}",
            $"Traffic monitor: {monitor.Error ?? "running"}",
            $"Limiter: {s.State}{(s.Error != null ? " - " + s.Error : "")} (packets {s.Packets}, delayed {s.Delayed}, dropped {s.Dropped}, not matched {s.Unmapped})",
            $"Settings folder: {settingsDir}"
        });
    }

    // ---- tray icon and its menu ----
    void BuildTrayMenu()
    {
        var menu = trayMenu = new ContextMenuStrip();
        openItem.Click += (_, _) => { if (Visible && WindowState != FormWindowState.Minimized) HideToTray(); else RestoreFromTray(); };
        menu.Items.Add(openItem);
        menu.Items.Add(new ToolStripSeparator());
        for (int i = 0; i < LevelNames.Length; i++)
        {
            int level = i;                             // captured by the click handler
            levelItems[i] = new ToolStripMenuItem(LevelNames[i]);
            levelItems[i].Click += (_, _) => ApplyLevel(level, true);
            menu.Items.Add(levelItems[i]);
        }
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitApp());
        menu.Opening += (_, _) => openItem.Text = Visible && WindowState != FormWindowState.Minimized ? "Minimise to tray" : "Open";
        tray.ContextMenuStrip = menu;
        ShowLevelInTray();
    }

    // The one place a level is applied. tellPage = the change came from the tray menu, so the page must update its buttons.
    void ApplyLevel(int level, bool tellPage)
    {
        currentLevel = Math.Clamp(level, 0, LevelNames.Length - 1);
        limiter.SetLevel(currentLevel);
        ShowLevelInTray();
        if (tellPage) web.CoreWebView2?.PostWebMessageAsJson("{\"setLevel\":" + currentLevel + "}");
    }

    // Tick and bold the level in use, and show it in the icon's tooltip.
    void ShowLevelInTray()
    {
        if (trayMenu is null) return;
        boldFont ??= new Font(trayMenu.Font, FontStyle.Bold);
        for (int i = 0; i < levelItems.Length; i++)
        {
            bool on = i == currentLevel;
            levelItems[i].Checked = on;
            levelItems[i].Font = on ? boldFont : trayMenu.Font;
        }
        tray.Text = "ChokePoint for Teams: " + LevelNames[currentLevel];     // tooltips are limited to 63 characters
    }

    void HideToTray() { SaveWindow(); Hide(); }

    void ExitApp() { exiting = true; Close(); }

    void RestoreFromTray()
    {
        Show();
        WindowState = lastState;      // Normal or Maximized, whichever it was before
        TopMost = true;               // jump above every other window...
        TopMost = false;              // ...then go back to behaving normally
        Activate();
    }

    // ---- remember the window ----
    void TrackWindow()
    {
        if (!IsHandleCreated) return;
        if (WindowState == FormWindowState.Normal) normal = Bounds;
        if (WindowState != FormWindowState.Minimized && WindowState != lastState)
        {
            lastState = WindowState;
            SaveWindow();                              // maximise / restore was clicked
        }
    }

    void SaveWindow()
    {
        if (!IsHandleCreated) return;
        var screen = WindowState == FormWindowState.Minimized ? Screen.FromRectangle(normal) : Screen.FromHandle(Handle);
        WindowSettings.Save(new SavedWindow(normal.X, normal.Y, normal.Width, normal.Height,
            lastState == FormWindowState.Maximized, screen.DeviceName));
    }

    protected override void OnLocationChanged(EventArgs e) { base.OnLocationChanged(e); TrackWindow(); }
    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        TrackWindow();
        if (WindowState == FormWindowState.Minimized) Hide();   // minimise = go to the tray
    }
    protected override void OnResizeEnd(EventArgs e) { base.OnResizeEnd(e); SaveWindow(); }     // after dragging or resizing
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!exiting && e.CloseReason == CloseReason.UserClosing)    // the X button: hide to the tray instead of quitting
        {
            e.Cancel = true;
            HideToTray();
            return;
        }
        SaveWindow();
        base.OnFormClosing(e);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.Apply(Handle);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        timer.Stop();
        limiter.Dispose();      // closes WinDivert, so normal traffic resumes immediately
        monitor.Dispose();
        tray.Visible = false;
        tray.Dispose();
        base.OnFormClosed(e);
    }
}
