namespace LazyType;

internal sealed class MainForm : Form
{
    private readonly Label status = new();
    private readonly Label detail = new();
    private readonly Button pause = new();
    private readonly Label shortcut = new();
    public readonly TextBox Result = new();
    public readonly TextBox Original = new();
    public readonly ComboBox Mic = new();
    public readonly ComboBox HotkeyChoice = new();
    public readonly ComboBox ThemeChoice = new();
    public readonly ComboBox PopupThemeChoice = new();
    public event Action? PreviewPopupRequested;
    public readonly CheckBox Clean = new();
    public readonly CheckBox Suggestions = new();
    public readonly CheckBox Startup = new();
    private readonly WandButton suggest = new();
    private readonly ToolTip tips = new();
    private readonly ToolTip memoryTip = new()
    {
        ToolTipTitle = "Memory with all models loaded",
        InitialDelay = 350, ReshowDelay = 100, AutoPopDelay = 20000, ShowAlways = true
    };
    private readonly Label memoryInfo = new() { Text = "ⓘ", AccessibleName = "Model memory information", Dock = DockStyle.Right, Width = 28, TextAlign = ContentAlignment.MiddleCenter, Cursor = Cursors.Help, ForeColor = Color.FromArgb(65, 98, 211), TabStop = true };
    private bool suggestionBusy;
    public event Action? PauseRequested;
    public event Action? QuitRequested;
    public event Action<int>? HotkeyPressed;
    public event Action? ImportRequested;
    public event Action? SuggestionRequested;
    public bool Quitting;

