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
    public readonly CheckBox Clean = new();
    public readonly CheckBox Startup = new();
    public event Action? PauseRequested;
    public event Action? QuitRequested;
    public event Action<int>? HotkeyPressed;
    public event Action? ImportRequested;
    public bool Quitting;
    public MainForm()
    {
        Text = "Lazy Type"; Font = new Font("Segoe UI", 10); AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Color.FromArgb(246, 247, 250); ForeColor = Color.FromArgb(31, 42, 59);
        ClientSize = new Size(690, 742); MinimumSize = new Size(706, 780);
        StartPosition = FormStartPosition.CenterScreen;
        Icon = Native.MakeIcon(Color.FromArgb(65, 98, 211));
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(26), ColumnCount = 1, RowCount = 12 };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 55));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 88));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 47));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 41));
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
        root.Controls.Add(new Label { Text = "Memory: loads while you speak · frees after every dictation", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.FromArgb(74, 89, 112) }, 0, 5);
        var checks = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        Clean.Text = "Clean up speech locally"; Clean.AutoSize = true;
        Startup.Text = "Start with Windows"; Startup.AutoSize = true; Startup.Margin = new Padding(24, 3, 3, 3);
        checks.Controls.AddRange(new Control[] { Clean, Startup }); root.Controls.Add(checks, 0, 6);
        root.Controls.Add(new Label { Text = "LAST RESULT  ·  kept only until you quit", Dock = DockStyle.Fill, Font = new Font("Segoe UI", 9, FontStyle.Bold) }, 0, 7);
        SetEditor(Result); root.Controls.Add(Result, 0, 8);
        root.Controls.Add(new Label { Text = "Original transcript", Dock = DockStyle.Fill, Font = new Font("Segoe UI", 9) }, 0, 9);
        SetEditor(Original); Original.ReadOnly = true; root.Controls.Add(Original, 0, 10);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(0, 10, 0, 0) };
        pause.Text = "Pause && free memory"; pause.Width = 170; pause.Height = 32; pause.Click += (_, _) => PauseRequested?.Invoke();
        var copy = new Button { Text = "Copy result", Width = 105, Height = 32 }; copy.Click += (_, _) => { if (Result.TextLength > 0) try { Clipboard.SetText(Result.Text); } catch { } };
        var import = new Button { Text = "Test WAV…", Width = 102, Height = 32 }; import.Click += (_, _) => ImportRequested?.Invoke();
        var hide = new Button { Text = "Hide", Width = 70, Height = 32 }; hide.Click += (_, _) => Hide();
        var quit = new Button { Text = "Quit", Width = 65, Height = 32 }; quit.Click += (_, _) => QuitRequested?.Invoke();
        buttons.Controls.AddRange(new Control[] { pause, copy, import, hide, quit }); root.Controls.Add(buttons, 0, 11);
        HotkeyChoice.Items.AddRange(new object[] { "Ctrl+Alt+Space", "Ctrl+Shift+Space", "F8" });
        FormClosing += (_, e) => { if (!Quitting) { e.Cancel = true; Hide(); } };
    }
    private static Control Row(string title, ComboBox input)
    {
        var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 153)); row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.Controls.Add(new Label { Text = title, AutoSize = true, Margin = new Padding(0, 6, 0, 0) }, 0, 0);
        input.DropDownStyle = ComboBoxStyle.DropDownList; input.Dock = DockStyle.Top; row.Controls.Add(input, 1, 0);
        return row;
    }
    private static void SetEditor(TextBox editor) { editor.Multiline = true; editor.ScrollBars = ScrollBars.Vertical; editor.Dock = DockStyle.Fill; editor.BackColor = Color.White; editor.BorderStyle = BorderStyle.FixedSingle; }
    public void SetStatus(string title, string explanation, bool paused)
    {
        status.Text = title; detail.Text = explanation; pause.Text = paused ? "Resume dictation" : "Pause && free memory";
    }
    public void SetShortcut(string text) => shortcut.Text = text + " to start / stop  ·  add " + (text == "Ctrl+Shift+Space" ? "Alt" : "Shift") + " for raw text  ·  Esc cancels";
    public void ShowWindow() { Show(); WindowState = FormWindowState.Normal; Activate(); }
    protected override void WndProc(ref Message m) { if (m.Msg == Native.WM_HOTKEY) HotkeyPressed?.Invoke(m.WParam.ToInt32()); base.WndProc(ref m); }
}
