namespace LazyType;

internal sealed class ModelSelector : UserControl
{
    private readonly Dictionary<string, ModernCheckBox> boxes = new();
    private readonly List<Label> labels = new();
    private readonly Label detail = new() { Dock = DockStyle.Bottom, Height = 22, AutoEllipsis = true };
    private readonly ModernButton usage = new();
    private bool updating, locked, dark;
    private string selected = TextModels.Qwen35;
    public event Action? SelectionChanged;
    public event Action? UsageRequested;
    public string SelectedModel
    {
        get => selected;
        set
        {
            value = TextModels.Normalize(value);
            var changed = selected != value;
            selected = value;
            updating = true;
            try { foreach (var pair in boxes) pair.Value.Checked = pair.Key == value; }
            finally { updating = false; }
            SetStatus("Models unloaded");
            if (changed) SelectionChanged?.Invoke();
        }
    }
    // Locks the choice while recording or editing. Disabling the whole control would draw its labels in the
    // system's disabled grey, which is unreadable on the dark theme, so only the checkboxes are disabled.
    public bool Locked
    {
        get => locked;
        set
        {
            if (locked == value) return;
            locked = value;
            foreach (var box in boxes.Values) { box.Enabled = !value; box.Cursor = value ? Cursors.Default : Cursors.Hand; }
            foreach (var label in labels) label.Cursor = value ? Cursors.Default : Cursors.Hand;
            ApplyColors();
        }
    }
    public ModelSelector()
    {
        Height = 84;
        detail.Font = new Font("Segoe UI", 8.5f);
        var header = new Panel { Dock = DockStyle.Top, Height = 30, Padding = new Padding(0, 0, 0, 4) };
        var title = new Label { Text = "Clean up / Polish model", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI", 10f, FontStyle.Bold) };
        // Opens Model usage from the main window; usage stays available while the choice is locked.
        usage.Text = "Usage"; usage.AccessibleName = "Model usage";
        usage.Style = ModernButton.ButtonStyle.Secondary; usage.CornerRadius = 6;
        usage.Font = new Font("Segoe UI", 8.5f);
        usage.Dock = DockStyle.Right; usage.Width = 84;
        usage.IconDrawAction = (g, rect, col) => VectorIcons.DrawUsage(g, rect, col);
        usage.Click += (_, _) => UsageRequested?.Invoke();
        header.Controls.Add(title); header.Controls.Add(usage);
        var choices = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        choices.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        choices.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        choices.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        for (var i = 0; i < TextModels.Choices.Length; i++)
        {
            var model = TextModels.Choices[i];
            var box = new ModernCheckBox { Dock = DockStyle.Left, Width = 28, AccessibleName = model, Checked = model == selected };
            var label = new Label { Text = model,
                Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Cursor = Cursors.Hand };
            var row = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            row.Controls.Add(label); row.Controls.Add(box);
            boxes.Add(model, box); labels.Add(label);
            label.Click += (_, _) => { if (Enabled && !locked) SelectedModel = model; };
            box.CheckedChanged += (_, _) => { if (!updating) SelectedModel = model; };
            choices.Controls.Add(row, i % 2, i / 2);
        }
        Controls.Add(choices); Controls.Add(header); Controls.Add(detail);
        SetStatus("Models unloaded");
    }
    public void SetStatus(string status) => detail.Text = status == "Models unloaded"
        ? "Loads when you start dictating."
        : status;
    public void ApplyTheme(bool dark)
    {
        this.dark = dark;
        foreach (var box in boxes.Values) box.IsDark = dark;
        usage.IsDark = dark;
        ApplyColors();
    }
    private void ApplyColors()
    {
        var muted = dark ? Color.FromArgb(150, 165, 185) : Color.FromArgb(100, 116, 139);
        detail.ForeColor = muted;
        // Empty inherits the form's text colour; locked choices are muted but stay readable.
        foreach (var label in labels) label.ForeColor = locked ? muted : Color.Empty;
    }
}
