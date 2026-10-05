using System.Net.Http;

namespace LazyType;

internal static class ModelProcess
{
    public static Process Start(string engine, string filename, IEnumerable<string> args, ProcessJob job, Func<bool> loading)
    {
        var exe = EngineHost.Executable(engine, filename);
        var info = new ProcessStartInfo(exe) { WorkingDirectory = Path.GetDirectoryName(exe)!, CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        var process = new Process { StartInfo = info };
        var count = 0;
        void Output(object sender, DataReceivedEventArgs e)
        {
            if (e.Data != null && loading() && (e.Data.Contains("CUDA") || e.Data.Contains("buffer size") || e.Data.Contains("error", StringComparison.OrdinalIgnoreCase)) && Interlocked.Increment(ref count) <= 40)
                AppLog.Write(engine + ": " + e.Data);
        }
        process.OutputDataReceived += Output; process.ErrorDataReceived += Output;
        try
        {
            process.Start(); job.Add(process); process.BeginOutputReadLine(); process.BeginErrorReadLine();
            try { process.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
            return process;
        }
        catch { Kill(process); throw; }
    }
    public static async Task WaitReadyAsync(HttpClient http, Process process, string url, CancellationToken ct)
    {
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < TimeSpan.FromMinutes(3))
        {
            ct.ThrowIfCancellationRequested();
            if (process.HasExited) throw new InvalidOperationException("A model could not start (exit " + process.ExitCode + "). See the local app.log for device diagnostics.");
            try
            {
                using var ping = CancellationTokenSource.CreateLinkedTokenSource(ct); ping.CancelAfter(1500);
                using var response = await http.GetAsync(url, ping.Token).ConfigureAwait(false);
                if (response.IsSuccessStatusCode) return;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
            catch (HttpRequestException) { }
            await Task.Delay(100, ct).ConfigureAwait(false);
        }
        throw new TimeoutException("Loading the local models took too long. Pause and resume to retry.");
    }
    public static void Kill(Process? process)
    {
        if (process == null) return;
        try { if (!process.HasExited) { process.Kill(true); process.WaitForExit(5000); } } catch { }
        process.Dispose();
    }
}

internal sealed class LlamaWorker : ITextWorker
{
    private readonly object gate = new();
    private readonly ProcessJob job = new();
    private readonly HttpClient http;
    private Process? process;
    private bool disposed;
    private volatile bool ready;
    public string Model { get; }
    public string Key { get; } = Guid.NewGuid().ToString("N");
    public string Url { get; }
    public bool IsAlive { get { lock (gate) return ready && process?.HasExited == false; } }
    public long WorkingSetBytes { get { lock (gate) { if (process?.HasExited != false) return 0; process.Refresh(); return process.WorkingSet64; } } }
    public LlamaWorker(string model, HttpClient http)
    { Model = model; this.http = http; Url = "http://127.0.0.1:" + EngineHost.FreePort(); }
    public async Task StartAsync(CancellationToken ct)
    {
        Process started;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this); ct.ThrowIfCancellationRequested();
            var path = TextModels.PathFor(Model);
            if (!File.Exists(path)) throw new FileNotFoundException(Model + " is not installed. Choose another text model or install it with scripts/setup_models.py.");
            process = started = ModelProcess.Start("llama", "llama-server.exe", new[] { "-m", path, "--host", "127.0.0.1", "--port", new Uri(Url).Port.ToString(), "-ngl", "99", "-c", "4096", "-np", "1", "-t", "4", "-b", "256", "-ub", "128", "--load-mode", "none", "--api-key", Key, "--no-webui" }, job, () => !ready);
        }
        await ModelProcess.WaitReadyAsync(http, started, Url + "/health", ct).ConfigureAwait(false);
        lock (gate) { ct.ThrowIfCancellationRequested(); ObjectDisposedException.ThrowIf(disposed, this); ready = true; }
    }
    public void Dispose()
    {
        lock (gate) { if (disposed) return; disposed = true; ready = false; ModelProcess.Kill(process); process = null; job.Dispose(); }
    }
}
