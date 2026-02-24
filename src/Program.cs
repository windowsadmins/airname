using System;
using System.Drawing;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace AirName;

static class Program
{
    private static Mutex? _mutex;

    [STAThread]
    static void Main()
    {
        const string mutexName = "Global\\AirName-{7A3F2E1B-4C5D-6E7F-8A9B-0C1D2E3F4A5B}";
        _mutex = new Mutex(true, mutexName, out bool createdNew);
        if (!createdNew) return;

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new TrayContext());
    }
}

sealed class TrayContext : ApplicationContext
{
    private readonly NotifyIcon _trayIcon;
    private readonly System.Windows.Forms.Timer _refreshTimer;

    public TrayContext()
    {
        string name = GetFriendlyName();

        _trayIcon = new NotifyIcon
        {
            Icon = LoadEmbeddedIcon(),
            Text = Truncate(name, 127),
            Visible = true,
            ContextMenuStrip = BuildMenu(name)
        };

        // Refresh every 5 minutes in case srvcomment changes after preflight
        _refreshTimer = new System.Windows.Forms.Timer { Interval = 300_000 };
        _refreshTimer.Tick += (_, _) => Refresh();
        _refreshTimer.Start();

        SystemEvents.SessionSwitch += (_, _) => Refresh();
    }

    private void Refresh()
    {
        string name = GetFriendlyName();
        _trayIcon.Text = Truncate(name, 127);
        _trayIcon.ContextMenuStrip = BuildMenu(name);
    }

    private static ContextMenuStrip BuildMenu(string name)
    {
        var menu = new ContextMenuStrip();
        var label = menu.Items.Add(name);
        label.Enabled = false;
        var font = label.Font;
        label.Font = new Font(font, FontStyle.Bold);

        menu.Items.Add(new ToolStripSeparator());

        var hostname = menu.Items.Add(Environment.MachineName);
        hostname.Enabled = false;

        menu.Items.Add(new ToolStripSeparator());

        var copy = menu.Items.Add("Copy Name");
        copy.Click += (_, _) => Clipboard.SetText(name);

        menu.Items.Add(new ToolStripSeparator());

        var quit = menu.Items.Add("Quit");
        quit.Click += (_, _) => Application.Exit();

        return menu;
    }

    private static string GetFriendlyName()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Services\LanmanServer\Parameters");
            if (key?.GetValue("srvcomment") is string comment && !string.IsNullOrWhiteSpace(comment))
                return comment;
        }
        catch
        {
            // Registry access may fail in restricted contexts
        }

        return Environment.MachineName;
    }

    private static Icon LoadEmbeddedIcon()
    {
        var asm = Assembly.GetExecutingAssembly();
        // Embedded resource name follows: DefaultNamespace.filename
        foreach (string res in asm.GetManifestResourceNames())
        {
            if (res.EndsWith(".ico", StringComparison.OrdinalIgnoreCase))
            {
                using var stream = asm.GetManifestResourceStream(res);
                if (stream != null) return new Icon(stream);
            }
        }
        return SystemIcons.Information;
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max];

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _refreshTimer.Stop();
            _refreshTimer.Dispose();
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }
        base.Dispose(disposing);
    }
}
