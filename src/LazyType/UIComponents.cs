using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace LazyType;

internal sealed class BufferedPanel : Panel
{
    public BufferedPanel()
    {
        // Repaint the full surface when docking/anchoring changes its size, so
        // rounded borders and moved child controls cannot leave old pixels behind.
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }
}

internal static class DrawingHelpers
{
    public static GraphicsPath CreateRoundedRectangle(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        int diameter = Math.Max(1, radius * 2);
        var arc = new Rectangle(bounds.Location, new Size(diameter, diameter));

        path.AddArc(arc, 180, 90);
        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = bounds.Left;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static void FillRoundedRectangle(Graphics g, Brush brush, Rectangle bounds, int radius)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        using var path = CreateRoundedRectangle(bounds, radius);
        g.FillPath(brush, path);
    }

    public static void DrawRoundedRectangle(Graphics g, Pen pen, Rectangle bounds, int radius)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        using var path = CreateRoundedRectangle(bounds, radius);
        g.DrawPath(pen, path);
    }
}

internal static class PolishIcon
{
    public static void Draw(Graphics g, Rectangle bounds, Color color)
    {
        var prevSmoothing = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        float w = bounds.Width;
        float h = bounds.Height;
        float x = bounds.X;
        float y = bounds.Y;

        using var brush = new SolidBrush(color);

        // Major 4-point sparkle star (upper right/center)
        float cx1 = x + w * 0.42f;
        float cy1 = y + h * 0.42f;
        float r1Outer = Math.Min(w, h) * 0.38f;
        float r1Inner = r1Outer * 0.22f;
        DrawFourPointStar(g, brush, cx1, cy1, r1Outer, r1Inner);

        // Minor 4-point sparkle star (lower right)
        float cx2 = x + w * 0.78f;
        float cy2 = y + h * 0.78f;
        float r2Outer = Math.Min(w, h) * 0.20f;
        float r2Inner = r2Outer * 0.22f;
        DrawFourPointStar(g, brush, cx2, cy2, r2Outer, r2Inner);

        g.SmoothingMode = prevSmoothing;
    }

    private static void DrawFourPointStar(Graphics g, Brush brush, float cx, float cy, float rOuter, float rInner)
    {
        PointF[] pts = new PointF[8];
        for (int i = 0; i < 8; i++)
        {
            double angle = (i * Math.PI / 4.0) - (Math.PI / 2.0);
            float r = (i % 2 == 0) ? rOuter : rInner;
            pts[i] = new PointF((float)(cx + r * Math.Cos(angle)), (float)(cy + r * Math.Sin(angle)));
        }
        g.FillPolygon(brush, pts);
    }
}

internal static class VectorIcons
{
    public static void DrawMicrophone(Graphics g, Rectangle bounds, Color color)
    {
        var prevSmoothing = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        float w = bounds.Width;
        float h = bounds.Height;
        float x = bounds.X;
        float y = bounds.Y;

        float stroke = Math.Max(1.6f, w * 0.08f);
        using var pen = new Pen(color, stroke) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var brush = new SolidBrush(color);

        // Mic capsule
        float capW = w * 0.38f;
        float capH = h * 0.52f;
        float capX = x + (w - capW) / 2f;
        float capY = y + h * 0.08f;
        var capRect = new Rectangle((int)capX, (int)capY, (int)capW, (int)capH);
        DrawingHelpers.FillRoundedRectangle(g, brush, capRect, (int)(capW / 2f));

        // Cradle arc
        float arcW = w * 0.60f;
        float arcH = h * 0.52f;
        float arcX = x + (w - arcW) / 2f;
        float arcY = y + h * 0.16f;
        g.DrawArc(pen, arcX, arcY, arcW, arcH, 0, 180);

        // Stem & base
        float stemX = x + w / 2f;
        float stemTop = arcY + arcH;
        float stemBottom = y + h * 0.88f;
        g.DrawLine(pen, stemX, stemTop, stemX, stemBottom);

        float baseW = w * 0.44f;
        g.DrawLine(pen, stemX - baseW / 2f, stemBottom, stemX + baseW / 2f, stemBottom);

        g.SmoothingMode = prevSmoothing;
    }

