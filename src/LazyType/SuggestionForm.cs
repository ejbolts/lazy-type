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
    private readonly RichTextBox suggestion = new();
    private readonly ModernButton apply = new();
    private readonly ModernButton keep = new();
    private readonly ModernButton copy = new();
    private readonly Button close = new();
    private readonly LinkLabel details = new();
    private readonly Panel headerPanel = new();
    private readonly System.Windows.Forms.Timer copyResetTimer = new() { Interval = 1600 };
    private bool isDark;
    private string suggestedText = string.Empty;
    private IReadOnlyList<DiffPart>? changes;
    // Compact: the edits are drawn in the field itself, so only the actions are shown unless expanded.
    private bool compact, expanded;
    public event Action? ApplyRequested;
    public string SuggestedText => suggestedText;

    public SuggestionForm(string original, bool external, bool isDark = false)
    {
        this.isDark = isDark;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        DoubleBuffered = true;
        KeyPreview = true;
        Size = new Size(440, 240);
        Padding = new Padding(16, 14, 16, 14);

        // Header panel
        headerPanel.Dock = DockStyle.Top;
        headerPanel.Height = 32;
        headerPanel.Cursor = Cursors.SizeAll;
        headerPanel.Paint += (_, pe) =>
        {
            pe.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var iconRect = new Rectangle(2, 6, 20, 20);
            PolishIcon.Draw(pe.Graphics, iconRect, Color.FromArgb(127, 86, 217));
        };
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
        title.SetBounds(28, 4, 220, 24);
        title.BackColor = Color.Transparent;
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

        details.Text = "Show wording";
        details.Font = new Font("Segoe UI", 8.5f);
        details.AutoSize = false;
        details.TextAlign = ContentAlignment.MiddleRight;
        details.SetBounds(headerPanel.Width - 140, 6, 108, 22);
        details.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        details.LinkBehavior = LinkBehavior.HoverUnderline;
        details.Visible = false;
        details.LinkClicked += (_, _) =>
        {
            expanded = !expanded;
            details.Text = expanded ? "Hide wording" : "Show wording";
            UpdateLayout();
        };

        headerPanel.Controls.Add(title);
        headerPanel.Controls.Add(details);
        headerPanel.Controls.Add(close);

        // Subtitle
        subtitle.Text = "Suggested wording";
        subtitle.Font = new Font("Segoe UI", 8.5f, FontStyle.Regular);
        subtitle.Dock = DockStyle.Top;
        subtitle.Height = 20;

        // Content card containing the suggestion
        contentCard.Dock = DockStyle.Fill;
        contentCard.Padding = new Padding(12);

        // Read-only comparison: removed words are struck through and added words are tinted.
        suggestion.ScrollBars = RichTextBoxScrollBars.Vertical;
        suggestion.Dock = DockStyle.Fill;
        suggestion.BorderStyle = BorderStyle.None;
        suggestion.Font = new Font("Segoe UI", 10f);
        suggestion.Text = "Generating suggestion…";
        suggestion.ReadOnly = true;
        suggestion.DetectUrls = false;
        suggestion.TabStop = false;
        contentCard.Controls.Add(suggestion);

        // Action row
        var actionPanel = new Panel { Dock = DockStyle.Bottom, Height = 44, Padding = new Padding(0, 6, 0, 0) };

        apply.Text = "Apply suggestion";
        apply.Style = ModernButton.ButtonStyle.Primary;
        apply.SetBounds(0, 6, 154, 34);
        apply.Click += (_, _) => ApplyRequested?.Invoke();
        apply.Enabled = false; // until a suggestion arrives

        keep.Text = "Keep original";
        keep.Style = ModernButton.ButtonStyle.Secondary;
        keep.SetBounds(162, 6, 120, 34);
        keep.Click += (_, _) => Close();

        copy.Text = "Copy";
        copy.Style = ModernButton.ButtonStyle.Secondary;
        copy.SetBounds(290, 6, 96, 34);
        copy.IconDrawAction = (g, rect, col) => VectorIcons.DrawCopy(g, rect, col);
        copy.Click += (_, _) =>
        {
            if (!string.IsNullOrEmpty(suggestedText) && Native.SetClipboardText(suggestedText))
            {
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
        if (external)
        {
            compact = true;
            subtitle.Text = "Checking your wording…";
            UpdateLayout();
        }
    }

    private void UpdateRegion()
    {
        if (Width > 0 && Height > 0)
        {
            var hRgn = Native.CreateRoundRectRgn(0, 0, Width + 1, Height + 1, 18, 18);
            Region = Region.FromHrgn(hRgn);
            Native.DeleteObject(hRgn);
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        int cornerPref = 2; // DWMWCP_ROUND
        Native.DwmSetWindowAttribute(Handle, 33 /* DWMWA_WINDOW_CORNER_PREFERENCE */, ref cornerPref, sizeof(int));
        UpdateRegion();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        UpdateRegion();
    }

    // inline: the edits are already marked in the field, so the comparison stays hidden unless expanded.
    public void ShowSuggestion(string text, IReadOnlyList<DiffPart> parts, bool canApply, bool inline = false)
    {
        suggestedText = text;
        var changed = TextDiff.HasChanges(parts);
        changes = changed ? parts : null;
        compact = inline; expanded = false;
        var edits = parts.Where((part, i) => !string.IsNullOrWhiteSpace(part.Text)
            && (part.Kind == DiffKind.Removed || (part.Kind == DiffKind.Added && (i == 0 || parts[i - 1].Kind != DiffKind.Removed)))).Count();
        subtitle.Text = !changed ? (inline ? "Looks good · no changes needed" : "No changes needed")
            : inline ? $"{edits} {(edits == 1 ? "change" : "changes")} marked · click a label to apply just that one" : "Suggested changes";
        details.Text = "Show wording";
        details.Visible = inline && changed;
        apply.Enabled = canApply && changed;
        apply.Text = changed ? "Apply suggestion" : "Already optimal";
        RenderChanges();
        UpdateLayout();
    }

    public void ShowFailure(string message)
    {
        changes = null;
        compact = false; details.Visible = false;
        subtitle.Text = "Suggested wording";
        suggestion.Text = message;
        apply.Enabled = false;
        UpdateLayout();
    }

    private void UpdateLayout()
    {
        contentCard.Visible = !compact || expanded;
        if (contentCard.Visible) AdjustHeight();
        else
        {
            Height = Padding.Vertical + headerPanel.Height + subtitle.Height + 50;
            UpdateRegion();
            Invalidate();
        }
    }

    private void RenderChanges()
    {
        if (string.IsNullOrEmpty(suggestedText)) return;
        suggestion.Clear();
        if (changes == null)
        {
            Append("Looks good. Your wording already reads clearly, so there's nothing to change.", suggestion.ForeColor, FontStyle.Regular);
            return;
        }
        var removed = isDark ? Color.FromArgb(240, 128, 140) : Color.FromArgb(196, 50, 66);
        var added = isDark ? Color.FromArgb(190, 160, 255) : Color.FromArgb(109, 64, 204);
        // A tint behind added text keeps a new comma or full stop visible.
        var addedTint = isDark ? Color.FromArgb(68, 50, 112) : Color.FromArgb(230, 220, 255);
        for (var i = 0; i < changes.Count; i++)
        {
            var part = changes[i];
            if (part.Kind == DiffKind.Equal) Append(part.Text, suggestion.ForeColor, FontStyle.Regular);
            else if (part.Kind == DiffKind.Removed)
            {
                Append(part.Text, removed, FontStyle.Strikeout);
                // Separate a struck-out word from its replacement word for readability; display only.
                if (i + 1 < changes.Count && changes[i + 1].Kind == DiffKind.Added && char.IsLetterOrDigit(part.Text[^1]) && char.IsLetterOrDigit(changes[i + 1].Text[0]))
                    Append(" ", suggestion.ForeColor, FontStyle.Regular);
            }
            else Append(part.Text, added, FontStyle.Bold, addedTint);
        }
        suggestion.Select(0, 0);
    }

    private void Append(string text, Color color, FontStyle style, Color? tint = null)
    {
        suggestion.SelectionStart = suggestion.TextLength;
        suggestion.SelectionLength = 0;
        suggestion.SelectionColor = color;
        suggestion.SelectionBackColor = tint ?? suggestion.BackColor;
        suggestion.SelectionFont = new Font(suggestion.Font, style);
        suggestion.AppendText(text);
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
        int needed = Math.Clamp(size.Height + 155, 210, 480);
        Height = needed;
        UpdateRegion();
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
        if (!string.IsNullOrEmpty(suggestedText)) RenderChanges();

        close.ForeColor = dark ? Color.FromArgb(180, 175, 200) : Color.FromArgb(120, 125, 140);
        details.LinkColor = details.ActiveLinkColor = dark ? Color.FromArgb(190, 160, 255) : Color.FromArgb(109, 64, 204);
        details.BackColor = Color.Transparent;
        close.BackColor = Color.Transparent;

        apply.IsDark = dark;
        keep.IsDark = dark;
        copy.IsDark = dark;

        headerPanel.BackColor = Color.Transparent;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        // Window border
        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        Color borderColor = isDark ? Color.FromArgb(68, 55, 98) : Color.FromArgb(215, 205, 235);
        using (var borderPen = new Pen(borderColor, 1.5f))
        {
            DrawingHelpers.DrawRoundedRectangle(g, borderPen, bounds, 16);
        }

        // Draw rounded border around suggestion card
        if (!contentCard.Visible) return;
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
