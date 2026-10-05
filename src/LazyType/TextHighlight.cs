using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace LazyType;

// Grammarly-style marker over the dictated text in an external field while a suggestion is open.
// The window is layered, click-through and never activates, so the editor underneath keeps working.
internal sealed class TextHighlight : Form
{
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("user32.dll")] private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr dst, ref Native.POINT position, ref SIZE size, IntPtr src, ref Native.POINT source, int key, ref BLENDFUNCTION blend, int flags);
    [StructLayout(LayoutKind.Sequential)] private struct SIZE { public int cx, cy; }
    [StructLayout(LayoutKind.Sequential, Pack = 1)] private struct BLENDFUNCTION { public byte op, flags, alpha, format; }

    private static readonly Color Accent = Color.FromArgb(127, 86, 217);
    private const int Pad = 7;
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 40 };
    private readonly Stopwatch clock = new();
    private TextTarget? target;
    private Rectangle[] lines = Array.Empty<Rectangle>();
    private IReadOnlyList<TextSpan>? spans;
    private bool working;
    private int ticks;

    public TextHighlight()
    {
        Text = "Lazy Type · Suggestion highlight";
        FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true;
        StartPosition = FormStartPosition.Manual;
        timer.Tick += (_, _) => Step();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get { var cp = base.CreateParams; cp.ExStyle |= 0x80000 | 0x08000000 | 0x80 | 0x20; return cp; } // LAYERED | NOACTIVATE | TOOLWINDOW | TRANSPARENT
    }

    // Pulses over the whole dictation while the suggestion is generated, then settles on just the words it changes.
    public void Present(TextTarget destination)
    {
        target = destination; working = true; ticks = 0; spans = null;
        lines = Array.Empty<Rectangle>();
        clock.Restart(); timer.Start();
        Step();
    }

    public void Settle(IReadOnlyList<TextSpan> changed)
    {
        if (target == null) return;
        if (changed.Count == 0) { Dismiss(); return; }
        spans = changed; working = false; ticks = 0;
        Step();
    }

    public void Dismiss()
    {
        timer.Stop(); target = null; working = false; spans = null;
        lines = Array.Empty<Rectangle>();
        Hide();
    }

    private void Step()
    {
        // Re-measure a few times per second so the marker follows scrolling, moves and edits.
        if (ticks++ % 3 == 0)
        {
            var next = target?.InsertionLineBounds(spans).Where(target.IsUncovered).ToArray() ?? Array.Empty<Rectangle>();
            var changed = !next.SequenceEqual(lines);
            lines = next;
            if (lines.Length == 0) { Hide(); return; }
            if (!changed && !working && Visible) return;
        }
        else if (!working || lines.Length == 0) return;
        Render();
    }

    private void Render()
    {
        if (lines.Length == 0 || IsDisposed) return;
        var area = lines.Aggregate(Rectangle.Union);
        area.Inflate(Pad, Pad);
        var wave = working ? 0.5 + 0.5 * Math.Sin(clock.Elapsed.TotalSeconds * Math.PI * 1.6) : 1;
        var fill = Color.FromArgb(working ? (int)(22 + 30 * wave) : 46, Accent);
        var underline = Color.FromArgb(working ? (int)(150 + 90 * wave) : 235, Accent);

        using var bitmap = new Bitmap(area.Width, area.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var fillBrush = new SolidBrush(fill);
            using var lineBrush = new SolidBrush(underline);
            foreach (var line in lines)
            {
                var box = line; box.Offset(-area.Left, -area.Top);
                // Keep a changed comma or full stop wide enough to notice.
                if (box.Width < 8) box.Inflate((9 - box.Width) / 2, 0);
                var thickness = Math.Max(2, (int)Math.Round(line.Height / 12f));
                DrawingHelpers.FillRoundedRectangle(g, fillBrush, Rectangle.Inflate(box, 2, 1), 3);
                DrawingHelpers.FillRoundedRectangle(g, lineBrush, new Rectangle(box.Left, box.Bottom, box.Width, thickness), Math.Max(1, thickness / 2));
            }
        }

        if (Bounds != area) Bounds = area;
        var screen = GetDC(IntPtr.Zero);
        var memory = CreateCompatibleDC(screen);
        var hBitmap = bitmap.GetHbitmap(Color.FromArgb(0));
        var previous = SelectObject(memory, hBitmap);
        try
        {
            var position = new Native.POINT { X = area.Left, Y = area.Top };
            var size = new SIZE { cx = area.Width, cy = area.Height };
            var source = new Native.POINT();
            var blend = new BLENDFUNCTION { op = 0, alpha = 255, format = 1 }; // AC_SRC_OVER, AC_SRC_ALPHA
            UpdateLayeredWindow(Handle, screen, ref position, ref size, memory, ref source, 0, ref blend, 2); // ULW_ALPHA
        }
        finally
        {
            SelectObject(memory, previous);
            Native.DeleteObject(hBitmap);
            DeleteDC(memory);
            ReleaseDC(IntPtr.Zero, screen);
        }
        if (!Visible) Show();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) timer.Dispose();
        base.Dispose(disposing);
    }
}
