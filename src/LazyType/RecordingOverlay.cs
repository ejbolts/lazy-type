using System.Drawing.Drawing2D;

namespace LazyType;

internal sealed class RecordingOverlay : Form
{
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 33 };
    private readonly Stopwatch elapsed = new();
    private string message = "Listening";
    private string hint = "Press hotkey to stop · Esc to cancel";
    private bool recording;
    private float smoothLevel;
    public Func<float>? AudioLevel { get; set; }
    public RecordingOverlay()
    {
        Text = "Lazy Type · Recording indicator";
        FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(24, 31, 44); ForeColor = Color.White;
        ClientSize = new Size(302, 80); DoubleBuffered = true; Opacity = 0.96;
        AutoScaleMode = AutoScaleMode.Dpi;
        timer.Tick += (_, _) => { smoothLevel = smoothLevel * .6f + (AudioLevel?.Invoke() ?? 0) * .4f; Invalidate(); };
    }
    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    {
        get { var cp = base.CreateParams; cp.ExStyle |= 0x08000000 | 0x80 | 0x20; return cp; }
    }
    public void Present(string text, bool isRecording = false)
    {
        message = text; recording = isRecording;
        hint = isRecording ? "Press hotkey to stop · Esc to cancel" : "Microphone off · Esc to cancel";
        if (!Visible)
        {
            var mouse = Cursor.Position; var area = Screen.FromPoint(mouse).WorkingArea;
            Location = new Point(Math.Clamp(mouse.X + 22, area.Left, Math.Max(area.Left, area.Right - Width)), Math.Clamp(mouse.Y + 26, area.Top, Math.Max(area.Top, area.Bottom - Height)));
            Show();
        }
        if (isRecording) elapsed.Restart();
        timer.Start(); Invalidate();
    }
    public void Dismiss() { timer.Stop(); Hide(); elapsed.Stop(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        var scale = DeviceDpi / 96f; g.ScaleTransform(scale, scale);
        using var accent = new SolidBrush(recording ? Color.FromArgb(88, 227, 174) : Color.FromArgb(135, 166, 255));
        if (recording)
        {
            for (var i = 0; i < 5; i++)
            {
                var h = 5 + smoothLevel * (16 + 14 * (float)Math.Abs(Math.Sin(elapsed.Elapsed.TotalSeconds * 9 + i)));
                g.FillRectangle(accent, 18 + i * 5, 31 - h / 2, 3, h);
            }
        }
        else g.FillEllipse(accent, 24, 24, 12, 12);
        using var titleFont = new Font("Segoe UI", 10.5f, FontStyle.Bold);
        using var hintFont = new Font("Segoe UI", 8.5f);
        g.DrawString(message, titleFont, Brushes.White, 55, 17);
        using var muted = new SolidBrush(Color.FromArgb(177, 189, 207));
        g.DrawString(hint, hintFont, muted, 18, 53);
        if (recording) g.DrawString(elapsed.Elapsed.ToString(@"mm\:ss"), hintFont, muted, 249, 21);
        base.OnPaint(e);
    }
    protected override void Dispose(bool disposing) { if (disposing) timer.Dispose(); base.Dispose(disposing); }
}
