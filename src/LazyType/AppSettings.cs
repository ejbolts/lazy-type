using System.Text.Json;
using Microsoft.Win32;

namespace LazyType;

public sealed class AppSettings
{
    public int Microphone { get; set; } = -1;
    public bool Cleanup { get; set; } = true;
    public string Hotkey { get; set; } = "Ctrl+Alt+Space";
    public string Theme { get; set; } = "System";
    public string PopupTheme { get; set; } = "Follow app";
    public static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Applications", "LazyType");
    public static string SettingsFile => Path.Combine(Root, "settings.json");
    public static AppSettings Load()
    {
        try { return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsFile)) ?? new(); }
        catch { return new(); }
    }
    public void Save()
    {
        Directory.CreateDirectory(Root);
        var temp = SettingsFile + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, SettingsFile, true);
    }
    public static bool Startup
    {
        get { using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"); return key?.GetValue("LazyType") != null; }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            if (value) key.SetValue("LazyType", $"\"{Environment.ProcessPath}\" --background");
            else key.DeleteValue("LazyType", false);
        }
    }
}

internal static class AppLog
{
    private static readonly object Gate = new();
    public static void Write(string text)
    {
        // Operational events only. Never log recordings, transcripts or prompts.
        lock (Gate) try
        {
            var path = Path.Combine(AppSettings.Root, "app.log");
            Directory.CreateDirectory(AppSettings.Root);
            if (File.Exists(path) && new FileInfo(path).Length > 1_000_000) File.Move(path, path + ".old", true);
            File.AppendAllText(path, $"{DateTimeOffset.Now:O} {text}{Environment.NewLine}");
        }
        catch { }
    }
}
