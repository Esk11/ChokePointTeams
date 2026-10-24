using System.Diagnostics;

/// <summary>The About window: version, a button that copies diagnostic info, licenses and support links.</summary>
class AboutForm : Form
{
    // Fill these in when you have them. An empty address hides that button.
    const string DonateUrl = "";     // e.g. "https://ko-fi.com/yourname"
    const string ProjectUrl = "";    // e.g. "https://github.com/yourname/ChokePoint"

    public AboutForm(string version, Func<string> diagnostics)
    {
        Text = "About ChokePoint for Teams";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(460, 290);
        BackColor = Color.FromArgb(14, 11, 18);
        ForeColor = Color.FromArgb(246, 239, 255);
        Font = new Font("Segoe UI", 9.5f);

        Controls.Add(new Label
        {
            Text = "ChokePoint Teams",
            Font = new Font("Segoe UI", 20f, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(20, 16)
        });
        Controls.Add(new Label
        {
            Text = "Version " + version,
            AutoSize = true,
            ForeColor = Color.FromArgb(131, 120, 158),
            Location = new Point(23, 62)
        });
        Controls.Add(new Label
        {
            Text = "Simulates network disruption by making the Microsoft Teams upload traffic misbehave.\n\n" +
                   "Only your window position is saved, in your " +
                   "AppData\\ChokePointTeams folder. Nothing is sent anywhere.",
            AutoSize = false,
            Size = new Size(415, 90),
            Location = new Point(23, 96)
        });

        var copy = AddButton("Copy diagnostic info", 23, 205, 165, null);
        copy.Click += (_, _) =>
        {
            try { Clipboard.SetText(diagnostics()); copy.Text = "Copied to clipboard"; }
            catch { copy.Text = "Could not copy"; }
        };

        AddButton("Third-party notices", 196, 205, 150, () =>
            Open(Path.Combine(AppContext.BaseDirectory, "THIRD-PARTY-NOTICES.txt")));

        int x = 23;
        if (DonateUrl != "") { AddButton("Buy me a coffee", x, 245, 140, () => Open(DonateUrl)); x += 148; }
        if (ProjectUrl != "") { AddButton("Project page", x, 245, 120, () => Open(ProjectUrl)); }

        var close = AddButton("Close", 355, 245, 82, Close);
        AcceptButton = close;
        CancelButton = close;
    }

    Button AddButton(string text, int x, int y, int w, Action? click)
    {
        var b = new Button
        {
            Text = text,
            Location = new Point(x, y),
            Size = new Size(w, 30),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(23, 21, 26),
            ForeColor = Color.FromArgb(246, 239, 255),
            UseVisualStyleBackColor = false
        };
        b.FlatAppearance.BorderColor = Color.FromArgb(135, 133, 138);
        if (click != null) b.Click += (_, _) => click();
        Controls.Add(b);
        return b;
    }

    static void Open(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch { /* nothing sensible to do if it cannot be opened */ }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.Apply(Handle);          // dark title bar, like the main window
    }
}
