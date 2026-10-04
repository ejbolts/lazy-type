using System.Drawing.Drawing2D;

namespace LazyType;

internal sealed class SuggestionBadge : Form
{
    private readonly WandButton button = new();
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 250 };
    private readonly ToolTip tips = new();
    private TextTarget? target;
    public event Action? Requested;

    public SuggestionBadge()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        Size = new Size(34, 34);
        BackColor = Color.Magenta;
        TransparencyKey = Color.Magenta;

        button.Dock = DockStyle.Fill;
        button.Click += (_, _) => Requested?.Invoke();
        tips.SetToolTip(button, "A little polish · Refine wording");
        Controls.Add(button);

        timer.Tick += (_, _) => UpdatePosition();
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x08000000 | 0x80; // WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW
            return cp;
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x21) { m.Result = (IntPtr)3; return; } // MA_NOACTIVATE
        base.WndProc(ref m);
    }

    public void Present(TextTarget destination)
    {
        target = destination;
        timer.Start();
        UpdatePosition();
    }

    public void Dismiss()
    {
        timer.Stop();
        target = null;
        Hide();
    }

    private void UpdatePosition()
    {
        if (target == null || !target.IsCurrent() || !target.TryGetBounds(out var bounds))
        {
            Hide();
            return;
        }
        var area = Screen.FromRectangle(bounds).WorkingArea;
        var gap = Math.Max(4, DeviceDpi / 24);
        var x = bounds.Right + gap;
        if (x + Width > area.Right - gap) x = bounds.Right - Width - gap;
        Location = new Point(
            Math.Clamp(x, area.Left, Math.Max(area.Left, area.Right - Width)),
            Math.Clamp(bounds.Bottom - Height, area.Top, Math.Max(area.Top, area.Bottom - Height)));
        if (!Visible) Show();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            timer.Dispose();
            tips.Dispose();
        }
        base.Dispose(disposing);
    }
}

internal sealed class SuggestionForm : Form
{
    private readonly Label title = new();
    private readonly Label subtitle = new();
    private readonly Panel contentCard = new();
    private readonly TextBox suggestion = new();
    private readonly ModernButton apply = new();
    private readonly ModernButton keep = new();
    private readonly ModernButton copy = new();
    private readonly Button close = new();
    private readonly Panel headerPanel = new();
    private readonly System.Windows.Forms.Timer copyResetTimer = new() { Interval = 1600 };
    private bool isDark;
    public event Action? ApplyRequested;
    public string SuggestedText => suggestion.Text;

