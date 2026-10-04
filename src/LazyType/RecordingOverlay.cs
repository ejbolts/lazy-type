using System.Drawing.Drawing2D;

namespace LazyType;

internal sealed class RecordingOverlay : Form
{
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 33 };
    private readonly Stopwatch elapsed = new();
    private readonly Font titleFont = new("Segoe UI", 9f, FontStyle.Bold);
    private readonly Font hintFont = new("Segoe UI", 8f);
    private string message = "Listening";
    private string hint = "Hotkey stops · Esc cancels";
    private bool recording;
    private bool dark = true;
    private float smoothLevel;
    private Point? lastCursor;
    public Func<float>? AudioLevel { get; set; }
    public RecordingOverlay()
    {
        Text = "Lazy Type · Recording indicator";
        FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(28, 28, 28); ForeColor = Color.FromArgb(247, 247, 247);
        ClientSize = new Size(244, 62); DoubleBuffered = true; Opacity = 0.78;
        AutoScaleMode = AutoScaleMode.Dpi;
        UpdateShape();
        timer.Tick += (_, _) =>
        {
            FollowCursor();
            smoothLevel = smoothLevel * .6f + (AudioLevel?.Invoke() ?? 0) * .4f;
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
        if (!Visible)
        {
            lastCursor = null; smoothLevel = 0;
            FollowCursor();
            Show();
        }
        if (isRecording) elapsed.Restart();
        timer.Start(); Invalidate();
    }
    public void Dismiss() { timer.Stop(); Hide(); elapsed.Stop(); }
    public void PresentPreview()
    {
        Present("Popup preview");
        hint = "Preview · Microphone off";
    }
    private void FollowCursor()
    {
        var mouse = Cursor.Position;
        if (lastCursor == mouse) return;
        lastCursor = mouse;
        var area = Screen.FromPoint(mouse).WorkingArea;
        var scale = DeviceDpi / 96f;
        var gapX = (int)(18 * scale); var gapY = (int)(22 * scale);
        var margin = (int)(6 * scale);
        var x = mouse.X + gapX; var y = mouse.Y + gapY;
        if (x + Width > area.Right - margin) x = mouse.X - Width - gapX;
        if (y + Height > area.Bottom - margin) y = mouse.Y - Height - gapY;
        Location = new Point(
            Math.Clamp(x, area.Left + margin, Math.Max(area.Left + margin, area.Right - Width - margin)),
            Math.Clamp(y, area.Top + margin, Math.Max(area.Top + margin, area.Bottom - Height - margin)));
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
        using var path = RoundedRect(ClientRectangle, Math.Min(12 * DeviceDpi / 96f, Math.Min(Width, Height) / 2f));
        var previous = Region; Region = new Region(path); previous?.Dispose();
        lastCursor = null;
    }
    protected override void OnResize(EventArgs e) { base.OnResize(e); UpdateShape(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        var scale = DeviceDpi / 96f; g.ScaleTransform(scale, scale);
        var width = ClientSize.Width / scale; var height = ClientSize.Height / scale;
        var bounds = new RectangleF(.5f, .5f, width - 1, height - 1);
        using var outline = RoundedRect(bounds, 12);
        using var glass = new LinearGradientBrush(bounds, dark ? Color.FromArgb(48, 48, 48) : Color.White, dark ? Color.FromArgb(19, 19, 19) : Color.FromArgb(228, 233, 241), LinearGradientMode.Vertical);
        g.FillPath(glass, outline);
        using var border = new Pen(Color.FromArgb(110, 0, 0, 0), 1);
        g.DrawPath(border, outline);
        using var innerEdge = RoundedRect(new RectangleF(1.5f, 1.5f, width - 3, height - 3), 11);
        using var highlight = new Pen(Color.FromArgb(48, 255, 255, 255), 1);
        g.DrawPath(highlight, innerEdge);
        using var accent = new SolidBrush(recording ? (dark ? Color.FromArgb(92, 220, 157) : Color.FromArgb(18, 112, 73)) : (dark ? Color.FromArgb(195, 195, 195) : Color.FromArgb(82, 93, 111)));
        if (recording)
        {
            for (var i = 0; i < 5; i++)
            {
                var h = 4 + smoothLevel * (10 + 9 * (float)Math.Abs(Math.Sin(elapsed.Elapsed.TotalSeconds * 9 + i)));
                g.FillRectangle(accent, 14 + i * 4, 22 - h / 2, 2, h);
            }
        }
        else g.FillEllipse(accent, 19, 17, 9, 9);
        using var textFormat = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
        using var ink = new SolidBrush(ForeColor);
        g.DrawString(message, titleFont, ink, new RectangleF(43, 13, width - (recording ? 94 : 54), 20), textFormat);
        using var muted = new SolidBrush(dark ? Color.FromArgb(210, 210, 210) : Color.FromArgb(64, 76, 95));
        g.DrawString(hint, hintFont, muted, 13, 39);
        if (recording) g.DrawString(elapsed.Elapsed.ToString(@"mm\:ss"), hintFont, muted, width - 46, 14);
        base.OnPaint(e);
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { timer.Dispose(); titleFont.Dispose(); hintFont.Dispose(); }
        base.Dispose(disposing);
    }
}
