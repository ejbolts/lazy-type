using System.Drawing.Drawing2D;

namespace LazyType;

// Personal insight: how often each text model cleaned a dictation, and how much was said to it.
internal sealed class UsageForm : Form
{
    private sealed class ShareBar : Control
    {
        private float share;
        public float Share { get => share; set { share = Math.Clamp(value, 0, 1); Invalidate(); } }
        public Color Track = Color.FromArgb(234, 236, 242), Fill = Color.FromArgb(127, 86, 217);
        public ShareBar() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent?.BackColor ?? BackColor);
            var radius = Math.Max(2, Height / 2);
            using var track = new SolidBrush(Track);
            DrawingHelpers.FillRoundedRectangle(g, track, new Rectangle(0, 0, Width - 1, Height - 1), radius);
            var width = (int)Math.Round((Width - 1) * share);
            if (width <= 0) return;
            using var fill = new SolidBrush(Fill);
            DrawingHelpers.FillRoundedRectangle(g, fill, new Rectangle(0, 0, Math.Max(width, Height), Height - 1), radius);
        }
    }
    private sealed class Row
    {
        public readonly Label Name = new(), Count = new(), Detail = new();
        public readonly ShareBar Bar = new();
    }

    private static readonly string[] Models = { ModelUsage.LegacyQwenModel, TextModels.Qwen35, TextModels.Gemma };
    private readonly Label title = new(), summary = new(), note = new();
    private readonly Dictionary<string, Row> rows = new();
    private readonly ModernButton close = new();
    private bool isDark;

    public UsageForm(bool dark)
    {
        Text = "Lazy Type · Model usage";
        Font = new Font("Segoe UI", 10f);
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        ClientSize = new Size(440, 456);
        Padding = new Padding(22, 18, 22, 16);
        KeyPreview = true;
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Close(); };

        title.Text = "Model usage";
        title.Font = new Font("Segoe UI", 14f, FontStyle.Bold);
        title.Dock = DockStyle.Top; title.Height = 32;
        summary.Font = new Font("Segoe UI", 9.5f);
        summary.Dock = DockStyle.Top; summary.Height = 34; summary.AutoEllipsis = true;

        var list = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 6, 0, 0) };
        foreach (var model in Models.Reverse())
        {
            var row = new Row();
            var panel = new Panel { Dock = DockStyle.Top, Height = 78, Padding = new Padding(0, 4, 0, 8) };
            var head = new Panel { Dock = DockStyle.Top, Height = 24 };
            row.Name.Text = model == ModelUsage.LegacyQwenModel ? model + " (previous)" : model; row.Name.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
            row.Name.Dock = DockStyle.Fill; row.Name.TextAlign = ContentAlignment.MiddleLeft;
            row.Count.Dock = DockStyle.Right; row.Count.Width = 200; row.Count.TextAlign = ContentAlignment.MiddleRight;
            head.Controls.Add(row.Name); head.Controls.Add(row.Count);
            row.Bar.Dock = DockStyle.Top; row.Bar.Height = 8; row.Bar.AccessibleName = model + " share";
            var gap = new Panel { Dock = DockStyle.Top, Height = 6 };
            row.Detail.Font = new Font("Segoe UI", 8.5f);
            row.Detail.Dock = DockStyle.Top; row.Detail.Height = 22; row.Detail.AutoEllipsis = true;
            row.Detail.TextAlign = ContentAlignment.MiddleLeft;
            panel.Controls.Add(row.Detail); panel.Controls.Add(row.Bar); panel.Controls.Add(gap); panel.Controls.Add(head);
            rows[model] = row;
            list.Controls.Add(panel);
        }

        note.Text = "Each edited dictation counts once, for the model that edited it. Raw dictations and wording checks aren't counted. Only counts are saved, never your words.";
        note.Font = new Font("Segoe UI", 8.5f);
        note.Dock = DockStyle.Bottom; note.Height = 48;

        var actions = new Panel { Dock = DockStyle.Bottom, Height = 46, Padding = new Padding(0, 10, 0, 0) };
        close.Text = "Close"; close.Style = ModernButton.ButtonStyle.Primary;
        close.Dock = DockStyle.Right; close.Width = 96;
        close.Click += (_, _) => Close();
        actions.Controls.Add(close);

        Controls.Add(list); Controls.Add(note); Controls.Add(actions); Controls.Add(summary); Controls.Add(title);
        ApplyTheme(dark);
    }

    public void ShowStats(ModelUsage usage)
    {
        var total = usage.Dictations;
        var since = usage.Since.ToLocalTime().ToString("d MMM yyyy");
        summary.Text = total == 0
            ? $"No edited dictations yet · counting since {since}"
            : $"{Plural(total, "edited dictation")} · {Plural(usage.Words, "word")} spoken · since {since}";
        foreach (var (model, row) in rows)
        {
            var entry = usage.For(model);
            var share = total == 0 ? 0 : (float)entry.Dictations / total;
            row.Count.Text = entry.Dictations == 0 ? "Not used" : $"{entry.Dictations:N0} · {share:P0}";
            row.Bar.Share = share;
            row.Detail.Text = entry.Dictations == 0
                ? model == ModelUsage.LegacyQwenModel ? "Historical model; no longer selectable" : model == TextModels.Gemma ? "Used for Reword or when selected" : "Used when selected for Clean up / Polish"
                : $"{Plural(entry.Words, "word")} · {entry.Words / (double)entry.Dictations:N0} per dictation · {entry.Dynamic:N0} historical Dynamic · {entry.Manual:N0} direct";
            row.Count.ForeColor = entry.Dictations == 0 ? MutedColor : ForeColor;
        }
    }

    // CenterParent only applies to modal dialogs, so centre this modeless window over its owner by hand.
    public void ShowOver(Form owner)
    {
        var anchor = owner.Visible ? owner.Bounds : Screen.FromPoint(Cursor.Position).WorkingArea;
        var area = Screen.FromRectangle(anchor).WorkingArea;
        Location = new Point(Math.Clamp(anchor.Left + (anchor.Width - Width) / 2, area.Left, Math.Max(area.Left, area.Right - Width)),
            Math.Clamp(anchor.Top + (anchor.Height - Height) / 2, area.Top, Math.Max(area.Top, area.Bottom - Height)));
        Show(owner);
    }

    private static string Plural(long n, string noun) => $"{n:N0} {noun}{(n == 1 ? "" : "s")}";
    private Color MutedColor => isDark ? Color.FromArgb(150, 165, 185) : Color.FromArgb(100, 116, 139);

    public void ApplyTheme(bool dark)
    {
        isDark = dark;
        BackColor = dark ? Color.FromArgb(16, 20, 28) : Color.FromArgb(248, 249, 252);
        ForeColor = dark ? Color.FromArgb(240, 242, 248) : Color.FromArgb(30, 41, 59);
        title.ForeColor = ForeColor;
        summary.ForeColor = note.ForeColor = MutedColor;
        foreach (var row in rows.Values)
        {
            row.Name.ForeColor = ForeColor;
            row.Detail.ForeColor = MutedColor;
            row.Bar.Track = dark ? Color.FromArgb(42, 49, 66) : Color.FromArgb(230, 233, 240);
            row.Bar.Fill = dark ? Color.FromArgb(160, 125, 240) : Color.FromArgb(127, 86, 217);
            row.Bar.Invalidate();
        }
        close.IsDark = dark;
        if (IsHandleCreated) SetTitleBar();
        Invalidate(true);
    }

    private void SetTitleBar()
    {
        int useDark = isDark ? 1 : 0;
        Native.DwmSetWindowAttribute(Handle, 20, ref useDark, sizeof(int));
    }
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); SetTitleBar(); }
}
