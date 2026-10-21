// TaskbarNetSpeed - tiny live upload/download speed overlay for the Windows 11 taskbar.
// Build: run build.bat (no SDK needed; uses the C# compiler that ships with Windows).
// Right-click the text for options. Left-drag to move it along the taskbar.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

static class Program
{
    [STAThread]
    static void Main()
    {
        bool created;
        using (var mutex = new Mutex(true, "TaskbarNetSpeed_SingleInstance", out created))
        {
            if (!created) return;
            Native.SetProcessDPIAware();
            Application.EnableVisualStyles();
            Application.Run(new SpeedForm());
        }
    }
}

class SpeedForm : Form
{
    static readonly string[] UnitNames = { "Auto (KB/s, MB/s)", "KB/s", "MB/s", "Auto (Kbps, Mbps)", "Kbps", "Mbps" };
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string AppName = "TaskbarNetSpeed";

    readonly string settingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppName, "settings.txt");

    readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
    readonly Stopwatch clock = new Stopwatch();
    List<NetworkInterface> nics = new List<NetworkInterface>();
    volatile bool nicsDirty = true;
    long lastRx = -1, lastTx = -1;
    double rxRate, txRate;

    int unit;               // index into UnitNames
    int rightOffset = -1;   // distance from taskbar's right edge to our right edge; -1 = auto (next to tray)
    float scale = 1f;
    Font font;
    Color textColor = Color.White;
    int winX, winY, winW, winH;
    bool hiddenForFullscreen;

    bool dragging;
    int dragStartCursor, dragStartX;

    ContextMenuStrip menu;
    ToolStripMenuItem startupItem;

    public SpeedForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Text = AppName;

        using (var g = Graphics.FromHwnd(IntPtr.Zero)) scale = g.DpiX / 96f;
        font = new Font("Segoe UI", 12f * scale, FontStyle.Regular, GraphicsUnit.Pixel);

        LoadSettings();
        UpdateThemeColor();
        BuildMenu();
        MeasureWidth();

        NetworkChange.NetworkAddressChanged += delegate { nicsDirty = true; };
        NetworkChange.NetworkAvailabilityChanged += delegate { nicsDirty = true; };
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

        timer.Interval = 1000;
        timer.Tick += delegate { Tick(); };
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            // layered (per-pixel alpha) | toolwindow (no Alt-Tab) | topmost | never steal focus
            cp.ExStyle |= 0x00080000 | 0x00000080 | 0x00000008 | 0x08000000;
            return cp;
        }
    }

    protected override bool ShowWithoutActivation { get { return true; } }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // Being owned by the taskbar keeps us above it even after the taskbar is clicked.
        IntPtr taskbar = Native.FindWindow("Shell_TrayWnd", null);
        if (taskbar != IntPtr.Zero) Native.SetWindowLongPtr(Handle, -8 /*GWLP_HWNDPARENT*/, taskbar);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Sample();
        Render();
        clock.Start();
        timer.Start();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        base.OnFormClosed(e);
    }

    // ---------- sampling ----------

    void RefreshInterfaces()
    {
        nicsDirty = false;
        var all = NetworkInterface.GetAllNetworkInterfaces();
        var withGateway = new List<NetworkInterface>();
        var fallback = new List<NetworkInterface>();
        foreach (var n in all)
        {
            if (n.OperationalStatus != OperationalStatus.Up) continue;
            if (n.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                n.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
            fallback.Add(n);
            try
            {
                // Adapters with a gateway carry internet traffic; this skips virtual switches (Hyper-V/WSL)
                // that would otherwise double-count the same bytes.
                if (n.GetIPProperties().GatewayAddresses.Count > 0) withGateway.Add(n);
            }
            catch { }
        }
        nics = withGateway.Count > 0 ? withGateway : fallback;
        lastRx = lastTx = -1; // totals change with the interface set; re-baseline
    }

    void Sample()
    {
        if (nicsDirty) RefreshInterfaces();

        long rx = 0, tx = 0;
        foreach (var n in nics)
        {
            try
            {
                var s = n.GetIPStatistics();
                rx += s.BytesReceived;
                tx += s.BytesSent;
            }
            catch { nicsDirty = true; }
        }

        double secs = clock.Elapsed.TotalSeconds;
        clock.Restart();
        if (lastRx >= 0 && secs > 0.05)
        {
            rxRate = Math.Max(0, rx - lastRx) / secs;
            txRate = Math.Max(0, tx - lastTx) / secs;
        }
        lastRx = rx;
        lastTx = tx;
    }

    void Tick()
    {
        Sample();

        int state;
        bool fullscreen = Native.SHQueryUserNotificationState(out state) == 0 && (state == 2 || state == 3 || state == 4);
        if (fullscreen != hiddenForFullscreen)
        {
            hiddenForFullscreen = fullscreen;
            Native.ShowWindow(Handle, fullscreen ? 0 /*SW_HIDE*/ : 4 /*SW_SHOWNOACTIVATE*/);
        }
        if (fullscreen) return;

        Native.SetWindowPos(Handle, (IntPtr)(-1) /*HWND_TOPMOST*/, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 /*NOSIZE|NOMOVE|NOACTIVATE*/);
        Render();
    }

    // ---------- formatting ----------

    void Format(double bytesPerSec, out string num, out string u)
    {
        double v;
        switch (unit)
        {
            case 0:
                v = bytesPerSec / 1024; u = "KB/s";
                if (v >= 1000) { v /= 1024; u = "MB/s"; if (v >= 1000) { v /= 1024; u = "GB/s"; } }
                break;
            case 1: v = bytesPerSec / 1024; u = "KB/s"; break;
            case 2: v = bytesPerSec / (1024 * 1024); u = "MB/s"; break;
            case 3:
                v = bytesPerSec * 8 / 1000; u = "Kbps";
                if (v >= 1000) { v /= 1000; u = "Mbps"; if (v >= 1000) { v /= 1000; u = "Gbps"; } }
                break;
            case 4: v = bytesPerSec * 8 / 1000; u = "Kbps"; break;
            default: v = bytesPerSec * 8 / 1000000; u = "Mbps"; break;
        }
        num = v < 10 ? v.ToString("0.00") : v < 100 ? v.ToString("0.0") : v.ToString("0");
    }

    // Layout columns: [pad][arrow][gap][number, right-aligned][space][unit][pad]
    float padW, arrowW, gapW, numW, spaceW, unitW;

    void MeasureWidth()
    {
        // Fixed column widths so the overlay doesn't jitter as numbers change.
        bool auto = unit == 0 || unit == 3;
        using (var bmp = new Bitmap(1, 1))
        using (var g = Graphics.FromImage(bmp))
        {
            var fmt = StringFormat.GenericTypographic;
            padW = 6 * scale;
            gapW = 5 * scale;
            spaceW = 3 * scale;
            arrowW = g.MeasureString("↓", font, 0, fmt).Width;
            // Widest number each unit can reach on a ~1 Gbps link.
            string[] widest = { "8.88", "888888", "8888", "8.88", "8888888", "88888" };
            numW = g.MeasureString(widest[unit], font, 0, fmt).Width;
            if (auto) numW = Math.Max(numW, g.MeasureString("888", font, 0, fmt).Width);
            unitW = 0;
            foreach (var u in new[] { "KB/s", "MB/s", "GB/s", "Kbps", "Mbps", "Gbps" })
                unitW = Math.Max(unitW, g.MeasureString(u, font, 0, fmt).Width);
            winW = (int)Math.Ceiling(padW * 2 + arrowW + gapW + numW + spaceW + unitW);
        }
    }

    void DrawRow(Graphics g, Brush brush, string arrow, double rate, float y)
    {
        string num, u;
        Format(rate, out num, out u);
        var fmt = StringFormat.GenericTypographic;
        float x = padW;
        g.DrawString(arrow, font, brush, x, y, fmt);
        x += arrowW + gapW;
        float nw = g.MeasureString(num, font, 0, fmt).Width;
        g.DrawString(num, font, brush, x + numW - nw, y, fmt);
        x += numW + spaceW;
        g.DrawString(u, font, brush, x, y, fmt);
    }

    // ---------- positioning & drawing ----------

    Native.RECT TaskbarRect()
    {
        Native.RECT r;
        IntPtr tb = Native.FindWindow("Shell_TrayWnd", null);
        if (tb == IntPtr.Zero || !Native.GetWindowRect(tb, out r))
        {
            var wa = Screen.PrimaryScreen.Bounds;
            r = new Native.RECT { Left = wa.Left, Right = wa.Right, Top = wa.Bottom - (int)(48 * scale), Bottom = wa.Bottom };
        }
        return r;
    }

    void Reposition()
    {
        var tb = TaskbarRect();
        winH = tb.Bottom - tb.Top;
        winY = tb.Top;

        int x;
        if (rightOffset >= 0)
        {
            x = tb.Right - rightOffset - winW;
        }
        else
        {
            x = tb.Right - winW - (int)(330 * scale);
            IntPtr tray = Native.FindWindowEx(Native.FindWindow("Shell_TrayWnd", null), IntPtr.Zero, "TrayNotifyWnd", null);
            Native.RECT tr;
            if (tray != IntPtr.Zero && Native.GetWindowRect(tray, out tr) && tr.Left > tb.Left + winW)
                x = tr.Left - winW - (int)(4 * scale);
        }
        winX = Math.Max(tb.Left, Math.Min(x, tb.Right - winW));
    }

    void Render()
    {
        if (!dragging) Reposition();
        if (winW <= 0 || winH <= 0) return;

        using (var bmp = new Bitmap(winW, winH, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
        {
            using (var g = Graphics.FromImage(bmp))
            using (var brush = new SolidBrush(textColor))
            {
                // Alpha 1: invisible to the eye but still catches mouse clicks.
                g.Clear(Color.FromArgb(1, 0, 0, 0));
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

                float lineH = font.GetHeight(g);
                float top = (winH - lineH * 2) / 2f;
                DrawRow(g, brush, "↑", txRate, top);
                DrawRow(g, brush, "↓", rxRate, top + lineH);
            }
            Native.PushBitmap(Handle, bmp, winX, winY);
        }
    }

    // ---------- mouse ----------

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left)
        {
            dragging = true;
            dragStartCursor = Cursor.Position.X;
            dragStartX = winX;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!dragging) return;
        var tb = TaskbarRect();
        winX = Math.Max(tb.Left, Math.Min(dragStartX + Cursor.Position.X - dragStartCursor, tb.Right - winW));
        Render();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Left && dragging)
        {
            dragging = false;
            rightOffset = TaskbarRect().Right - (winX + winW);
            SaveSettings();
        }
        else if (e.Button == MouseButtons.Right)
        {
            startupItem.Checked = IsStartupEnabled();
            menu.Show(Cursor.Position);
        }
    }

    // ---------- menu ----------

    void BuildMenu()
    {
        menu = new ContextMenuStrip();
        var unitMenu = new ToolStripMenuItem("Speed unit");
        for (int i = 0; i < UnitNames.Length; i++)
        {
            int idx = i;
            var item = new ToolStripMenuItem(UnitNames[i]) { Checked = i == unit };
            item.Click += delegate
            {
                unit = idx;
                foreach (ToolStripItem it in unitMenu.DropDownItems)
                {
                    var mi = it as ToolStripMenuItem;
                    if (mi != null) mi.Checked = false;
                }
                item.Checked = true;
                SaveSettings();
                MeasureWidth();
                Render();
            };
            unitMenu.DropDownItems.Add(item);
            if (i == 2) unitMenu.DropDownItems.Add(new ToolStripSeparator());
        }
        menu.Items.Add(unitMenu);

        startupItem = new ToolStripMenuItem("Start with Windows");
        startupItem.Click += delegate { SetStartup(!IsStartupEnabled()); };
        menu.Items.Add(startupItem);

        var reset = new ToolStripMenuItem("Reset position");
        reset.Click += delegate { rightOffset = -1; SaveSettings(); Render(); };
        menu.Items.Add(reset);

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem(AppName + " v" + Application.ProductVersion) { Enabled = false });
        var exit = new ToolStripMenuItem("Exit");
        exit.Click += delegate { Close(); };
        menu.Items.Add(exit);
    }

    bool IsStartupEnabled()
    {
        using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
            return k != null && k.GetValue(AppName) != null;
    }

    void SetStartup(bool on)
    {
        using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
        {
            if (on) k.SetValue(AppName, "\"" + Application.ExecutablePath + "\"");
            else k.DeleteValue(AppName, false);
        }
    }

    // ---------- theme & settings ----------

    void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        UpdateThemeColor();
        Render();
    }

    void UpdateThemeColor()
    {
        object v = null;
        try
        {
            using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                if (k != null) v = k.GetValue("SystemUsesLightTheme");
        }
        catch { }
        bool light = v is int && (int)v == 1;
        textColor = light ? Color.Black : Color.White;
    }

    void LoadSettings()
    {
        try
        {
            if (!File.Exists(settingsPath)) return;
            foreach (var line in File.ReadAllLines(settingsPath))
            {
                var parts = line.Split('=');
                if (parts.Length != 2) continue;
                int n;
                if (!int.TryParse(parts[1].Trim(), out n)) continue;
                if (parts[0].Trim() == "unit" && n >= 0 && n < UnitNames.Length) unit = n;
                if (parts[0].Trim() == "offset") rightOffset = n;
            }
        }
        catch { }
    }

    void SaveSettings()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(settingsPath));
            File.WriteAllText(settingsPath, "unit=" + unit + "\r\noffset=" + rightOffset + "\r\n");
        }
        catch { }
    }
}

