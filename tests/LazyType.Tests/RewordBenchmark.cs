using System.Diagnostics;
using System.Text.Json;
using LazyType;

internal static class RewordBenchmark
{
    private sealed record Sample(string Id, string Input, string[] Ideas);
    public static int Validate(string path)
    {
        using var report = JsonDocument.Parse(File.ReadAllText(path));
        var cases = report.RootElement.GetProperty("cases").EnumerateArray().ToDictionary(c => c.GetProperty("Id").GetString()!, c => c.GetProperty("Input").GetString()!);
        var count = 0;
        foreach (var result in report.RootElement.GetProperty("results").EnumerateArray().Where(r => r.GetProperty("mode").GetString() == TextEditModes.Reword))
        {
            var raw = cases[result.GetProperty("sample").GetString()!];
            var text = result.GetProperty("text").GetString()!;
            if (!EngineHost.PlausibleReword(raw, text) || !EngineHost.PreservesRewordNumbers(raw, text) || !EngineHost.PreservesRewordLiterals(raw, text))
                throw new Exception("Final validation failed: " + result.GetProperty("model").GetString() + " / " + result.GetProperty("sample").GetString());
            count++;
        }
        Console.WriteLine($"PASS {count} recorded reword outputs satisfy final production validation.");
        return 0;
    }
    public static async Task<int> Run(string[] args)
    {
        var index = Array.IndexOf(args, "--benchmark");
        if (index + 1 >= args.Length) throw new ArgumentException("--benchmark requires an output JSON path.");
        var output = Path.GetFullPath(args[index + 1]);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var fixtures = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "reword-cases.json"));
        var cases = JsonSerializer.Deserialize<Sample[]>(fixtures, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        var results = new List<object>();
        var loads = new List<object>();
        var errors = 0;
        void Save() => File.WriteAllText(output, JsonSerializer.Serialize(new
        {
            createdAt = DateTimeOffset.Now, runtime = Environment.Version.ToString(),
            fixtureSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(fixtures))),
            cleanupPrompt = EngineHost.CleanupPrompt, rewordPrompt = EngineHost.RewordPrompt,
            settings = "Production EngineHost; Q4_K_M; 4096 context; temperature 0; thinking off; one worker at a time. Load and warm-up excluded from edit timings.",
            loads, cases, results
        }, new JsonSerializerOptions { WriteIndented = true }));
        foreach (var model in TextModels.Choices)
        {
            using var host = new EngineHost();
            host.SetTextModel(model);
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(15));
            var load = Stopwatch.StartNew();
            try
            {
                await host.EnsureTextReadyAsync(timeout.Token);
                loads.Add(new { model, seconds = load.Elapsed.TotalSeconds, bytes = new FileInfo(TextModels.PathFor(model)).Length });
                await host.RewordAsync("Please send the report tomorrow.", timeout.Token);
                foreach (var sample in cases)
                    foreach (var mode in new[] { TextEditModes.Cleanup, TextEditModes.Reword })
                    {
                        var timer = Stopwatch.StartNew();
                        try
                        {
                            var text = await host.RewriteAsync(sample.Input, mode, timeout.Token);
                            results.Add(new { model, mode, sample = sample.Id, text, seconds = timer.Elapsed.TotalSeconds,
                                lengthRatio = (double)text.Length / sample.Input.Length, numericDetails = EngineHost.PreservesRewordNumbers(sample.Input, text) });
                            Console.WriteLine($"OK {model} / {mode} / {sample.Id}: {timer.Elapsed.TotalSeconds:F2}s");
                        }
                        catch (Exception e) when (!timeout.IsCancellationRequested)
                        {
                            errors++;
                            results.Add(new { model, mode, sample = sample.Id, error = e.Message,
                                rejectedText = e.Data["cleanupResult"] as string, seconds = timer.Elapsed.TotalSeconds });
                            Console.WriteLine($"REJECT {model} / {mode} / {sample.Id}: {e.Message}");
                        }
                        Save();
                    }
            }
            finally { host.Stop(); Save(); }
        }
        Console.WriteLine($"Benchmark saved to {output}; {errors} rejected edits. Review meaning against each case's ideas; numeric checks alone do not score fidelity.");
        return 0;
    }
}