    public MainForm()
    {
        Text = "Lazy Type"; Font = new Font("Segoe UI", 10); AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Color.FromArgb(246, 247, 250); ForeColor = Color.FromArgb(31, 42, 59);
        ClientSize = new Size(690, 866); MinimumSize = new Size(706, 904);
        StartPosition = FormStartPosition.CenterScreen;
        Icon = Native.MakeIcon(Color.FromArgb(65, 98, 211));

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 15, Padding = new Padding(22, 18, 22, 18) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 102));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 41));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 41));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 41));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 41));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 65));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 35));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        Controls.Add(root);

        var heading = new Label { Text = "Lazy Type", Font = new Font("Segoe UI", 23, FontStyle.Bold), Dock = DockStyle.Fill };
        root.Controls.Add(heading, 0, 0);

        var card = new Panel { BackColor = Color.White, Dock = DockStyle.Fill, Padding = new Padding(14) };
        status.Text = "Getting ready"; status.Font = new Font("Segoe UI", 13, FontStyle.Bold); status.SetBounds(14, 12, 565, 26);
        detail.SetBounds(14, 42, 595, 38); detail.Font = new Font("Segoe UI", 9); detail.Text = "Whisper Turbo + Qwen 3 · Fully local · Microphone off";
        card.Controls.AddRange(new Control[] { status, detail }); root.Controls.Add(card, 0, 1);

        shortcut.Dock = DockStyle.Fill; shortcut.TextAlign = ContentAlignment.MiddleLeft; root.Controls.Add(shortcut, 0, 2);
        root.Controls.Add(Row("Microphone", Mic), 0, 3);
        root.Controls.Add(Row("Dictation hotkey", HotkeyChoice), 0, 4);
        root.Controls.Add(Row("App theme", ThemeChoice), 0, 5);

        var popupRow = Row("Popup theme", PopupThemeChoice);
        var preview = new Button { Text = "Preview", Dock = DockStyle.Right, Width = 85 };
        preview.Click += (_, _) => PreviewPopupRequested?.Invoke();
        var popupPanel = new Panel { Dock = DockStyle.Fill };
        popupPanel.Controls.Add(popupRow); popupPanel.Controls.Add(preview);
        root.Controls.Add(popupPanel, 0, 6);

        var memoryRow = new Panel { Dock = DockStyle.Fill };
        var memoryNote = new Label { Text = "Memory: loads on demand · frees after dictation or suggestions", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        const string memoryDetails = "RAM: approximately 1.0 GiB for the model workers (app extra).\nGPU: approximately 4.2 GiB of additional VRAM.\nWhisper + Qwen + speech detection · RTX 4080, 5 Oct 2026.\nWarm sample after a short test; not a live reading or peak.\nModels unload after each dictation. Usage varies.";
        memoryTip.SetToolTip(memoryNote, memoryDetails);
        memoryTip.SetToolTip(memoryInfo, memoryDetails);
        memoryInfo.AccessibleDescription = memoryDetails;
        memoryInfo.Enter += (_, _) => memoryTip.Show(memoryDetails, memoryInfo, 0, memoryInfo.Height, 20000);
        memoryInfo.Leave += (_, _) => memoryTip.Hide(memoryInfo);
        memoryRow.Controls.Add(memoryNote); memoryRow.Controls.Add(memoryInfo);
        root.Controls.Add(memoryRow, 0, 7);

        var checks = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        Clean.Text = "Clean up speech locally"; Clean.AutoSize = true;
        Startup.Text = "Start with Windows"; Startup.AutoSize = true; Startup.Margin = new Padding(24, 3, 3, 3);
        checks.Controls.AddRange(new Control[] { Clean, Startup }); root.Controls.Add(checks, 0, 8);

        Suggestions.Text = "AI suggestions · show a wand after dictation"; Suggestions.AutoSize = true;
        tips.SetToolTip(Suggestions, "Offer a local rewrite when you click the wand. You review it before applying.");
        root.Controls.Add(Suggestions, 0, 9);

        root.Controls.Add(new Label { Text = "LAST RESULT  ·  kept only until you quit", Dock = DockStyle.Fill, Font = new Font("Segoe UI", 9, FontStyle.Bold) }, 0, 10);

        var resultPanel = new Panel { Dock = DockStyle.Fill, Margin = new Padding(3) };
        var resultActions = new Panel { Dock = DockStyle.Right, Width = 43 };
        suggest.Dock = DockStyle.Bottom; suggest.Height = 36; suggest.Visible = false;
        tips.SetToolTip(suggest, "Suggest clearer wording and grammar");
        suggest.Click += (_, _) => SuggestionRequested?.Invoke();
        resultActions.Controls.Add(suggest);
        SetEditor(Result);
        resultPanel.Controls.Add(Result);
        resultPanel.Controls.Add(resultActions);
        root.Controls.Add(resultPanel, 0, 11);

        Suggestions.CheckedChanged += (_, _) => { resultActions.Visible = Suggestions.Checked; UpdateSuggestionButton(); };
        Result.TextChanged += (_, _) => UpdateSuggestionButton();
        resultActions.Visible = false;

        root.Controls.Add(new Label { Text = "Original transcript", Dock = DockStyle.Fill, Font = new Font("Segoe UI", 9) }, 0, 12);
        SetEditor(Original); Original.ReadOnly = true; root.Controls.Add(Original, 0, 13);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(0, 10, 0, 0) };
        pause.Text = "Pause && free memory"; pause.Width = 170; pause.Height = 32; pause.Click += (_, _) => PauseRequested?.Invoke();
        var copy = new Button { Text = "Copy result", Width = 105, Height = 32 }; copy.Click += (_, _) => { if (Result.TextLength > 0) try { Clipboard.SetText(Result.Text); } catch { } };
        var import = new Button { Text = "Test WAV…", Width = 102, Height = 32 }; import.Click += (_, _) => ImportRequested?.Invoke();
        var hide = new Button { Text = "Hide", Width = 70, Height = 32 }; hide.Click += (_, _) => Hide();
        var quit = new Button { Text = "Quit", Width = 65, Height = 32 }; quit.Click += (_, _) => QuitRequested?.Invoke();
        buttons.Controls.AddRange(new Control[] { pause, copy, import, hide, quit }); root.Controls.Add(buttons, 0, 14);

        HotkeyChoice.Items.AddRange(new object[] { "Ctrl+Alt+Space", "Ctrl+Shift+Space", "F8" });
        ThemeChoice.Items.AddRange(new object[] { "System", "Light", "Dark" });
        PopupThemeChoice.Items.AddRange(new object[] { "Follow app", "Light", "Dark" });

        FormClosing += (_, e) => { if (!Quitting) { e.Cancel = true; Hide(); } };
    }

    private static Control Row(string title, ComboBox input)
    {
        var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 153)); row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.Controls.Add(new Label { Text = title, AutoSize = true, Margin = new Padding(0, 6, 0, 0) }, 0, 0);
        input.AccessibleName = title; input.DropDownStyle = ComboBoxStyle.DropDownList; input.Dock = DockStyle.Top; row.Controls.Add(input, 1, 0);
        return row;
    }

    private static void SetEditor(TextBox editor) { editor.Multiline = true; editor.ScrollBars = ScrollBars.Vertical; editor.Dock = DockStyle.Fill; editor.BackColor = Color.White; editor.BorderStyle = BorderStyle.FixedSingle; }

    public void ApplyTheme(bool dark)
    {
        var background = dark ? Color.FromArgb(25, 28, 35) : Color.FromArgb(246, 247, 250);
        var surface = dark ? Color.FromArgb(38, 43, 53) : Color.White;
        var foreground = dark ? Color.FromArgb(235, 239, 246) : Color.FromArgb(31, 42, 59);
        void PaintControls(Control parent)
        {
            foreach (Control control in parent.Controls)
            {
                if (control is WandButton wand)
                {
                    wand.BackColor = dark ? Color.FromArgb(50, 40, 80) : Color.FromArgb(239, 235, 255);
                    wand.ForeColor = dark ? Color.FromArgb(180, 150, 255) : Color.FromArgb(102, 65, 191);
                    continue;
                }
                if (control == memoryInfo)
                {
                    memoryInfo.ForeColor = dark ? Color.FromArgb(145, 175, 255) : Color.FromArgb(65, 98, 211);
                    continue;
                }
                control.ForeColor = foreground;
                control.BackColor = control is TextBox or ComboBox or Button || control == status.Parent ? surface : parent.BackColor;
                if (control is Button button)
                {
                    button.UseVisualStyleBackColor = false;
                    button.FlatStyle = FlatStyle.Flat;
                    button.FlatAppearance.BorderColor = dark ? Color.FromArgb(80, 88, 105) : Color.FromArgb(200, 205, 215);
                }
                if (control is ComboBox combo) combo.FlatStyle = FlatStyle.Flat;
                PaintControls(control);
            }
        }
        BackColor = background; ForeColor = foreground; PaintControls(this);
        var useDark = dark ? 1 : 0;
        Native.DwmSetWindowAttribute(Handle, 20, ref useDark, sizeof(int));
        Invalidate(true);
    }

    public void SetStatus(string title, string explanation, bool paused)
    {
        status.Text = title; detail.Text = explanation; pause.Text = paused ? "Resume dictation" : "Pause && free memory";
    }

    public void SetShortcut(string text) => shortcut.Text = text + " to start / stop  ·  add " + (text == "Ctrl+Shift+Space" ? "Alt" : "Shift") + " for raw text  ·  Esc cancels";
    public void SetSuggestionBusy(bool busy) { suggestionBusy = busy; UpdateSuggestionButton(); }
    private void UpdateSuggestionButton()
    {
        suggest.Visible = Suggestions.Checked && !string.IsNullOrWhiteSpace(Result.Text);
        suggest.Enabled = !suggestionBusy;
    }
    public void ShowWindow() { Show(); WindowState = FormWindowState.Normal; Activate(); }
    protected override void WndProc(ref Message m) { if (m.Msg == Native.WM_HOTKEY) HotkeyPressed?.Invoke(m.WParam.ToInt32()); base.WndProc(ref m); }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { tips.Dispose(); memoryTip.Dispose(); }
        base.Dispose(disposing);
    }
}