    public static void DrawCleanSpeech(Graphics g, Rectangle bounds, Color color)
    {
        var prevSmoothing = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        float w = bounds.Width;
        float h = bounds.Height;
        float x = bounds.X;
        float y = bounds.Y;

        float stroke = Math.Max(1.8f, w * 0.09f);
        using var pen = new Pen(color, stroke) { StartCap = LineCap.Round, EndCap = LineCap.Round };

        float y1 = y + h * 0.28f;
        float y2 = y + h * 0.50f;
        float y3 = y + h * 0.72f;

        g.DrawLine(pen, x + w * 0.15f, y1, x + w * 0.85f, y1);
        g.DrawLine(pen, x + w * 0.10f, y2, x + w * 0.72f, y2);
        g.DrawLine(pen, x + w * 0.22f, y3, x + w * 0.65f, y3);

        g.SmoothingMode = prevSmoothing;
    }

    public static void DrawWindowsLogo(Graphics g, Rectangle bounds, Color color)
    {
        var prevSmoothing = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        float w = bounds.Width;
        float h = bounds.Height;
        float x = bounds.X + w * 0.12f;
        float y = bounds.Y + h * 0.12f;
        float sizeW = w * 0.76f;
        float sizeH = h * 0.76f;
        float gap = Math.Max(2f, sizeW * 0.10f);

        float paneW = (sizeW - gap) / 2f;
        float paneH = (sizeH - gap) / 2f;

        using var brush = new SolidBrush(color);
        g.FillRectangle(brush, x, y, paneW, paneH);
        g.FillRectangle(brush, x + paneW + gap, y, paneW, paneH);
        g.FillRectangle(brush, x, y + paneH + gap, paneW, paneH);
        g.FillRectangle(brush, x + paneW + gap, y + paneH + gap, paneW, paneH);

        g.SmoothingMode = prevSmoothing;
    }

    public static void DrawCopy(Graphics g, Rectangle bounds, Color color)
    {
        var prevSmoothing = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        float stroke = Math.Max(1.4f, bounds.Width * 0.09f);
        using var pen = new Pen(color, stroke);

        int pad = (int)(bounds.Width * 0.12f);
        int sheetW = (int)(bounds.Width * 0.56f);
        int sheetH = (int)(bounds.Height * 0.62f);

        var backRect = new Rectangle(bounds.X + bounds.Width - sheetW - pad, bounds.Y + pad, sheetW, sheetH);
        DrawingHelpers.DrawRoundedRectangle(g, pen, backRect, 2);

        using var bgBrush = new SolidBrush(Color.FromArgb(240, 242, 246));
        var frontRect = new Rectangle(bounds.X + pad, bounds.Y + bounds.Height - sheetH - pad, sheetW, sheetH);
        DrawingHelpers.DrawRoundedRectangle(g, pen, frontRect, 2);

        g.SmoothingMode = prevSmoothing;
    }

    public static void DrawPause(Graphics g, Rectangle bounds, Color color)
    {
        var prevSmoothing = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        using var brush = new SolidBrush(color);
        int barW = Math.Max(3, (int)(bounds.Width * 0.18f));
        int barH = (int)(bounds.Height * 0.60f);
        int gap = (int)(bounds.Width * 0.18f);

        int totalW = barW * 2 + gap;
        int x1 = bounds.X + (bounds.Width - totalW) / 2;
        int y = bounds.Y + (bounds.Height - barH) / 2;
        int x2 = x1 + barW + gap;

        DrawingHelpers.FillRoundedRectangle(g, brush, new Rectangle(x1, y, barW, barH), barW / 2);
        DrawingHelpers.FillRoundedRectangle(g, brush, new Rectangle(x2, y, barW, barH), barW / 2);

        g.SmoothingMode = prevSmoothing;
    }

    public static void DrawPlay(Graphics g, Rectangle bounds, Color color)
    {
        var prevSmoothing = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        using var brush = new SolidBrush(color);
        float w = bounds.Width * 0.55f;
        float h = bounds.Height * 0.60f;
        float x = bounds.X + (bounds.Width - w) / 2f + w * 0.1f;
        float y = bounds.Y + (bounds.Height - h) / 2f;

        PointF[] pts = new[]
        {
            new PointF(x, y),
            new PointF(x + w, y + h / 2f),
            new PointF(x, y + h)
        };
        g.FillPolygon(brush, pts);

        g.SmoothingMode = prevSmoothing;
    }

