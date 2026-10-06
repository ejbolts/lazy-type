namespace LazyType;

internal sealed class ModelSelector : UserControl
{
    private readonly Dictionary<string, ModernCheckBox> boxes = new();
    private readonly List<Label> labels = new();
    private readonly Label detail = new() { Dock = DockStyle.Bottom, Height = 22, AutoEllipsis = true };
    private bool updating, locked, dark;
    private string selected = TextModels.Current;
    public event Action? SelectionChanged;
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
        Height = 108;
        detail.Font = new Font("Segoe UI", 8.5f);
        var title = new Label { Text = "Text model", Dock = DockStyle.Top, Height = 24, Font = new Font("Segoe UI", 10f, FontStyle.Bold) };
        var choices = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = Padding.Empty };
        choices.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        choices.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        choices.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        choices.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        for (var i = 0; i < TextModels.Choices.Length; i++)
        {
            var model = TextModels.Choices[i];
            var box = new ModernCheckBox { Dock = DockStyle.Left, Width = 28, AccessibleName = model, Checked = model == selected };
            var label = new Label { Text = model == TextModels.Current ? model + " (current)" : model,
                Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Cursor = Cursors.Hand };
            var row = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            row.Controls.Add(label); row.Controls.Add(box);
            boxes.Add(model, box); labels.Add(label);
            label.Click += (_, _) => { if (Enabled && !locked) SelectedModel = model; };
            box.CheckedChanged += (_, _) => { if (!updating) SelectedModel = model; };
            choices.Controls.Add(row, i % 2, i / 2);
        }
        Controls.Add(choices); Controls.Add(title); Controls.Add(detail);
        SetStatus("Models unloaded");
    }
    public void SetStatus(string status) => detail.Text = status == "Models unloaded"
        ? selected == TextModels.Dynamic ? "Qwen first; load Gemma 5s after Qwen is ready." : "Loads when you start dictating."
        : status;
    public void ApplyTheme(bool dark)
    {
        this.dark = dark;
        foreach (var box in boxes.Values) box.IsDark = dark;
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
