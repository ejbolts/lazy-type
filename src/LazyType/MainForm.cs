using System.Drawing.Drawing2D;

namespace LazyType;

internal sealed class MainForm : Form
{
    private readonly Label greeting = new();
    private readonly Label statusDetail = new();
    private readonly BufferedPanel statusDot = new();
    private Color dotColor = Color.FromArgb(16, 185, 129); // Green for ready

    // Dictation shortcut card
    private readonly BufferedPanel shortcutCard = new();
    private string currentHotkey = "Ctrl+Alt+Space";

    // Setting rows controls
    public readonly ComboBox Mic = new();
    public readonly ModernCheckBox Clean = new();
    public readonly ComboBox EditModeChoice = new();
    public readonly ModernCheckBox Suggestions = new();
    public readonly ModernCheckBox Startup = new();
    public readonly ModelSelector Models = new();
    public readonly ComboBox HotkeyChoice = new();
    public readonly ComboBox ThemeChoice = new();
    public readonly ComboBox PopupThemeChoice = new();

    // Last result card
    private readonly BufferedPanel resultCard = new();
    private readonly BufferedPanel editorPanel = new();
    public readonly TextBox Result = new();
    public readonly TextBox Original = new();
    private readonly WandButton suggest = new();
    private readonly ModernButton copyBtn = new();
    private readonly ModernButton cleanBtn = new();
    private readonly ModernButton rewordBtn = new();
    private readonly System.Windows.Forms.Timer copyFeedbackTimer = new() { Interval = 1600 };
    private readonly BufferedPanel accordionHeader = new();
    private readonly Label accordionLabel = new();
    private readonly BufferedPanel accordionBody = new();
    private bool accordionExpanded;

    // Footer
    private readonly ModernButton pauseBtn = new();
    private readonly ModernButton hideBtn = new();
    private readonly ModernButton gearBtn = new();
    private readonly ContextMenuStrip settingsMenu = new();

    private readonly ToolTip tips = new();
    private bool isDark;
    private bool isPaused;
    private bool suggestionBusy;

    public event Action? PauseRequested;
    public event Action? QuitRequested;
    public event Action<int>? HotkeyPressed;
    public event Action? ImportRequested;
    public event Action? SuggestionRequested;
    public event Action<string>? RewriteRequested;
    public event Action? PreviewPopupRequested;
    public bool Quitting;

    public MainForm()
    {
        Text = "Lazy Type";
        Font = new Font("Segoe UI", 10f);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(580, 720);
        MinimumSize = new Size(520, 680);
        StartPosition = FormStartPosition.CenterScreen;
        Icon = Native.MakeIcon(Color.FromArgb(127, 86, 217));
        DoubleBuffered = true;

        Clean.Text = string.Empty;
        Suggestions.Text = string.Empty;
        Startup.Text = string.Empty;

        var root = new BufferedPanel { Dock = DockStyle.Fill, Padding = new Padding(24, 20, 24, 16) };
        Controls.Add(root);

        // --- 1. Header (Greeting + status dot + explanation) ---
        var headerPanel = new BufferedPanel { Dock = DockStyle.Top, Height = 72 };

        statusDot.SetBounds(0, 8, 12, 12);
        statusDot.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var b = new SolidBrush(dotColor);
            e.Graphics.FillEllipse(b, 1, 1, 10, 10);
        };

        greeting.Text = "Ready when you are";
        greeting.Font = new Font("Segoe UI", 16f, FontStyle.Bold);
        greeting.SetBounds(20, 0, 480, 32);

