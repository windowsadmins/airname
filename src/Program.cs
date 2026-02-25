using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Reflection;
using System.Runtime.InteropServices;
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
        Application.Run(new AirNameContext());
    }
}

sealed class AirNameContext : ApplicationContext
{
    private readonly NotifyIcon _trayIcon;
    private readonly NameLabel _label;
    private readonly System.Windows.Forms.Timer _refreshTimer;

    public AirNameContext()
    {
        string name = GetFriendlyName();

        _trayIcon = new NotifyIcon
        {
            Icon = LoadEmbeddedIcon(),
            Text = Truncate(name, 127),
            Visible = true,
            ContextMenuStrip = BuildMenu(name)
        };

        _label = new NameLabel(name);
        _label.Show();

        _refreshTimer = new System.Windows.Forms.Timer { Interval = 300_000 };
        _refreshTimer.Tick += (_, _) => Refresh();
        _refreshTimer.Start();

        SystemEvents.SessionSwitch += (_, _) => Refresh();
        SystemEvents.DisplaySettingsChanged += (_, _) => _label.Reposition();
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category == UserPreferenceCategory.General)
                _label.Reposition(); // theme change triggers re-render
        };
    }

    private void Refresh()
    {
        string name = GetFriendlyName();
        _trayIcon.Text = Truncate(name, 127);
        _trayIcon.ContextMenuStrip = BuildMenu(name);
        _label.UpdateName(name);
    }

    private static ContextMenuStrip BuildMenu(string name)
    {
        var menu = new ContextMenuStrip();

        var label = menu.Items.Add(name);
        label.Enabled = false;
        label.Font = new Font(label.Font, FontStyle.Bold);

        menu.Items.Add(new ToolStripSeparator());

        var copy = menu.Items.Add("Copy Name");
        copy.Click += (_, _) => Clipboard.SetText(name);

        return menu;
    }

    internal static string GetFriendlyName()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Services\LanmanServer\Parameters");
            if (key?.GetValue("srvcomment") is string comment && !string.IsNullOrWhiteSpace(comment))
                return comment;
        }
        catch { }

        return Environment.MachineName;
    }

    private static Icon LoadEmbeddedIcon()
    {
        var asm = Assembly.GetExecutingAssembly();
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
            _label.Close();
            _label.Dispose();
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }
        base.Dispose(disposing);
    }
}

/// <summary>
/// Always-visible floating text label showing the computer's friendly name.
/// No background — just text with a subtle drop shadow for readability.
/// Positioned bottom-left, overlaying the taskbar next to the Start button.
/// Automatically adapts text color to system light/dark mode.
/// </summary>
sealed class NameLabel : Form
{
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_LAYERED = 0x00080000;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOPMOST = 0x00000008;
    private const int WM_NCHITTEST = 0x0084;
    private const int HTTRANSPARENT = -1;
    private const byte AC_SRC_OVER = 0;
    private const byte AC_SRC_ALPHA = 1;
    private const int ULW_ALPHA = 2;