    public static void DrawEye(Graphics g, Rectangle bounds, Color color)
    {
        var prevSmoothing = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        float stroke = Math.Max(1.6f, bounds.Width * 0.08f);
        using var pen = new Pen(color, stroke);
        using var brush = new SolidBrush(color);

        float cx = bounds.X + bounds.Width / 2f;
        float cy = bounds.Y + bounds.Height / 2f;
        float ew = bounds.Width * 0.72f;
        float eh = bounds.Height * 0.44f;

        using var path = new GraphicsPath();
        path.AddArc(cx - ew / 2f, cy - eh, ew, eh * 2, 35, 110);
        path.AddArc(cx - ew / 2f, cy - eh, ew, eh * 2, 215, 110);
        g.DrawArc(pen, cx - ew / 2f, cy - eh * 0.8f, ew, eh * 1.6f, 30, 120);
        g.DrawArc(pen, cx - ew / 2f, cy - eh * 0.8f, ew, eh * 1.6f, 210, 120);

        float pr = Math.Max(3f, bounds.Width * 0.12f);
        g.FillEllipse(brush, cx - pr, cy - pr, pr * 2, pr * 2);

        g.SmoothingMode = prevSmoothing;
    }

    public static void DrawChevron(Graphics g, Rectangle bounds, Color color, bool expanded)
    {
        var prevSmoothing = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        float stroke = Math.Max(1.6f, bounds.Width * 0.10f);
        using var pen = new Pen(color, stroke) { StartCap = LineCap.Round, EndCap = LineCap.Round };

        float cx = bounds.X + bounds.Width / 2f;
        float cy = bounds.Y + bounds.Height / 2f;
        float span = bounds.Width * 0.28f;
        float rise = bounds.Height * 0.16f;

        if (expanded)
        {
            g.DrawLine(pen, cx - span, cy + rise, cx, cy - rise);
            g.DrawLine(pen, cx, cy - rise, cx + span, cy + rise);
        }
        else
        {
            g.DrawLine(pen, cx - span, cy - rise, cx, cy + rise);
            g.DrawLine(pen, cx, cy + rise, cx + span, cy - rise);
        }

        g.SmoothingMode = prevSmoothing;
    }

    public static void DrawGear(Graphics g, Rectangle bounds, Color color)
    {
        var prevSmoothing = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        float stroke = Math.Max(1.6f, bounds.Width * 0.09f);
        using var pen = new Pen(color, stroke);
        using var brush = new SolidBrush(color);

        float cx = bounds.X + bounds.Width / 2f;
        float cy = bounds.Y + bounds.Height / 2f;
        float outerR = bounds.Width * 0.32f;
        float innerR = bounds.Width * 0.15f;

        g.DrawEllipse(pen, cx - outerR, cy - outerR, outerR * 2, outerR * 2);
        g.DrawEllipse(pen, cx - innerR, cy - innerR, innerR * 2, innerR * 2);

        for (int i = 0; i < 6; i++)
        {
            double angle = i * Math.PI / 3.0;
            float cos = (float)Math.Cos(angle);
            float sin = (float)Math.Sin(angle);
            g.DrawLine(pen, cx + cos * (outerR - 1), cy + sin * (outerR - 1), cx + cos * (outerR + 3), cy + sin * (outerR + 3));
        }

        g.SmoothingMode = prevSmoothing;
    }
}

internal sealed class ModernCheckBox : CheckBox
{
    private bool hovered;
    private bool pressed;
    private bool isDark;

    public bool IsDark
    {
        get => isDark;
        set { isDark = value; Invalidate(); }
    }

