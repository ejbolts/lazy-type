using Microsoft.Win32;

namespace LazyType;

internal sealed class ThemeController : IDisposable
{
    private readonly MainForm form;
    private readonly RecordingOverlay overlay;
    private readonly RecordingOverlay preview = new();
    private readonly System.Windows.Forms.Timer previewTimer = new() { Interval = 5000 };
    private readonly AppSettings settings;
    private readonly Action save;
    private bool disposed;

    public ThemeController(MainForm form, RecordingOverlay overlay, AppSettings settings, Action save)
    {
        this.form = form; this.overlay = overlay; this.settings = settings; this.save = save;
        preview.Owner = form;
        form.ThemeChoice.SelectedItem = settings.Theme;
        if (form.ThemeChoice.SelectedIndex < 0) form.ThemeChoice.SelectedIndex = 0;
        form.PopupThemeChoice.SelectedItem = settings.PopupTheme;
        if (form.PopupThemeChoice.SelectedIndex < 0) form.PopupThemeChoice.SelectedIndex = 0;
        settings.Theme = form.ThemeChoice.SelectedItem!.ToString()!;
        settings.PopupTheme = form.PopupThemeChoice.SelectedItem!.ToString()!;
        Apply();
        form.ThemeChoice.SelectedIndexChanged += SelectionChanged;
        form.PopupThemeChoice.SelectedIndexChanged += SelectionChanged;
        form.PreviewPopupRequested += Preview;
        previewTimer.Tick += (_, _) => { previewTimer.Stop(); preview.Dismiss(); };
        SystemEvents.UserPreferenceChanged += SystemPreferenceChanged;
    }

    internal static bool Resolve(string? choice, bool fallback) => choice switch
    {
        "Dark" => true,
        "Light" => false,
        _ => fallback
    };

    internal static bool IsSystemDark => SystemDark;

    private static bool SystemDark
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
            }
            catch { return false; }
        }
    }

    private void SelectionChanged(object? sender, EventArgs e)
    {
        settings.Theme = form.ThemeChoice.SelectedItem!.ToString()!;
        settings.PopupTheme = form.PopupThemeChoice.SelectedItem!.ToString()!;
        Apply(); save();
    }

    private void Apply()
    {
        var appDark = Resolve(settings.Theme, SystemDark);
        var popupDark = Resolve(settings.PopupTheme, appDark);
        form.ApplyTheme(appDark);
        overlay.ApplyTheme(popupDark);
        preview.ApplyTheme(popupDark);
    }

    private void Preview()
    {
        var popupDark = Resolve(settings.PopupTheme, Resolve(settings.Theme, SystemDark));
        preview.ApplyTheme(popupDark);
        preview.PresentPreview();
        previewTimer.Stop(); previewTimer.Start();
    }

    private void SystemPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category == UserPreferenceCategory.General || e.Category == UserPreferenceCategory.Color)
            UI(Apply);
    }

    private void UI(Action action)
    {
        if (!disposed && !form.IsDisposed)
            try { if (form.InvokeRequired) form.BeginInvoke(action); else action(); } catch { }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        SystemEvents.UserPreferenceChanged -= SystemPreferenceChanged;
        previewTimer.Dispose();
        preview.Dispose();
    }
}