        statusDetail.Text = "Everything stays on your device.";
        statusDetail.Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);
        statusDetail.SetBounds(22, 32, 500, 32);
        statusDetail.AutoEllipsis = true;
        headerPanel.Resize += (_, _) => statusDetail.Width = Math.Max(0, headerPanel.ClientSize.Width - 22);

        headerPanel.Controls.Add(statusDot);
        headerPanel.Controls.Add(greeting);
        headerPanel.Controls.Add(statusDetail);

        // --- 2. Interactive Dictation Shortcut Card ---
        shortcutCard.Dock = DockStyle.Top;
        shortcutCard.Height = 52;
        shortcutCard.Paint += PaintShortcutCard;



        // --- 3. Settings Rows ---
        var settingsContainer = new BufferedPanel { Dock = DockStyle.Top, Height = 210, Padding = new Padding(0, 10, 0, 10) };

        // Setting 1: Microphone
        var micRow = CreateSettingRow(g => VectorIcons.DrawMicrophone(g, new Rectangle(0, 0, 20, 20), GetIconColor()),
            "Microphone", null, Mic);
        Mic.DropDownStyle = ComboBoxStyle.DropDownList;
        Mic.Width = 220;
        Mic.Dock = DockStyle.Right;

        // Setting 2: Clean up speech
        var cleanRow = CreateSettingRow(g => VectorIcons.DrawCleanSpeech(g, new Rectangle(0, 0, 20, 20), GetIconColor()),
            "Clean up speech", null, Clean);
        EditModeChoice.DropDownStyle = ComboBoxStyle.DropDownList;
        EditModeChoice.Items.AddRange(new object[] { TextEditModes.Cleanup, TextEditModes.Reword });
        EditModeChoice.SelectedIndex = 0;
        EditModeChoice.Enabled = false;
        EditModeChoice.AccessibleName = "Speech editing mode";
        EditModeChoice.Width = 110;
        EditModeChoice.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        cleanRow.Controls.Add(EditModeChoice);
        EditModeChoice.BringToFront();
        cleanRow.Resize += (_, _) => EditModeChoice.SetBounds(cleanRow.Width - 154, 8, 110, 28);
        tips.SetToolTip(EditModeChoice, "Clean up keeps your wording. Reword removes repetition and reorganises ideas while preserving meaning.");
        Clean.AccessibleName = "Edit speech";
        Clean.CheckedChanged += (_, _) => EditModeChoice.Enabled = Clean.Checked && !suggestionBusy;

        // Setting 3: AI suggestions
        var suggestionsRow = CreateSettingRow(g => PolishIcon.Draw(g, new Rectangle(0, 0, 20, 20), Color.FromArgb(127, 86, 217)),
            "AI suggestions", "Check wording after dictation and mark suggested changes", Suggestions);

        // Setting 4: Start with Windows
        var startupRow = CreateSettingRow(g => VectorIcons.DrawWindowsLogo(g, new Rectangle(0, 0, 18, 18), GetIconColor()),
            "Start with Windows", null, Startup);

        settingsContainer.Controls.Add(startupRow);
        settingsContainer.Controls.Add(suggestionsRow);
        settingsContainer.Controls.Add(cleanRow);
        settingsContainer.Controls.Add(micRow);

        // Layout rows vertically
        micRow.Dock = DockStyle.Top;
        cleanRow.Dock = DockStyle.Top;
        suggestionsRow.Dock = DockStyle.Top;
        startupRow.Dock = DockStyle.Top;

        // --- 4. Last result card ---
        resultCard.Dock = DockStyle.Fill;
        resultCard.Padding = new Padding(16, 12, 16, 12);
        resultCard.Paint += PaintResultCard;

        var resultHeader = new BufferedPanel { Dock = DockStyle.Top, Height = 32 };
        var resultTitle = new Label { Text = "Last result", Font = new Font("Segoe UI", 10.5f, FontStyle.Bold), AutoSize = false };
        resultTitle.SetBounds(0, 4, 150, 24);

        copyBtn.Text = "Copy";
        copyBtn.Style = ModernButton.ButtonStyle.Secondary;
        copyBtn.CornerRadius = 6;
        copyBtn.SetBounds(resultHeader.Width - 96, 0, 92, 28);
        copyBtn.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        copyBtn.IconDrawAction = (g, rect, col) => VectorIcons.DrawCopy(g, rect, col);
        copyBtn.Click += (_, _) =>
        {
            if (!string.IsNullOrEmpty(Result.Text) && Native.SetClipboardText(Result.Text))
            {
                copyBtn.Text = "Copied!";
                copyFeedbackTimer.Stop();
                copyFeedbackTimer.Start();
            }
        };
        copyFeedbackTimer.Tick += (_, _) =>
        {
            copyFeedbackTimer.Stop();
            copyBtn.Text = "Copy";
        };

        cleanBtn.Text = TextEditModes.Cleanup;
        rewordBtn.Text = TextEditModes.Reword;
        foreach (var button in new[] { cleanBtn, rewordBtn })
        {
            button.Style = ModernButton.ButtonStyle.Secondary;
            button.CornerRadius = 6;
            button.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            button.Click += (_, _) => RewriteRequested?.Invoke(button.Text);
            resultHeader.Controls.Add(button);
        }
        resultHeader.Resize += (_, _) =>
        {
            cleanBtn.SetBounds(resultHeader.Width - 290, 0, 92, 28);
            rewordBtn.SetBounds(resultHeader.Width - 192, 0, 92, 28);
        };
        tips.SetToolTip(rewordBtn, "Remove rambling and repetition, reorganise ideas, and review before applying.");
        Result.AccessibleName = "Last result";

        resultHeader.Controls.Add(resultTitle);
        resultHeader.Controls.Add(copyBtn);

        // Result editor area
        editorPanel.Dock = DockStyle.Fill;
        editorPanel.Padding = new Padding(0, 6, 0, 6);
        Result.Multiline = true;
        Result.ScrollBars = ScrollBars.Vertical;
        Result.Dock = DockStyle.Fill;
        Result.BorderStyle = BorderStyle.None;
        Result.Font = new Font("Segoe UI", 10.5f);

        // Floating polish button inside result card
        suggest.SetBounds(editorPanel.Width - 46, editorPanel.Height - 46, 36, 36);
        suggest.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        suggest.Visible = false;
        suggest.Click += (_, _) => SuggestionRequested?.Invoke();
        tips.SetToolTip(suggest, "A little polish · Refine wording with dual sparkle stars");

        editorPanel.Controls.Add(Result);
        editorPanel.Controls.Add(suggest);
        editorPanel.Resize += (_, _) =>
        {
            suggest.Location = new Point(editorPanel.Width - 44, editorPanel.Height - 44);
            suggest.BringToFront();
        };

        // Accordion for Original transcript
        var accordionContainer = new BufferedPanel { Dock = DockStyle.Bottom, AutoSize = true };
        accordionHeader.Dock = DockStyle.Top;
        accordionHeader.Height = 28;
        accordionHeader.Cursor = Cursors.Hand;
        accordionHeader.Paint += PaintAccordionHeader;
        accordionHeader.Click += (_, _) => ToggleAccordion();

        accordionLabel.Text = "Original transcript";
        accordionLabel.Font = new Font("Segoe UI", 9f);
        accordionLabel.SetBounds(22, 4, 200, 20);
        accordionLabel.Cursor = Cursors.Hand;
        accordionLabel.Click += (_, _) => ToggleAccordion();
        accordionHeader.Controls.Add(accordionLabel);

        accordionBody.Dock = DockStyle.Top;
        accordionBody.Height = 70;
        accordionBody.Visible = false;
        accordionBody.Padding = new Padding(0, 4, 0, 0);

        Original.Multiline = true;
        Original.ScrollBars = ScrollBars.Vertical;
        Original.ReadOnly = true;
        Original.Dock = DockStyle.Fill;
        Original.BorderStyle = BorderStyle.None;
        Original.Font = new Font("Segoe UI", 9.5f);
        accordionBody.Controls.Add(Original);

        accordionContainer.Controls.Add(accordionBody);
        accordionContainer.Controls.Add(accordionHeader);

        resultCard.Controls.Add(editorPanel);
        resultCard.Controls.Add(accordionContainer);
        resultCard.Controls.Add(resultHeader);

        // --- 5. Footer Bar ---
        var footerBar = new BufferedPanel { Dock = DockStyle.Bottom, Height = 50, Padding = new Padding(0, 10, 0, 0) };

        pauseBtn.Text = "Pause dictation";
        pauseBtn.Style = ModernButton.ButtonStyle.Secondary;
        pauseBtn.SetBounds(0, 10, 140, 36);
        pauseBtn.IconDrawAction = (g, rect, col) => { if (isPaused) VectorIcons.DrawPlay(g, rect, col); else VectorIcons.DrawPause(g, rect, col); };
        pauseBtn.Click += (_, _) => PauseRequested?.Invoke();

        hideBtn.Text = "Hide";
        hideBtn.Style = ModernButton.ButtonStyle.Secondary;
        hideBtn.SetBounds(148, 10, 80, 36);
        hideBtn.IconDrawAction = (g, rect, col) => VectorIcons.DrawEye(g, rect, col);
        hideBtn.Click += (_, _) => Hide();

        gearBtn.Text = "";
        gearBtn.Style = ModernButton.ButtonStyle.IconOnly;
        gearBtn.SetBounds(footerBar.Width - 44, 10, 38, 36);
        gearBtn.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        gearBtn.IconDrawAction = (g, rect, col) => VectorIcons.DrawGear(g, rect, col);
        gearBtn.Click += (_, _) => settingsMenu.Show(gearBtn, new Point(gearBtn.Width - settingsMenu.Width, gearBtn.Height + 4));

        footerBar.Controls.AddRange(new Control[] { pauseBtn, hideBtn, gearBtn });

        // Assemble root layout
        root.Controls.Add(resultCard);
        Models.Dock = DockStyle.Top;
        root.Controls.Add(Models);
        root.Controls.Add(settingsContainer);
        root.Controls.Add(shortcutCard);
        root.Controls.Add(headerPanel);
        root.Controls.Add(footerBar);

        // Setup hidden combos for ThemeController compatibility & settings menu
        HotkeyChoice.Items.Add("Ctrl+Alt+Space");
        ThemeChoice.Items.AddRange(new object[] { "System", "Light", "Dark" });
        PopupThemeChoice.Items.AddRange(new object[] { "Follow app", "Light", "Dark" });

        SetupSettingsMenu();

        Suggestions.CheckedChanged += (_, _) => UpdateSuggestionButton();
        Result.TextChanged += (_, _) => UpdateSuggestionButton();

        FormClosing += (_, e) => { if (!Quitting) { e.Cancel = true; Hide(); } };
        UpdateSuggestionButton();
        Shown += (_, _) =>
        {
            var area = Screen.FromControl(this).WorkingArea;
            if (Height > area.Height) Height = Math.Max(MinimumSize.Height, area.Height);
        };
    }



    private Color GetIconColor() => isDark ? Color.FromArgb(170, 180, 200) : Color.FromArgb(102, 112, 133);

    private Panel CreateSettingRow(Action<Graphics> drawIcon, string title, string? subtitle, Control rightControl)
    {
        var row = new BufferedPanel { Height = subtitle != null ? 52 : 44, Padding = new Padding(4, 4, 4, 4) };

        var iconBox = new BufferedPanel { Width = 28, Height = 28, Location = new Point(4, (row.Height - 28) / 2) };
        iconBox.Paint += (_, e) => drawIcon(e.Graphics);

        int labelX = 36;
        var lblTitle = new Label
        {
            Text = title,
            Font = new Font("Segoe UI", 10f, FontStyle.Regular),
            AutoSize = false,
            Location = new Point(labelX, subtitle != null ? 6 : (row.Height - 24) / 2),
            Size = new Size(240, 22),
            TextAlign = ContentAlignment.MiddleLeft,
            Cursor = (rightControl is ModernCheckBox) ? Cursors.Hand : Cursors.Default
        };

        if (rightControl is ModernCheckBox cb)
        {
            cb.Text = string.Empty;
            lblTitle.Click += (_, _) => { if (cb.Enabled) cb.Checked = !cb.Checked; };
            iconBox.Click += (_, _) => { if (cb.Enabled) cb.Checked = !cb.Checked; };
            row.Click += (_, _) => { if (cb.Enabled) cb.Checked = !cb.Checked; };
            cb.Location = new Point(row.Width - 28, (row.Height - 22) / 2);
            cb.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            cb.Size = new Size(22, 22);
        }
        else
        {
            rightControl.Location = new Point(row.Width - rightControl.Width - 4, (row.Height - rightControl.Height) / 2);
            rightControl.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        }

        row.Controls.Add(iconBox);
        row.Controls.Add(lblTitle);
        row.Resize += (_, _) => lblTitle.Width = Math.Max(60, row.ClientSize.Width - labelX - rightControl.Width - 20);
        if (subtitle != null)
        {
            var lblSub = new Label
            {
                Text = subtitle,
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = isDark ? Color.FromArgb(150, 160, 180) : Color.FromArgb(120, 130, 145),
                AutoSize = false,
                Location = new Point(labelX, 28),
                Size = new Size(340, 18),
                TextAlign = ContentAlignment.MiddleLeft,
                Cursor = (rightControl is ModernCheckBox) ? Cursors.Hand : Cursors.Default
            };
            if (rightControl is ModernCheckBox cb2) lblSub.Click += (_, _) => { if (cb2.Enabled) cb2.Checked = !cb2.Checked; };
            row.Controls.Add(lblSub);
        }
        row.Controls.Add(rightControl);
        return row;
    }

    private void ToggleAccordion()
    {
        accordionExpanded = !accordionExpanded;
        accordionBody.Visible = accordionExpanded;
        accordionHeader.Invalidate();
    }

    private void PaintAccordionHeader(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        Color col = isDark ? Color.FromArgb(170, 180, 200) : Color.FromArgb(102, 112, 133);
        VectorIcons.DrawChevron(g, new Rectangle(4, 7, 14, 14), col, accordionExpanded);
    }

    private void PaintShortcutCard(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var bounds = new Rectangle(0, 0, shortcutCard.Width - 1, shortcutCard.Height - 1);
        Color cardBg = isDark ? Color.FromArgb(28, 33, 46) : Color.White;
        Color cardBorder = isDark ? Color.FromArgb(45, 52, 68) : Color.FromArgb(230, 235, 245);

        using var bgBrush = new SolidBrush(cardBg);
        DrawingHelpers.FillRoundedRectangle(g, bgBrush, bounds, 10);
        using var borderPen = new Pen(cardBorder, 1.2f);
        DrawingHelpers.DrawRoundedRectangle(g, borderPen, bounds, 10);

        var keys = currentHotkey.Split('+');
        int x = 12;
        int y = (shortcutCard.Height - 26) / 2;

        Color keyBg = isDark ? Color.FromArgb(42, 49, 66) : Color.FromArgb(243, 244, 248);
        Color keyBorder = isDark ? Color.FromArgb(60, 70, 90) : Color.FromArgb(215, 220, 230);
        Color keyFg = isDark ? Color.FromArgb(230, 235, 245) : Color.FromArgb(50, 60, 80);
        using var kBrush = new SolidBrush(keyBg);
        using var kPen = new Pen(keyBorder, 1f);
        using var keyFont = new Font("Segoe UI", 9f, FontStyle.Bold);

        for (int i = 0; i < keys.Length; i++)
        {
            var keyText = keys[i];
            int keyW = Math.Max(36, TextRenderer.MeasureText(keyText, Font).Width + 16);
            var kRect = new Rectangle(x, y, keyW, 26);

            DrawingHelpers.FillRoundedRectangle(g, kBrush, kRect, 5);
            DrawingHelpers.DrawRoundedRectangle(g, kPen, kRect, 5);
            TextRenderer.DrawText(g, keyText, keyFont, kRect, keyFg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

            x += keyW + 6;
            if (i < keys.Length - 1)
            {
                TextRenderer.DrawText(g, "+", Font, new Rectangle(x - 2, y, 10, 26), keyFg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                x += 12;
            }
        }

        Color actionFg = isDark ? Color.FromArgb(170, 180, 200) : Color.FromArgb(102, 112, 133);
        var actionRect = new Rectangle(shortcutCard.Width - 194, 0, 180, shortcutCard.Height);
        using var actionFont = new Font("Segoe UI", 9.5f, FontStyle.Regular);
        TextRenderer.DrawText(g, "Start or stop dictation", actionFont, actionRect, actionFg, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
    }

    private void PaintResultCard(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var bounds = new Rectangle(0, 0, resultCard.Width - 1, resultCard.Height - 1);
        Color cardBg = isDark ? Color.FromArgb(24, 28, 38) : Color.White;
        Color cardBorder = isDark ? Color.FromArgb(45, 52, 68) : Color.FromArgb(230, 235, 245);

        using var bgBrush = new SolidBrush(cardBg);
        DrawingHelpers.FillRoundedRectangle(g, bgBrush, bounds, 12);
        using var borderPen = new Pen(cardBorder, 1.2f);
        DrawingHelpers.DrawRoundedRectangle(g, borderPen, bounds, 12);
    }

    private void SetupSettingsMenu()
    {
        settingsMenu.Items.Clear();

        var themeHeader = new ToolStripMenuItem("Theme") { Enabled = false };
        settingsMenu.Items.Add(themeHeader);
        foreach (string t in new[] { "System", "Light", "Dark" })
        {
            var item = new ToolStripMenuItem("  " + t, null, (_, _) => { ThemeChoice.SelectedItem = t; });
            settingsMenu.Items.Add(item);
        }

        settingsMenu.Items.Add(new ToolStripSeparator());

        var popupHeader = new ToolStripMenuItem("Popup theme") { Enabled = false };
        settingsMenu.Items.Add(popupHeader);
        foreach (string pt in new[] { "Follow app", "Light", "Dark" })
        {
            var item = new ToolStripMenuItem("  " + pt, null, (_, _) => { PopupThemeChoice.SelectedItem = pt; });
            settingsMenu.Items.Add(item);
        }

        settingsMenu.Items.Add(new ToolStripSeparator());



        settingsMenu.Items.Add("Preview suggestion popup", null, (_, _) => PreviewPopupRequested?.Invoke());
        settingsMenu.Items.Add("Test with WAV audio…", null, (_, _) => ImportRequested?.Invoke());

        settingsMenu.Items.Add(new ToolStripSeparator());
        settingsMenu.Items.Add("Quit Lazy Type", null, (_, _) => QuitRequested?.Invoke());
    }

    public void ApplyTheme(bool dark)
    {
        isDark = dark;
        BackColor = dark ? Color.FromArgb(16, 20, 28) : Color.FromArgb(248, 249, 252);
        ForeColor = dark ? Color.FromArgb(240, 242, 248) : Color.FromArgb(30, 41, 59);

        greeting.ForeColor = ForeColor;
        statusDetail.ForeColor = dark ? Color.FromArgb(150, 165, 185) : Color.FromArgb(100, 116, 139);
        accordionLabel.ForeColor = dark ? Color.FromArgb(160, 175, 200) : Color.FromArgb(100, 116, 139);

        Clean.IsDark = dark;
        Suggestions.IsDark = dark;
        Startup.IsDark = dark;
        Models.ApplyTheme(dark);

        Color cardBack = dark ? Color.FromArgb(24, 28, 38) : Color.White;
        editorPanel.BackColor = cardBack;
        Result.BackColor = cardBack;
        Result.ForeColor = ForeColor;

        Original.BackColor = dark ? Color.FromArgb(20, 24, 32) : Color.FromArgb(245, 247, 250);
        Original.ForeColor = dark ? Color.FromArgb(180, 190, 205) : Color.FromArgb(70, 80, 95);

        pauseBtn.IsDark = dark;
        hideBtn.IsDark = dark;
        gearBtn.IsDark = dark;
        copyBtn.IsDark = dark;
        cleanBtn.IsDark = dark;
        rewordBtn.IsDark = dark;

        suggest.IsDark = dark;

        int useDark = dark ? 1 : 0;
        Native.DwmSetWindowAttribute(Handle, 20, ref useDark, sizeof(int));

        shortcutCard.Invalidate();
        resultCard.Invalidate();
        accordionHeader.Invalidate();
        Invalidate(true);
    }

    public void SetStatus(string title, string explanation, bool paused)
    {
        isPaused = paused;
        greeting.Text = paused ? "Dictation paused" : title.Contains("Listening") ? "Listening…" : "Ready when you are";
        statusDetail.Text = explanation;

        if (paused)
        {
            dotColor = Color.FromArgb(245, 158, 11); // Orange
            pauseBtn.Text = "Resume dictation";
        }
        else if (title.Contains("Listening") || title.Contains("recording"))
        {
            dotColor = Color.FromArgb(239, 68, 68); // Red
            pauseBtn.Text = "Pause dictation";
        }
        else if (title.Contains("Clean") || title.Contains("Reword") || title.Contains("Suggesting") || title.Contains("Transcrib"))
        {
            dotColor = Color.FromArgb(127, 86, 217); // Purple
            pauseBtn.Text = "Pause dictation";
        }
        else
        {
            dotColor = Color.FromArgb(16, 185, 129); // Green
            pauseBtn.Text = "Pause dictation";
        }

        statusDot.Invalidate();
        pauseBtn.Invalidate();
    }

    public void SetShortcut(string text)
    {
        currentHotkey = text;
        shortcutCard.Invalidate();
    }

    public void SetSuggestionBusy(bool busy)
    {
        suggestionBusy = busy;
        Clean.Enabled = !busy;
        EditModeChoice.Enabled = !busy && Clean.Checked;
        UpdateSuggestionButton();
    }

    public WandButton SuggestButton => suggest;
    public bool SuggestVisible => suggest.Visible;
    public Rectangle SuggestBounds => suggest.Bounds;
    public string SuggestParent => suggest.Parent?.Name ?? suggest.Parent?.GetType().Name ?? "null";
    public void UpdateSuggestionButton()
    {
        cleanBtn.Enabled = rewordBtn.Enabled = !suggestionBusy && !string.IsNullOrWhiteSpace(Result.Text);
        suggest.Visible = Suggestions.Checked && !string.IsNullOrWhiteSpace(Result.Text);
        suggest.Enabled = !suggestionBusy;
        if (suggest.Visible)
        {
            suggest.Location = new Point(editorPanel.Width - 44, editorPanel.Height - 44);
            suggest.BringToFront();
        }
    }

    public void ShowWindow()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Native.WM_HOTKEY) HotkeyPressed?.Invoke(m.WParam.ToInt32());
        base.WndProc(ref m);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            tips.Dispose();
            copyFeedbackTimer.Dispose();
            settingsMenu.Dispose();
        }
        base.Dispose(disposing);
    }
}