    public ModernCheckBox()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        Font = new Font("Segoe UI", 10f);
        Margin = new Padding(0);
        Padding = new Padding(0);
    }

    protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hovered = false; pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs mevent) { if (mevent.Button == MouseButtons.Left) { pressed = true; Invalidate(); } base.OnMouseDown(mevent); }
    protected override void OnMouseUp(MouseEventArgs mevent) { pressed = false; Invalidate(); base.OnMouseUp(mevent); }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        var g = pevent.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        if (Parent != null)
        {
            using var pBrush = new SolidBrush(Parent.BackColor);
            g.FillRectangle(pBrush, ClientRectangle);
        }

        int boxSize = Math.Max(18, (int)(18f * (DeviceDpi / 96f)));
        int x = (Width - boxSize) / 2;
        int y = (Height - boxSize) / 2;
        var boxRect = new Rectangle(x, y, boxSize, boxSize);

        Color borderNormal = isDark ? Color.FromArgb(71, 84, 103) : Color.FromArgb(208, 213, 221);
        Color borderHover = isDark ? Color.FromArgb(145, 120, 240) : Color.FromArgb(127, 86, 217);
        Color bgUnchecked = isDark ? Color.FromArgb(30, 36, 48) : Color.White;
        Color bgChecked = pressed ? Color.FromArgb(105, 65, 198) : Color.FromArgb(127, 86, 217);
        Color checkColor = Color.White;

        Color currentBorder = (hovered || Focused) ? borderHover : borderNormal;
        if (!Enabled)
        {
            // Locked but still legible: a softer fill and border instead of the system's disabled grey.
            bgChecked = isDark ? Color.FromArgb(84, 66, 138) : Color.FromArgb(196, 178, 238);
            currentBorder = isDark ? Color.FromArgb(52, 61, 78) : Color.FromArgb(226, 230, 237);
        }

        if (Checked)
        {
            using var fillBrush = new SolidBrush(bgChecked);
            DrawingHelpers.FillRoundedRectangle(g, fillBrush, boxRect, 4);

            using var pen = new Pen(checkColor, Math.Max(2f, 2f * (DeviceDpi / 96f))) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            float cx = boxRect.X;
            float cy = boxRect.Y;
            float bs = boxSize;

            PointF p1 = new PointF(cx + bs * 0.26f, cy + bs * 0.52f);
            PointF p2 = new PointF(cx + bs * 0.44f, cy + bs * 0.70f);
            PointF p3 = new PointF(cx + bs * 0.76f, cy + bs * 0.32f);
            g.DrawLine(pen, p1, p2);
            g.DrawLine(pen, p2, p3);
        }
        else
        {
            using var bgBrush = new SolidBrush(bgUnchecked);
            DrawingHelpers.FillRoundedRectangle(g, bgBrush, boxRect, 4);
            using var borderPen = new Pen(currentBorder, 1.5f);
            DrawingHelpers.DrawRoundedRectangle(g, borderPen, boxRect, 4);
        }
    }
}

internal sealed class WandButton : Button
{
    private bool hovered, pressed;
    private bool isDark;

    public bool IsDark
    {
        get => isDark;
        set { isDark = value; Invalidate(); }
    }

    public WandButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        UpdateRegion();
    }

    private void UpdateRegion()
    {
        if (Width > 0 && Height > 0)
        {
            using var path = new GraphicsPath();
            path.AddEllipse(0, 0, Width, Height);
            Region = new Region(path);
        }
    }

    protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hovered = false; pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs mevent) { if (mevent.Button == MouseButtons.Left) { pressed = true; Invalidate(); } base.OnMouseDown(mevent); }
    protected override void OnMouseUp(MouseEventArgs mevent) { pressed = false; Invalidate(); base.OnMouseUp(mevent); }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        var g = pevent.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        if (Parent != null)
        {
            using var pBrush = new SolidBrush(Parent.BackColor);
            g.FillRectangle(pBrush, ClientRectangle);
        }

        var bounds = ClientRectangle;
        Color bg = isDark
            ? (pressed ? Color.FromArgb(48, 36, 80) : hovered ? Color.FromArgb(75, 58, 120) : Color.FromArgb(60, 46, 96))
            : (pressed ? Color.FromArgb(225, 215, 250) : hovered ? Color.FromArgb(250, 245, 255) : Color.FromArgb(243, 238, 255));
        Color fg = isDark ? Color.FromArgb(195, 170, 255) : Color.FromArgb(127, 86, 217);

        using var brush = new SolidBrush(bg);
        g.FillEllipse(brush, 0, 0, bounds.Width - 1, bounds.Height - 1);

        int iconPadding = Math.Max(6, (int)(bounds.Width * 0.22f));
        var iconRect = new Rectangle(bounds.X + iconPadding, bounds.Y + iconPadding, bounds.Width - iconPadding * 2, bounds.Height - iconPadding * 2);
        PolishIcon.Draw(g, iconRect, Enabled ? fg : (isDark ? Color.FromArgb(90, 80, 110) : Color.FromArgb(180, 175, 195)));
    }
}

internal sealed class ModernButton : Button
{
    public enum ButtonStyle { Primary, Secondary, IconOnly }
    public ButtonStyle Style { get; set; } = ButtonStyle.Secondary;
    public int CornerRadius { get; set; } = 8;
    public Action<Graphics, Rectangle, Color>? IconDrawAction { get; set; }
    private bool hovered, pressed;
    private bool isDark;

