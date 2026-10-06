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
    public void Reset() { Models.Clear(); Since = DateTimeOffset.Now; }
    // Spoken words, so "3:30" and "state-of-the-art" are one word each.
    internal static int CountWords(string text) => Regex.Matches(text, @"\S+").Count(m => m.Value.Any(char.IsLetterOrDigit));

    public static ModelUsage Load()
    {
        try { return JsonSerializer.Deserialize<ModelUsage>(File.ReadAllText(UsageFile)) ?? new(); }
        catch { return new(); }
    }
    public void Save()
    {
        try
        {
            Directory.CreateDirectory(AppSettings.Root);
            var temp = UsageFile + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, UsageFile, true);
        }
        catch (Exception e) { AppLog.Write("Model usage could not be saved (" + e.GetType().Name + ")."); }
    }
}
