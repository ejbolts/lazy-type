using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace LazyType;

// Explicit diagnostic only; ordinary startup never loads models or queries the GPU.
internal static class MemoryTest
{
    public static async Task<int> RunAsync(string[] args)
    {
        var index = Array.IndexOf(args, "--memory-test");
        var destination = index + 1 < args.Length ? args[index + 1] : Path.Combine(AppSettings.Root, "memory-test.json");
        var report = new Dictionary<string, object?> { ["measuredAt"] = DateTimeOffset.Now };
        try
        {
            // Keep the GPU baseline free of another Lazy Type model session.
            foreach (var name in new[] { "whisper-server", "llama-server" })
                foreach (var process in Process.GetProcessesByName(name))
                    using (process) throw new InvalidOperationException("Wait for existing model workers to unload before measuring.");

            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            using var engines = new EngineHost();
            var baseline = await GpuAsync(timeout.Token);
            report["gpuBefore"] = baseline;
            await engines.EnsureReadyAsync(timeout.Token);
            report["loadedModelWorkingSetBytes"] = engines.ModelWorkingSetBytes();

            // Exercise speech/VAD and cleanup so lazy allocations are included.
            using var buffer = new MemoryStream();
            using (var writer = new NAudio.Wave.WaveFileWriter(new NAudio.Utils.IgnoreDisposeStream(buffer), new NAudio.Wave.WaveFormat(16000, 16, 1)))
                writer.Write(new byte[32000], 0, 32000);
            await engines.TranscribeAsync(buffer.ToArray(), timeout.Token);
            await engines.CleanupAsync("Um, this is a short local memory test.", timeout.Token);
            var samples = new List<object>();
            for (var sample = 0; sample < 5; sample++)
            {
                samples.Add(new { modelWorkingSetBytes = engines.ModelWorkingSetBytes(), gpu = await GpuAsync(timeout.Token) });
                await Task.Delay(500, timeout.Token);
            }
            report["warmSamples"] = samples;
            engines.Stop();
            await Task.Delay(500, timeout.Token);
            report["gpuAfterUnload"] = await GpuAsync(timeout.Token);
            report["notes"] = "RAM is the sum of both model workers' working sets, excluding the UI. GPU readings are whole-device MiB; subtract the baseline for an estimate. Other GPU activity can affect the difference. Samples are warm idle readings after short inference, not peak usage.";
            report["passed"] = true;
        }
        catch (Exception e) { report["passed"] = false; report["error"] = e.Message; }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);
        await File.WriteAllTextAsync(destination, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        return report["passed"] is true ? 0 : 1;
    }

    private static async Task<object[]> GpuAsync(CancellationToken ct)
    {
        var info = new ProcessStartInfo("nvidia-smi") { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        info.ArgumentList.Add("--query-gpu=index,name,memory.used");
        info.ArgumentList.Add("--format=csv,noheader,nounits");
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Could not start nvidia-smi.");
        try
        {
            var output = process.StandardOutput.ReadToEndAsync(ct);
            var error = process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);
            if (process.ExitCode != 0) throw new InvalidOperationException("GPU memory measurement failed: " + await error);
            return (await output).Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line =>
            {
                var fields = line.Split(',');
                return (object)new { index = fields[0].Trim(), name = fields[1].Trim(), usedMiB = double.Parse(fields[2].Trim(), CultureInfo.InvariantCulture) };
            }).ToArray();
        }
        finally { if (!process.HasExited) process.Kill(true); }
    }
}
