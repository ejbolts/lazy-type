using System.Drawing.Drawing2D;

namespace LazyType;

internal sealed class WandButton : Button
{
    public WandButton()
    {
        AccessibleName = "AI suggestion";
        AccessibleDescription = "Suggest clearer wording and grammar using local AI";
        BackColor = Color.FromArgb(239, 235, 255);
        ForeColor = Color.FromArgb(102, 65, 191);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Cursor = Cursors.Hand;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var unit = Math.Min(ClientSize.Width, ClientSize.Height) / 36f;
        g.TranslateTransform((ClientSize.Width - 36 * unit) / 2, (ClientSize.Height - 36 * unit) / 2);
        g.ScaleTransform(unit, unit);
        using var pen = new Pen(Enabled ? ForeColor : SystemColors.GrayText, 2.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        // Wand shaft & tip
        g.DrawLine(pen, 10, 26, 23, 13);
        g.DrawLine(pen, 19, 17, 22, 20);
        // Sparkles / star effect around the tip
        g.DrawLine(pen, 12, 8, 12, 14);
        g.DrawLine(pen, 9, 11, 15, 11);
        g.DrawLine(pen, 27, 21, 27, 27);
        g.DrawLine(pen, 24, 24, 30, 24);
        g.DrawLine(pen, 25, 5, 25, 9);
        g.DrawLine(pen, 23, 7, 27, 7);
    }
}

// The small affordance never takes keyboard focus from the editor it accompanies.
internal sealed class SuggestionBadge : Form
{
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 350 };
    private readonly ToolTip tips = new();
    private TextTarget? target;
    public event Action? Requested;

    public SuggestionBadge()
    {
        Text = "Lazy Type · AI suggestion";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(36, 36);
        var wand = new WandButton { Dock = DockStyle.Fill, TabStop = false };
        tips.SetToolTip(wand, "Suggest clearer wording and grammar");
        wand.Click += (_, _) => Requested?.Invoke();
        Controls.Add(wand);
        timer.Tick += (_, _) => UpdatePosition();
    }

    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x08000000 | 0x80;
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
    private readonly Label hint = new();
    private readonly TextBox suggestion;
    private readonly Button apply = new();
    private readonly Button copy = new();
    public event Action? ApplyRequested;
    public string SuggestedText => suggestion.Text;

    public SuggestionForm(string original, bool external)
    {
        Text = "Lazy Type · AI suggestion";
        Font = new Font("Segoe UI", 10);
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Color.FromArgb(246, 247, 250);
        ForeColor = Color.FromArgb(31, 42, 59);
        ClientSize = new Size(530, 442);
        MinimumSize = new Size(460, 450);
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = true;
        KeyPreview = true;
        MaximizeBox = false;
        MinimizeBox = false;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 7 };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 37));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 27));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 40));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 60));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        Controls.Add(root);

        root.Controls.Add(new Label { Text = "A little polish", Font = new Font("Segoe UI", 17, FontStyle.Bold), Dock = DockStyle.Fill }, 0, 0);
        root.Controls.Add(new Label { Text = "Your wording", Dock = DockStyle.Fill }, 0, 1);
        root.Controls.Add(Editor(original), 0, 2);
        root.Controls.Add(new Label { Text = "Suggested wording", Dock = DockStyle.Fill, Padding = new Padding(0, 6, 0, 0) }, 0, 3);
        suggestion = Editor("");
        root.Controls.Add(suggestion, 0, 4);

        hint.Text = "Loading local AI… You can keep typing when this is closed.";
        hint.Dock = DockStyle.Fill;
        hint.Padding = new Padding(0, 7, 0, 0);
        hint.Font = new Font("Segoe UI", 9);
        root.Controls.Add(hint, 0, 5);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        apply.Text = external ? "Replace dictation" : "Use suggestion";
        apply.AutoSize = true;
        apply.Height = 32;
        apply.Enabled = false;
        apply.Click += (_, _) => ApplyRequested?.Invoke();

        copy.Text = "Copy";
        copy.Width = 72;
        copy.Height = 32;
        copy.Enabled = false;
        copy.Click += (_, _) =>
        {
            if (Native.SetClipboardText(suggestion.Text)) hint.Text = "Copied. Paste it wherever you need it.";
            else hint.Text = "Clipboard is busy. Please try again.";
        };

        var dismiss = new Button { Text = "Keep original", AutoSize = true, Height = 32 };
        dismiss.Click += (_, _) => Close();
        CancelButton = dismiss;

        buttons.Controls.AddRange(new Control[] { apply, copy, dismiss });
        root.Controls.Add(buttons, 0, 6);
    }

    private static TextBox Editor(string text) => new()
    {
        Text = text,
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical,
        BackColor = Color.White,
        Dock = DockStyle.Fill,
        BorderStyle = BorderStyle.FixedSingle
    };

    public void ShowSuggestion(string text, bool changed, bool canApply)
    {
        suggestion.Text = text;
        copy.Enabled = true;
        apply.Enabled = changed && canApply;
        hint.Text = !changed
            ? "Your wording already looks good. No changes suggested."
            : canApply
                ? "Made on your PC. Review the wording before applying."
                : "This editor can't safely replace the dictation. Copy the suggestion to use it.";
    }

    public void ShowFailure(string message)
    {
        hint.Text = message;
        apply.Enabled = false;
    }

    public void SetApplying()
    {
        apply.Enabled = false;
        hint.Text = "Applying your suggestion…";
    }
}
