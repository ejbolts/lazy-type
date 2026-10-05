using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace LazyType;

// Grammarly-style marks over the dictated text in an external field while a suggestion is open: a pulsing
// highlight while it is generated, then removed words struck through and added words labelled in place.
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
    private static readonly Color Removed = Color.FromArgb(220, 38, 38);
    private static readonly Color Added = Color.FromArgb(22, 163, 74);
    private static readonly Color AddedText = Color.FromArgb(20, 108, 52);
    private static readonly Color AddedFill = Color.FromArgb(220, 252, 231);
    private const int Pad = 7;
    private const int MaxLabelWidth = 520;
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 40 };
    private readonly Stopwatch clock = new();
    private TextTarget? target;
    private Rectangle[] lines = Array.Empty<Rectangle>();
    private IReadOnlyList<ChangeMark>? marks;
    private IReadOnlyList<TextSpan>? spans;
    private Rectangle[][] markLines = Array.Empty<Rectangle[]>();
    private Rectangle[] textLines = Array.Empty<Rectangle>();
    private string layout = string.Empty;
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

    // Pulses over the whole dictation while the suggestion is generated.
    public void Present(TextTarget destination)
    {
        target = destination; working = true; ticks = 0;
        marks = null; spans = null; layout = string.Empty;
        clock.Restart(); timer.Start();
        Step();
    }

    // Shows the edits in the field. Returns false when the editor cannot locate every edit, so the
    // preview can show the full comparison instead.
    public bool Settle(IReadOnlyList<ChangeMark> changes)
    {
        if (target == null) return false;
        if (changes.Count == 0) { Dismiss(); return true; }
        marks = changes; spans = changes.Select(mark => mark.Span).ToArray();
        working = false; ticks = 0; layout = string.Empty;
        var located = target.SpanBounds(spans);
        Step();
        return located != null && located.All(bounds => bounds.Length > 0);
    }

    public void Dismiss()
    {
        timer.Stop(); target = null; working = false;
        marks = null; spans = null; layout = string.Empty;
        lines = Array.Empty<Rectangle>(); markLines = Array.Empty<Rectangle[]>(); textLines = Array.Empty<Rectangle>();
        Hide();
    }

    private void Step()
    {
        // Re-measure a few times per second so the marks follow scrolling, moves and edits.
        if (ticks++ % 3 == 0)
        {
            if (spans == null) lines = target?.InsertionLineBounds().Where(target.IsUncovered).ToArray() ?? Array.Empty<Rectangle>();
            else
            {
                markLines = target?.SpanBounds(spans)?.Select(bounds => bounds.Where(target.IsUncovered).ToArray()).ToArray() ?? Array.Empty<Rectangle[]>();
                textLines = target?.TextLineBounds() ?? Array.Empty<Rectangle>();
            }
            var next = string.Join(";", (spans == null ? lines : markLines.SelectMany(bounds => bounds.Append(Rectangle.Empty))).Select(r => $"{r.X},{r.Y},{r.Width},{r.Height}"));
            var changed = next != layout;
            layout = next;
            if (spans == null ? lines.Length == 0 : markLines.All(bounds => bounds.Length == 0)) { Hide(); return; }
            if (!changed && !working && Visible) return;
        }
        else if (!working || lines.Length == 0) return;
        Render();
    }

    private void Render()
    {
        if (IsDisposed) return;
        var labels = spans == null ? new List<Label>() : PlaceLabels();
        try
        {
            var shapes = (spans == null ? lines : markLines.SelectMany(bounds => bounds)).Concat(labels.Select(label => label.Box)).ToArray();
            if (shapes.Length == 0) return;
            var area = shapes.Aggregate(Rectangle.Union);
            area.Inflate(Pad, Pad);

            using var bitmap = new Bitmap(area.Width, area.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.Clear(Color.Transparent);
                g.TranslateTransform(-area.Left, -area.Top);
                if (spans == null) DrawPulse(g);
                else DrawChanges(g, labels);
            }
            Blit(bitmap, area);
        }
        finally
        {
            foreach (var label in labels) label.Font.Dispose();
        }
    }

    private void Blit(Bitmap bitmap, Rectangle area)
    {
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

    private void DrawPulse(Graphics g)
    {
        var wave = 0.5 + 0.5 * Math.Sin(clock.Elapsed.TotalSeconds * Math.PI * 1.6);
        using var fill = new SolidBrush(Color.FromArgb((int)(22 + 30 * wave), Accent));
        using var underline = new SolidBrush(Color.FromArgb((int)(150 + 90 * wave), Accent));
        foreach (var line in lines)
        {
            var thickness = Stroke(line);
            DrawingHelpers.FillRoundedRectangle(g, fill, Rectangle.Inflate(line, 2, 1), 3);
            DrawingHelpers.FillRoundedRectangle(g, underline, new Rectangle(line.Left, line.Bottom, line.Width, thickness), Math.Max(1, thickness / 2));
        }
    }

    private void DrawChanges(Graphics g, List<Label> labels)
    {
        using var tint = new SolidBrush(Color.FromArgb(30, Removed));
        using var strike = new SolidBrush(Color.FromArgb(235, Removed));
        using var caret = new SolidBrush(Added);
        for (var i = 0; i < marks!.Count && i < markLines.Length; i++)
        {
            var bounds = markLines[i];
            if (bounds.Length == 0) continue;
            if (marks[i].Struck)
            {
                foreach (var line in bounds)
                {
                    // Keep a struck comma or full stop wide enough to notice.
                    var box = line.Width < 8 ? Rectangle.Inflate(line, (9 - line.Width) / 2, 0) : line;
                    var thickness = Stroke(line);
                    DrawingHelpers.FillRoundedRectangle(g, tint, Rectangle.Inflate(box, 1, 0), 3);
                    g.FillRectangle(strike, box.Left, box.Top + (int)(box.Height * 0.55) - thickness / 2, box.Width, thickness);
                }
            }
            else
            {
                var anchor = marks[i].Before ? bounds[0] : bounds[^1];
                var x = marks[i].Before ? anchor.Left - 2 : anchor.Right + 1;
                g.FillRectangle(caret, x, anchor.Top + 2, 2, anchor.Height - 3);
            }
        }

        using var shadow = new SolidBrush(Color.FromArgb(45, 0, 0, 0));
        using var fill = new SolidBrush(AddedFill);
        using var border = new Pen(Color.FromArgb(200, Added), 1f);
        using var text = new SolidBrush(AddedText);
        using var underline = new SolidBrush(Added);
        using var format = new StringFormat(StringFormat.GenericTypographic) { Alignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
        foreach (var label in labels)
        {
            DrawingHelpers.FillRoundedRectangle(g, shadow, new Rectangle(label.Box.X + 1, label.Box.Y + 2, label.Box.Width, label.Box.Height), 5);
            DrawingHelpers.FillRoundedRectangle(g, fill, label.Box, 5);
            DrawingHelpers.DrawRoundedRectangle(g, border, label.Box, 5);
            var textBox = new RectangleF(label.Box.X + 6, label.Box.Y + 2, label.Box.Width - 12, label.Font.GetHeight(g));
            g.DrawString(label.Text, label.Font, text, textBox, format);
            // An underline under a lone comma reads as an underscore, so only words are underlined.
            if (label.Text.Any(char.IsLetterOrDigit)) g.FillRectangle(underline, textBox.X, textBox.Bottom, textBox.Width, Math.Max(1.5f, label.Font.Size / 9));
        }
    }

    // Labels for added words sit just above where they go, or just below when that covers less of the
    // neighbouring lines, and are nudged right so labels never overlap each other.
    private List<Label> PlaceLabels()
    {
        var labels = new List<Label>();
        using var measure = Graphics.FromHwnd(IntPtr.Zero);
        for (var i = 0; i < marks!.Count && i < markLines.Length; i++)
        {
            if (string.IsNullOrEmpty(marks[i].Added) || markLines[i].Length == 0) continue;
            var anchor = marks[i].Struck || marks[i].Before ? markLines[i][0] : markLines[i][^1];
            // Punctuation alone is drawn larger so a new comma or full stop is legible.
            var words = marks[i].Added!.Any(char.IsLetterOrDigit);
            var font = new Font("Segoe UI", Math.Clamp(anchor.Height * (words ? 0.5f : 0.62f), 11f, 20f), FontStyle.Bold, GraphicsUnit.Pixel);
            var size = measure.MeasureString(marks[i].Added, font, PointF.Empty, StringFormat.GenericTypographic);
            var height = (int)Math.Ceiling(font.GetHeight(measure)) + 7;
            var width = Math.Clamp((int)Math.Ceiling(size.Width) + 13, height, MaxLabelWidth);
            var x = marks[i].Struck ? anchor.Left - 2 : (marks[i].Before ? anchor.Left : anchor.Right) - width / 2;
            var above = new Rectangle(x, anchor.Top + 3 - height, width, height);
            var below = new Rectangle(x, anchor.Bottom - 3, width, height);
            labels.Add(new Label(marks[i].Added!, font, Covered(below, anchor) < Covered(above, anchor) ? below : above));
        }
        labels.Sort((a, b) => a.Box.Y != b.Box.Y ? a.Box.Y.CompareTo(b.Box.Y) : a.Box.X.CompareTo(b.Box.X));
        for (var i = 1; i < labels.Count; i++)
        {
            var before = labels[i - 1].Box;
            var box = labels[i].Box;
            if (Math.Abs(before.Y - box.Y) < box.Height && box.Left < before.Right + 3)
                labels[i] = labels[i] with { Box = box with { X = before.Right + 3 } };
        }
        return labels;
    }

    // Area of other text lines a label would hide; the anchor's own line is excluded.
    private int Covered(Rectangle label, Rectangle anchor) => textLines
        .Where(line => line.Bottom <= anchor.Top + anchor.Height / 2 || line.Top >= anchor.Bottom - anchor.Height / 2)
        .Select(line => Rectangle.Intersect(line, label))
        .Sum(overlap => overlap.Width * overlap.Height);

    private static int Stroke(Rectangle line) => Math.Max(2, (int)Math.Round(line.Height / 12f));

    private sealed record Label(string Text, Font Font, Rectangle Box);

    protected override void Dispose(bool disposing)
    {
        if (disposing) timer.Dispose();
        base.Dispose(disposing);
    }
}