    private const int SWP_NOMOVE = 0x0002;
    private const int SWP_NOSIZE = 0x0001;
    private const int SWP_NOACTIVATE = 0x0010;
    private static readonly IntPtr HWND_TOPMOST = new(-1);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst,
        ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc, ref POINT pptSrc,
        int crKey, ref BLENDFUNCTION pblend, int dwFlags);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE { public int Width, Height; }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct BLENDFUNCTION
    {
        public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat;
    }

    private static readonly Font NameFont = new("Segoe UI Variable Display", 13f, FontStyle.Bold);
    private static readonly Font FallbackFont = new("Segoe UI", 13f, FontStyle.Bold);
    private const int PadX = 12;
    private const int PadY = 4;

    private string _name;
    private readonly System.Windows.Forms.Timer _topmostTimer;
    private IntPtr _winEventHook;

    private delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType,
        IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax,
        IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc, uint idProcess,
        uint idThread, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    private const uint EVENT_SYSTEM_FOREGROUND = 3;
    private const uint WINEVENT_OUTOFCONTEXT = 0;
    private const uint WINEVENT_SKIPOWNPROCESS = 2;

    // prevent GC of delegate
    private readonly WinEventDelegate _winEventProc;

    public NameLabel(string name)
    {
        _name = name;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;

        // Re-assert topmost aggressively so taskbar redraws can't bury us
        _topmostTimer = new System.Windows.Forms.Timer { Interval = 500 };
        _topmostTimer.Tick += (_, _) =>
            SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        _topmostTimer.Start();

        // Instant re-assert when any window takes focus (catches taskbar clicks)
        _winEventProc = OnForegroundChanged;
        _winEventHook = SetWinEventHook(
            EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero, _winEventProc, 0, 0,
            WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_LAYERED
                        | WS_EX_TRANSPARENT | WS_EX_TOPMOST;
            return cp;
        }
    }

    private void OnForegroundChanged(IntPtr hWinEventHook, uint eventType,
        IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    protected override bool ShowWithoutActivation => true;

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        RenderAndPosition();
    }

    public void UpdateName(string name)
    {
        if (_name == name) return;
        _name = name;
        RenderAndPosition();
    }

    public void Reposition() => RenderAndPosition();

    private static bool IsLightMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            // SystemUsesLightTheme: 1 = light taskbar, 0 = dark taskbar
            if (key?.GetValue("SystemUsesLightTheme") is int val)
                return val == 1;
        }
        catch { }
        return false; // default dark
    }

    private Font ResolveFont()
    {
        // Check if Segoe UI Variable is available
        using var fc = new System.Drawing.Text.InstalledFontCollection();
        foreach (var family in fc.Families)
        {
            if (family.Name.Equals("Segoe UI Variable Display", StringComparison.OrdinalIgnoreCase))
                return NameFont;
        }
        return FallbackFont;
    }

    private void RenderAndPosition()
    {
        bool lightMode = IsLightMode();
        var textColor = lightMode ? Color.FromArgb(230, 0, 0, 0) : Color.FromArgb(230, 255, 255, 255);
        var shadowColor = lightMode ? Color.FromArgb(80, 255, 255, 255) : Color.FromArgb(80, 0, 0, 0);
        var font = ResolveFont();

        // Measure text
        Size textSize;
        using (var bmp = new Bitmap(1, 1))
        using (var g = Graphics.FromImage(bmp))
        {
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            var sz = g.MeasureString(_name, font);
            textSize = new Size((int)Math.Ceiling(sz.Width), (int)Math.Ceiling(sz.Height));
        }

        int w = textSize.Width + PadX * 2;
        int h = textSize.Height + PadY * 2;

        // Position: bottom-left, centered vertically on the taskbar
        var screen = Screen.PrimaryScreen!;
        int taskbarHeight = screen.Bounds.Height - screen.WorkingArea.Height;
        if (taskbarHeight < 30) taskbarHeight = 48; // sensible default

        int x = screen.WorkingArea.Left + 4;
        int y = screen.WorkingArea.Bottom + (taskbarHeight - h) / 2;

        // Render to 32bpp ARGB — no background, just text + shadow
        using var bitmap = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            g.Clear(Color.Transparent);

            using var sf = new StringFormat
            {
                Alignment = StringAlignment.Near,
                LineAlignment = StringAlignment.Center
            };
            var rect = new RectangleF(PadX, 0, w - PadX * 2, h);

            // Drop shadow (1px offset)
            using var shadowBrush = new SolidBrush(shadowColor);
            var shadowRect = rect;
            shadowRect.Offset(1, 1);
            g.DrawString(_name, font, shadowBrush, shadowRect, sf);

            // Main text
            using var textBrush = new SolidBrush(textColor);
            g.DrawString(_name, font, textBrush, rect, sf);
        }

        // Apply via UpdateLayeredWindow
        IntPtr screenDc = IntPtr.Zero;
        IntPtr memDc = IntPtr.Zero;
        IntPtr hBitmap = IntPtr.Zero;
        IntPtr oldBitmap = IntPtr.Zero;

        try
        {
            screenDc = NativeMethods.GetDC(IntPtr.Zero);
            memDc = CreateCompatibleDC(screenDc);
            hBitmap = bitmap.GetHbitmap(Color.FromArgb(0));
            oldBitmap = SelectObject(memDc, hBitmap);

            var ptDst = new POINT { X = x, Y = y };
            var ptSrc = new POINT { X = 0, Y = 0 };
            var size = new SIZE { Width = w, Height = h };
            var blend = new BLENDFUNCTION
            {
                BlendOp = AC_SRC_OVER,
                BlendFlags = 0,
                SourceConstantAlpha = 255,
                AlphaFormat = AC_SRC_ALPHA
            };

            UpdateLayeredWindow(Handle, screenDc, ref ptDst, ref size, memDc,
                ref ptSrc, 0, ref blend, ULW_ALPHA);

            // Immediately re-assert topmost after render
            SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }
        finally
        {
            if (oldBitmap != IntPtr.Zero) SelectObject(memDc, oldBitmap);
            if (hBitmap != IntPtr.Zero) DeleteObject(hBitmap);
            if (memDc != IntPtr.Zero) DeleteDC(memDc);
            if (screenDc != IntPtr.Zero) NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_NCHITTEST)
        {
            m.Result = (IntPtr)HTTRANSPARENT;
            return;
        }
        base.WndProc(ref m);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _topmostTimer.Stop();
            _topmostTimer.Dispose();
        }
        if (_winEventHook != IntPtr.Zero)
        {
            UnhookWinEvent(_winEventHook);
            _winEventHook = IntPtr.Zero;
        }
        base.Dispose(disposing);
    }
}

static class NativeMethods
{
    [DllImport("user32.dll")]
    public static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
}