    public SuggestionForm(string original, bool external, bool isDark = false)
    {
        this.isDark = isDark;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        DoubleBuffered = true;
        KeyPreview = true;
        Size = new Size(420, 240);
        Padding = new Padding(16);

        // Header panel
        headerPanel.Dock = DockStyle.Top;
        headerPanel.Height = 32;
        headerPanel.Cursor = Cursors.SizeAll;
        headerPanel.MouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                Native.ReleaseCapture();
                Native.SendMessage(Handle, 0xA1 /* WM_NCLBUTTONDOWN */, (IntPtr)2 /* HTCAPTION */, IntPtr.Zero);
            }
        };

        title.Text = "A little polish";
        title.Font = new Font("Segoe UI", 11.5f, FontStyle.Bold);
        title.AutoSize = false;
        title.SetBounds(26, 3, 260, 26);
        title.Cursor = Cursors.SizeAll;
        title.MouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                Native.ReleaseCapture();
                Native.SendMessage(Handle, 0xA1, (IntPtr)2, IntPtr.Zero);
            }
        };

        close.Text = "✕";
        close.Font = new Font("Segoe UI", 9f);
        close.SetBounds(headerPanel.Width - 28, 2, 26, 26);
        close.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        close.FlatStyle = FlatStyle.Flat;
        close.FlatAppearance.BorderSize = 0;
        close.Cursor = Cursors.Hand;
        close.Click += (_, _) => Close();

        headerPanel.Controls.Add(title);
        headerPanel.Controls.Add(close);

        // Subtitle
        subtitle.Text = "Suggested wording";
        subtitle.Font = new Font("Segoe UI", 8.5f, FontStyle.Regular);
        subtitle.Dock = DockStyle.Top;
        subtitle.Height = 20;

        // Content card containing the suggestion
        contentCard.Dock = DockStyle.Fill;
        contentCard.Padding = new Padding(12);

        suggestion.Multiline = true;
        suggestion.ScrollBars = ScrollBars.Vertical;
        suggestion.Dock = DockStyle.Fill;
        suggestion.BorderStyle = BorderStyle.None;
        suggestion.Font = new Font("Segoe UI", 10f);
        suggestion.Text = "Generating suggestion…";
        suggestion.ReadOnly = false;
        contentCard.Controls.Add(suggestion);

        // Action row
        var actionPanel = new Panel { Dock = DockStyle.Bottom, Height = 42, Padding = new Padding(0, 8, 0, 0) };

        apply.Text = "Apply suggestion";
        apply.Style = ModernButton.ButtonStyle.Primary;
        apply.SetBounds(0, 6, 148, 34);
        apply.Click += (_, _) => ApplyRequested?.Invoke();

        keep.Text = "Keep original";
        keep.Style = ModernButton.ButtonStyle.Secondary;
        keep.SetBounds(154, 6, 115, 34);
        keep.Click += (_, _) => Close();

        copy.Text = "Copy";
        copy.Style = ModernButton.ButtonStyle.Secondary;
        copy.SetBounds(275, 6, 80, 34);
        copy.IconDrawAction = (g, rect, col) => VectorIcons.DrawCopy(g, rect, col);
        copy.Click += (_, _) =>
        {
            if (!string.IsNullOrEmpty(suggestion.Text))
            {
                try { Clipboard.SetText(suggestion.Text); } catch { }
                copy.Text = "Copied!";
                copyResetTimer.Stop();
                copyResetTimer.Start();
            }
        };
        copyResetTimer.Tick += (_, _) =>
        {
            copyResetTimer.Stop();
            copy.Text = "Copy";
        };

        actionPanel.Controls.AddRange(new Control[] { apply, keep, copy });

        Controls.Add(contentCard);
        Controls.Add(subtitle);
        Controls.Add(headerPanel);
        Controls.Add(actionPanel);

        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape) Close();
        };

        ApplyTheme(isDark);
    }

    public void ShowSuggestion(string text, bool changed, bool canApply)
    {
        suggestion.Text = text;
        apply.Enabled = canApply && changed;
        apply.Text = changed ? "Apply suggestion" : "Already optimal";
        AdjustHeight();
    }

    public void ShowFailure(string message)
    {
        suggestion.Text = message;
        apply.Enabled = false;
        AdjustHeight();
    }

    public void SetApplying()
    {
        apply.Enabled = false;
        apply.Text = "Applying…";
    }

    private void AdjustHeight()
    {
        using var g = suggestion.CreateGraphics();
        var size = TextRenderer.MeasureText(g, suggestion.Text, suggestion.Font, new Size(contentCard.Width - 30, int.MaxValue), TextFormatFlags.WordBreak);
        int needed = Math.Clamp(size.Height + 160, 220, 480);
        Height = needed;
        Invalidate();
    }

    public void ApplyTheme(bool dark)
    {
        isDark = dark;
        BackColor = dark ? Color.FromArgb(24, 20, 38) : Color.FromArgb(252, 252, 254);
        ForeColor = dark ? Color.FromArgb(235, 235, 245) : Color.FromArgb(30, 35, 50);

        title.ForeColor = ForeColor;
        subtitle.ForeColor = dark ? Color.FromArgb(160, 150, 185) : Color.FromArgb(102, 112, 133);

        contentCard.BackColor = dark ? Color.FromArgb(36, 30, 56) : Color.FromArgb(245, 243, 255);
        suggestion.BackColor = contentCard.BackColor;
        suggestion.ForeColor = ForeColor;

        close.ForeColor = dark ? Color.FromArgb(180, 175, 200) : Color.FromArgb(120, 125, 140);
        close.BackColor = Color.Transparent;

        apply.IsDark = dark;
        keep.IsDark = dark;
        copy.IsDark = dark;

        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        // Window border
        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        Color borderColor = isDark ? Color.FromArgb(65, 55, 95) : Color.FromArgb(220, 215, 235);
        using (var borderPen = new Pen(borderColor, 1.5f))
        {
            DrawingHelpers.DrawRoundedRectangle(g, borderPen, bounds, 12);
        }

        // Draw dual stars polish icon in header
        var iconRect = new Rectangle(16, 17, 20, 20);
        PolishIcon.Draw(g, iconRect, Color.FromArgb(127, 86, 217));

        // Draw rounded border around suggestion card
        var cardBounds = new Rectangle(contentCard.Left, contentCard.Top, contentCard.Width - 1, contentCard.Height - 1);
        Color cardBorder = isDark ? Color.FromArgb(58, 48, 88) : Color.FromArgb(233, 215, 254);
        using (var cardPen = new Pen(cardBorder, 1.2f))
        {
            DrawingHelpers.DrawRoundedRectangle(g, cardPen, cardBounds, 8);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            copyResetTimer.Dispose();
        }
        base.Dispose(disposing);
    }
}