    public bool IsDark
    {
        get => isDark;
        set { isDark = value; Invalidate(); }
    }

    public ModernButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        UpdateRegion();
    }

    private void UpdateRegion()
    {
        if (Width > 0 && Height > 0)
        {
            using var path = DrawingHelpers.CreateRoundedRectangle(new Rectangle(0, 0, Width, Height), CornerRadius);
            Region = new Region(path);
        }
    }

    protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hovered = false; pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs mevent) { if (mevent.Button == MouseButtons.Left) { pressed = true; Invalidate(); } base.OnMouseDown(mevent); }
    protected override void OnMouseUp(MouseEventArgs mevent) { pressed = false; Invalidate(); base.OnMouseUp(mevent); }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        var g = pevent.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        if (Parent != null)
        {
            using var parentBrush = new SolidBrush(Parent.BackColor);
            g.FillRectangle(parentBrush, ClientRectangle);
        }

        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        Color bg, fg, border = Color.Transparent;

        if (Style == ButtonStyle.Primary)
        {
            bg = hovered ? (pressed ? Color.FromArgb(105, 65, 198) : Color.FromArgb(115, 75, 210)) : Color.FromArgb(127, 86, 217);
            fg = Color.White;
        }
        else if (Style == ButtonStyle.IconOnly)
        {
            bg = hovered ? (isDark ? Color.FromArgb(45, 55, 72) : Color.FromArgb(238, 242, 246)) : Color.Transparent;
            fg = isDark ? Color.FromArgb(200, 210, 225) : Color.FromArgb(71, 84, 103);
            border = (hovered || isDark) ? (isDark ? Color.FromArgb(55, 65, 81) : Color.FromArgb(220, 225, 235)) : Color.Transparent;
        }
        else
        {
            bg = hovered ? (isDark ? Color.FromArgb(40, 48, 62) : Color.FromArgb(245, 247, 250)) : (isDark ? Color.FromArgb(30, 36, 48) : Color.White);
            fg = isDark ? Color.FromArgb(220, 225, 235) : Color.FromArgb(52, 64, 84);
            border = isDark ? Color.FromArgb(60, 70, 85) : Color.FromArgb(208, 213, 221);
        }

        if (!Enabled)
        {
            bg = isDark ? Color.FromArgb(35, 40, 50) : Color.FromArgb(240, 242, 245);
            fg = isDark ? Color.FromArgb(90, 100, 115) : Color.FromArgb(160, 170, 185);
            border = Color.Transparent;
        }

        using var bgBrush = new SolidBrush(bg);
        DrawingHelpers.FillRoundedRectangle(g, bgBrush, bounds, CornerRadius);

        if (border != Color.Transparent)
        {
            using var borderPen = new Pen(border, 1.2f);
            DrawingHelpers.DrawRoundedRectangle(g, borderPen, bounds, CornerRadius);
        }

        bool hasIcon = IconDrawAction != null;
        bool hasText = !string.IsNullOrEmpty(Text);

        if (hasIcon && !hasText)
        {
            int iconSize = Math.Min(18, bounds.Height - 8);
            int iconX = bounds.X + (bounds.Width - iconSize) / 2;
            int iconY = bounds.Y + (bounds.Height - iconSize) / 2;
            IconDrawAction!(g, new Rectangle(iconX, iconY, iconSize, iconSize), fg);
        }
        else if (hasIcon && hasText)
        {
            int iconSize = Math.Min(15, bounds.Height - 12);
            int iconY = bounds.Y + (bounds.Height - iconSize) / 2;
            var textSize = TextRenderer.MeasureText(Text, Font);
            int totalW = iconSize + 6 + textSize.Width;
            int startX = Math.Max(8, (bounds.Width - totalW) / 2);

            IconDrawAction!(g, new Rectangle(startX, iconY, iconSize, iconSize), fg);
            int textX = startX + iconSize + 6;
            var textRect = new Rectangle(textX, bounds.Y, bounds.Width - textX - 4, bounds.Height);
            TextRenderer.DrawText(g, Text, Font, textRect, fg, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        }
        else if (hasText)
        {
            TextRenderer.DrawText(g, Text, Font, bounds, fg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }
}
