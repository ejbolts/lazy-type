using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace LazyType;

internal sealed class RecordingOverlay : Form
{
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 8 };
    private readonly Stopwatch elapsed = new();
    private readonly Font titleFont = new("Segoe UI", 9f, FontStyle.Bold);
    private readonly Font hintFont = new("Segoe UI", 8f);
    private static readonly float[] BarWeights = { 0.65f, 0.90f, 1.00f, 0.90f, 0.65f };
    private static readonly float[] BarFreqs   = { 14f,   19f,   24f,   18f,   15f };
    private static readonly float[] BarPhases  = { 0.4f,  1.9f,  3.5f,  5.1f,  1.2f };
    private readonly StringFormat textFormat = new() { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };

    private string message = "Listening";
    private string hint = "Hotkey stops · Esc cancels";
    private bool recording;
    private bool dark = true;
    private float smoothLevel;
    private Point lastCursor = new(-9999, -9999);
    private Point lastWindowPos = new(-9999, -9999);
    private Rectangle cachedArea;
    private Native.HookProc? mouseHookProc;
    private IntPtr mouseHookHandle;
    private bool timePeriodActive;

    public Func<float>? AudioLevel { get; set; }

    public RecordingOverlay()
    {
        Text = "Lazy Type · Recording indicator";
        FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(28, 28, 28); ForeColor = Color.FromArgb(247, 247, 247);
        ClientSize = new Size(190, 56); DoubleBuffered = true; Opacity = 0.78;
        AutoScaleMode = AutoScaleMode.Dpi;
        UpdateShape();
        timer.Tick += (_, _) =>
        {
            var target = AudioLevel?.Invoke() ?? 0f;
            if (target > smoothLevel)
            {
                smoothLevel = Math.Min(1f, smoothLevel * 0.12f + target * 0.88f);
            }
            else
            {
                smoothLevel = Math.Max(0f, smoothLevel * 0.72f + target * 0.28f);
            }
            Invalidate();
        };
    }

    protected override bool ShowWithoutActivation => true;

    public void ApplyTheme(bool isDark)
    {
        dark = isDark;
        BackColor = dark ? Color.FromArgb(28, 28, 28) : Color.FromArgb(248, 249, 252);
        ForeColor = dark ? Color.FromArgb(247, 247, 247) : Color.FromArgb(28, 34, 45);
        Invalidate();
    }

    protected override CreateParams CreateParams
    {
        get { var cp = base.CreateParams; cp.ExStyle |= 0x08000000 | 0x80 | 0x20; return cp; }
    }

    public void Present(string text, bool isRecording = false)
    {
        message = text; recording = isRecording;
        hint = isRecording ? "Hotkey stops · Esc cancels" : "Microphone off · Esc cancels";
        if (!timePeriodActive) { Native.TimeBeginPeriod(1); timePeriodActive = true; }
        EnsureMouseHook(true);
        var mouse = Cursor.Position;
        FollowCursor(mouse.X, mouse.Y);
        if (!Visible) Show();
        if (isRecording) elapsed.Restart();
        timer.Start(); Invalidate();
    }

    public void Dismiss()
    {
        timer.Stop();
        EnsureMouseHook(false);
        if (timePeriodActive) { Native.TimeEndPeriod(1); timePeriodActive = false; }
        Hide();
        elapsed.Stop();
    }

    public void PresentPreview()
    {
        Present("Popup preview");
        hint = "Preview · Microphone off";
    }

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam == (IntPtr)Native.WM_MOUSEMOVE && Visible)
        {
            var hookInfo = Marshal.PtrToStructure<Native.MSLLHOOKSTRUCT>(lParam);
            FollowCursor(hookInfo.pt.X, hookInfo.pt.Y);
        }
        return Native.CallNextHookEx(mouseHookHandle, nCode, wParam, lParam);
    }

    private void EnsureMouseHook(bool enable)
    {
        if (enable)
        {
            if (mouseHookHandle == IntPtr.Zero)
            {
                mouseHookProc = MouseHookCallback;
                mouseHookHandle = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, mouseHookProc, Native.GetModuleHandle(null), 0);
            }
        }
        else
        {
            if (mouseHookHandle != IntPtr.Zero)
            {
                Native.UnhookWindowsHookEx(mouseHookHandle);
                mouseHookHandle = IntPtr.Zero;
                mouseHookProc = null;
            }
        }
    }

    private void FollowCursor(int mouseX, int mouseY)
    {
        if (lastCursor.X == mouseX && lastCursor.Y == mouseY) return;
        lastCursor = new Point(mouseX, mouseY);
        if (!cachedArea.Contains(mouseX, mouseY))
            cachedArea = Screen.FromPoint(new Point(mouseX, mouseY)).WorkingArea;
        var area = cachedArea;
        var scale = DeviceDpi / 96f;
        var gapX = (int)(10 * scale); var gapY = (int)(14 * scale);
        var margin = (int)(6 * scale);
        var x = mouseX + gapX; var y = mouseY + gapY;
        if (x + Width > area.Right - margin) x = mouseX - Width - gapX;
        if (y + Height > area.Bottom - margin) y = mouseY - Height - gapY;
        var finalX = Math.Clamp(x, area.Left + margin, Math.Max(area.Left + margin, area.Right - Width - margin));
        var finalY = Math.Clamp(y, area.Top + margin, Math.Max(area.Top + margin, area.Bottom - Height - margin));
        if (lastWindowPos.X != finalX || lastWindowPos.Y != finalY)
        {
            lastWindowPos = new Point(finalX, finalY);
            if (IsHandleCreated)
            {
                Native.SetWindowPos(Handle, IntPtr.Zero, finalX, finalY, 0, 0,
                    Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_ASYNCWINDOWPOS);
            }
            else Location = lastWindowPos;
        }
    }

    private static GraphicsPath RoundedRect(RectangleF rect, float radius)
    {
        var path = new GraphicsPath(); var diameter = radius * 2;
        path.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure(); return path;
    }

    private void UpdateShape()
    {
        if (ClientSize.Width < 1 || ClientSize.Height < 1) return;
        var scale = DeviceDpi / 96f;
        using var clipPath = RoundedRect(ClientRectangle, Math.Min(10 * scale, Math.Min(Width, Height) / 2f));
        var previous = Region; Region = new Region(clipPath); previous?.Dispose();
        lastCursor = new Point(-9999, -9999);
        lastWindowPos = new Point(-9999, -9999);
    }

    protected override void OnResize(EventArgs e) { base.OnResize(e); UpdateShape(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        var scale = DeviceDpi / 96f; g.ScaleTransform(scale, scale);
        var width = ClientSize.Width / scale; var height = ClientSize.Height / scale;
        var bounds = new RectangleF(.5f, .5f, width - 1, height - 1);
        using var outline = RoundedRect(bounds, 10);
        using var glass = new LinearGradientBrush(bounds, dark ? Color.FromArgb(48, 48, 48) : Color.White, dark ? Color.FromArgb(19, 19, 19) : Color.FromArgb(228, 233, 241), LinearGradientMode.Vertical);
        g.FillPath(glass, outline);
        using var border = new Pen(Color.FromArgb(110, 0, 0, 0), 1);
        g.DrawPath(border, outline);
        using var innerEdge = RoundedRect(new RectangleF(1.5f, 1.5f, width - 3, height - 3), 9);
        using var highlight = new Pen(Color.FromArgb(48, 255, 255, 255), 1);
        g.DrawPath(highlight, innerEdge);

        var accentColor = recording
            ? (dark ? Color.FromArgb(92, 220, 157) : Color.FromArgb(18, 112, 73))
            : (dark ? Color.FromArgb(195, 195, 195) : Color.FromArgb(82, 93, 111));

        if (recording)
        {
            using var barPen = new Pen(accentColor, 2.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            var t = elapsed.Elapsed.TotalSeconds;
            for (var i = 0; i < 5; i++)
            {
                var wave = 0.20f + 0.80f * (float)Math.Abs(Math.Sin(t * BarFreqs[i] + BarPhases[i]));
                var h = 3.0f + smoothLevel * 21.0f * BarWeights[i] * wave;
                var half = Math.Max(0.2f, (h - 2.4f) / 2f);
                var x = 12f + i * 4.4f;
                g.DrawLine(barPen, x, 19f - half, x, 19f + half);
            }
        }
        else
        {
            using var idleAccent = new SolidBrush(accentColor);
            g.FillEllipse(idleAccent, 15f, 14.5f, 9f, 9f);
        }

        using var ink = new SolidBrush(ForeColor);
        var titleX = recording ? 38f : 32f;
        var titleMaxW = recording ? (width - 38f - 44f) : (width - 32f - 12f);
        g.DrawString(message, titleFont, ink, new RectangleF(titleX, 10f, titleMaxW, 18f), textFormat);

        using var muted = new SolidBrush(dark ? Color.FromArgb(210, 210, 210) : Color.FromArgb(64, 76, 95));
        g.DrawString(hint, hintFont, muted, 12f, 33.5f);
        if (recording) g.DrawString(elapsed.Elapsed.ToString(@"mm\:ss"), hintFont, muted, width - 42f, 11.5f);

        base.OnPaint(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            EnsureMouseHook(false);
            if (timePeriodActive) { Native.TimeEndPeriod(1); timePeriodActive = false; }
            timer.Dispose();
            titleFont.Dispose();
            hintFont.Dispose();
            textFormat.Dispose();
        }
        base.Dispose(disposing);
    }
}