static class Native
{
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; public POINT(int x, int y) { X = x; Y = y; } }
    [StructLayout(LayoutKind.Sequential)] struct SIZE { public int W, H; public SIZE(int w, int h) { W = w; H = h; } }
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }

    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int cmd);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int idx, IntPtr val);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] static extern int SetWindowLong32(IntPtr hWnd, int idx, int val);
    [DllImport("shell32.dll")] public static extern int SHQueryUserNotificationState(out int state);

    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hWnd, IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    [DllImport("user32.dll")]
    static extern bool UpdateLayeredWindow(IntPtr hWnd, IntPtr dst, ref POINT pos, ref SIZE size, IntPtr src,
                                           ref POINT srcPos, int key, ref BLENDFUNCTION blend, int flags);

    public static void SetWindowLongPtr(IntPtr hWnd, int idx, IntPtr val)
    {
        if (IntPtr.Size == 8) SetWindowLongPtr64(hWnd, idx, val);
        else SetWindowLong32(hWnd, idx, val.ToInt32());
    }

    public static void PushBitmap(IntPtr hWnd, Bitmap bmp, int x, int y)
    {
        IntPtr screen = GetDC(IntPtr.Zero);
        IntPtr mem = CreateCompatibleDC(screen);
        IntPtr hbmp = bmp.GetHbitmap(Color.FromArgb(0));
        IntPtr old = SelectObject(mem, hbmp);
        try
        {
            var pos = new POINT(x, y);
            var size = new SIZE(bmp.Width, bmp.Height);
            var src = new POINT(0, 0);
            var blend = new BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
            UpdateLayeredWindow(hWnd, screen, ref pos, ref size, mem, ref src, 0, ref blend, 2 /*ULW_ALPHA*/);
        }
        finally
        {
            SelectObject(mem, old);
            DeleteObject(hbmp);
            DeleteDC(mem);
            ReleaseDC(IntPtr.Zero, screen);
        }
    }
}
