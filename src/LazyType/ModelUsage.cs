using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace LazyType;

// Personal usage counts: one cleaned dictation per model that actually edited it. Counts and word
// totals only; dictated text is never stored.
public sealed class ModelUsage
{
    public sealed class Entry
    {
        public int Manual { get; set; }
        public int Dynamic { get; set; }
        public long Words { get; set; }
        [JsonIgnore] public int Dictations => Manual + Dynamic;
    }

    public DateTimeOffset Since { get; set; } = DateTimeOffset.Now;
    public Dictionary<string, Entry> Models { get; set; } = new();
    public static string UsageFile => Path.Combine(AppSettings.Root, "usage.json");
    [JsonIgnore] public int Dictations => Models.Values.Sum(e => e.Dictations);
    [JsonIgnore] public long Words => Models.Values.Sum(e => e.Words);
    public Entry For(string model) => Models.TryGetValue(model, out var entry) ? entry : new();

    public void Record(string model, bool dynamic, string spoken)
    {
        if (!Models.TryGetValue(model, out var entry)) Models[model] = entry = new();
        if (dynamic) entry.Dynamic++; else entry.Manual++;
        entry.Words += CountWords(spoken);
    }
    // Spoken words, so "3:30" and "state-of-the-art" are one word each.
    internal static int CountWords(string text) => Regex.Matches(text, @"\S+").Count(m => m.Value.Any(char.IsLetterOrDigit));

    // Loaded at startup and saved after every count, so totals carry over between restarts.
    public static ModelUsage Load(string? path = null)
    {
        path ??= UsageFile;
        if (!File.Exists(path)) return new();
        try { return JsonSerializer.Deserialize<ModelUsage>(File.ReadAllText(path)) ?? new(); }
        catch (Exception e)
        {
            // Keep an unreadable file instead of overwriting it with fresh counts.
            try { File.Move(path, path + ".unreadable", true); } catch { }
            AppLog.Write("Model usage could not be read (" + e.GetType().Name + "); counting again from now.");
            return new();
        }
    }
    public void Save(string? path = null)
    {
        path ??= UsageFile;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, path, true);
        }
        catch (Exception e) { AppLog.Write("Model usage could not be saved (" + e.GetType().Name + ")."); }
    }
}
