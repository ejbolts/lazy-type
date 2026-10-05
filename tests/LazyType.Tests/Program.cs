using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using LazyType;

internal static class Program
{
    private static int passed, failed;
    private static void Check(bool value, string message = "Assertion failed") { if (!value) throw new Exception(message); }
    private static async Task Until(Func<bool> predicate) { using var timeout = new CancellationTokenSource(10000); while (!predicate()) await Task.Delay(5, timeout.Token); }
    private static async Task Throws(Func<Task> action) { try { await action(); } catch { return; } throw new Exception("Expected failure"); }
    private static async Task Test(string name, Func<Task> run)
    {
        try { await run().WaitAsync(TimeSpan.FromSeconds(90)); passed++; Console.WriteLine("PASS " + name); }
        catch (Exception e) { failed++; Console.WriteLine("FAIL " + name + ": " + e); }
    }
    [STAThread]
    private static async Task<int> Main(string[] args)
    {
        // Exercise the native controls on their creating STA before async continuations.
        using (var selector = new ModelSelector())
        {
            foreach (var mode in TextModels.Choices)
            {
                selector.SelectedModel = mode;
                var boxes = selector.Controls.Cast<Control>().SelectMany(Descendants).OfType<CheckBox>().ToArray();
                Check(boxes.Count(b => b.Checked) == 1 && boxes.Single(b => b.Checked).AccessibleName == mode);
                boxes.Single(b => b.Checked).Checked = false;
                Check(boxes.Count(b => b.Checked) == 1, "Cannot uncheck the sole selected model");
            }
            passed++; Console.WriteLine("PASS Mutually exclusive native checkbox controls");
        }
        SynchronizationContext.SetSynchronizationContext(null);
        if (args.Contains("--integration")) await Integration(args);
        else await Unit();
        Console.WriteLine($"RESULT {passed} passed, {failed} failed");
        return failed == 0 ? 0 : 1;
    }
    private static IEnumerable<Control> Descendants(Control c) => new[] { c }.Concat(c.Controls.Cast<Control>().SelectMany(Descendants));
    private static async Task Unit()
    {
        await Test("Existing settings default to current; selection round-trip; unknown fallback", () =>
        {
            Check(JsonSerializer.Deserialize<AppSettings>("{}")!.TextModel == TextModels.Current);
            foreach (var model in TextModels.Choices) Check(JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(new AppSettings { TextModel = model }))!.TextModel == model);
            Check(TextModels.Normalize(null) == TextModels.Current && TextModels.Normalize("unknown") == TextModels.Current);
            return Task.CompletedTask;
        });
        foreach (var model in TextModels.Choices.Where(m => m != TextModels.Dynamic))
            await Test("Manual loads only " + model, async () => { using var r = new Rig(); r.Host.SetMode(model); await r.Host.EnsureReadyAsync(default); Check(r.Workers.Length == 1 && r.Host.ActiveModel == model && r.DelayCalls == 0); });
        await Test("Dynamic delay is exactly five seconds after primary readiness", async () =>
        {
            using var r = new Rig { HoldPrimary = true }; r.Host.SetMode(TextModels.Dynamic);
            var initial = r.Host.EnsureReadyAsync(default); await Until(() => r.Workers.Length == 1);
            Check(r.DelayCalls == 0 && !r.Host.Ready); r.Workers[0].Complete(); await initial;
            await Until(() => r.DelayCalls == 1); Check(r.DelayDuration == TimeSpan.FromSeconds(5));
            Check(r.Workers.Length == 1 && r.Host.ActiveModel == TextModels.Current); r.Host.Stop(); await r.Host.UpgradeTask;
        });
        await Test("Candidate cannot replace Qwen before health readiness", async () =>
        {
            using var r = await Rig.Dynamic(); r.FireDelay(); await Until(() => r.Workers.Length == 2);
            using var lease = r.Host.Acquire(); Check(lease.Worker.Model == TextModels.Current && !r.Workers[0].Disposed && r.Host.WorkerCount == 2);
            r.Workers[1].Complete(); await r.Host.UpgradeTask; Check(r.Host.ActiveModel == TextModels.Gemma);
        });
        await Test("Handover drains all old requests; new requests use Gemma", async () =>
        {
            using var r = await Rig.Dynamic(); var first = r.Host.Acquire(); var second = r.Host.Acquire();
            r.FireDelay(); await Until(() => r.Workers.Length == 2); r.Workers[1].Complete(); await r.Host.UpgradeTask;
            using var latest = r.Host.Acquire(); Check(latest.Worker.Model == TextModels.Gemma && first.Worker.Model == TextModels.Current);
            Check(!r.Workers[0].Disposed && r.Host.WorkerCount == 2); first.Dispose(); first.Dispose(); Check(!r.Workers[0].Disposed);
            second.Dispose(); Check(r.Workers[0].Disposed && r.Host.WorkerCount == 1);
        });
        await Test("Idle old worker unloads immediately after Gemma is ready", async () =>
        { using var r = await Rig.Dynamic(); r.FireDelay(); await Until(() => r.Workers.Length == 2); r.Workers[1].Complete(); await r.Host.UpgradeTask; Check(r.Workers[0].Disposed && !r.Workers[1].Disposed && r.Host.WorkerCount == 1); });
        await Test("Concurrent preload calls create one primary and one upgrade", async () =>
        {
            using var r = new Rig { HoldPrimary = true }; r.Host.SetMode(TextModels.Dynamic);
            var tasks = Enumerable.Range(0, 12).Select(_ => r.Host.EnsureReadyAsync(default)).ToArray();
            await Until(() => r.Workers.Length == 1); r.Workers[0].Complete(); await Task.WhenAll(tasks);
            await Until(() => r.DelayCalls == 1); Check(r.Workers.Length == 1); r.FireDelay(); await Until(() => r.Workers.Length == 2);
            r.Workers[1].Complete(); await r.Host.UpgradeTask; Check(r.Workers.Length == 2 && r.DelayCalls == 1);
        });
        await Test("Stop before five seconds cancels upgrade and requests", async () =>
        { using var r = await Rig.Dynamic(); using var lease = r.Host.Acquire(); r.Host.Stop(); r.FireDelay(); await r.Host.UpgradeTask; Check(lease.SessionToken.IsCancellationRequested && r.Workers.Length == 1 && r.Workers.All(w => w.Disposed) && r.Host.WorkerCount == 0); });
        await Test("Stop during Gemma loading kills both without resurrection", async () =>
        { using var r = await Rig.Dynamic(); r.FireDelay(); await Until(() => r.Workers.Length == 2); r.Host.Stop(); r.Workers[1].Complete(); await r.Host.UpgradeTask; Check(!r.Host.Ready && r.Host.WorkerCount == 0 && r.Workers.All(w => w.Disposed)); });
        foreach (var error in new Exception[] { new FileNotFoundException(), new TimeoutException(), new InvalidOperationException("GPU allocation failed") })
            await Test("Upgrade failure retains current: " + error.GetType().Name, async () =>
            { using var r = await Rig.Dynamic(); r.FireDelay(); await Until(() => r.Workers.Length == 2); r.Workers[1].Fail(error); await r.Host.UpgradeTask; using var lease = r.Host.Acquire(); Check(lease.Worker.Model == TextModels.Current && r.Host.Ready && r.Workers[1].Disposed && r.Host.WorkerCount == 1 && r.Host.Status.Contains("unavailable")); });
        await Test("Candidate factory failure retains current", async () =>
        { using var r = await Rig.Dynamic(); r.FactoryFailure = true; r.FireDelay(); await r.Host.UpgradeTask; Check(r.Host.Ready && r.Host.ActiveModel == TextModels.Current && r.Host.WorkerCount == 1); });
        await Test("Manual initial failure cleans worker and can retry", async () =>
        {
            using var r = new Rig { HoldPrimary = true }; var load = r.Host.EnsureReadyAsync(default); await Until(() => r.Workers.Length == 1); r.Workers[0].Fail(new Exception("load failure")); await Throws(() => load);
            Check(r.Host.WorkerCount == 0 && r.Workers[0].Disposed); r.HoldPrimary = false; await r.Host.EnsureReadyAsync(default); Check(r.Host.Ready);
        });
        await Test("Cancelling initial load disposes partial worker", async () =>
        { using var r = new Rig { HoldPrimary = true }; using var ct = new CancellationTokenSource(); var load = r.Host.EnsureReadyAsync(ct.Token); await Until(() => r.Workers.Length == 1); ct.Cancel(); await Throws(() => load); Check(r.Workers[0].Disposed && r.Host.WorkerCount == 0); });
        await Test("Mode change cancels candidate; stale failure cannot stop new session", async () =>
        {
            using var r = await Rig.Dynamic(); r.FireDelay(); await Until(() => r.Workers.Length == 2); var oldUpgrade = r.Host.UpgradeTask;
            r.Host.SetMode(TextModels.Qwen35); await r.Host.EnsureReadyAsync(default); r.Workers[1].Fail(new Exception("late failure")); await oldUpgrade;
            Check(r.Host.Ready && r.Host.ActiveModel == TextModels.Qwen35 && r.Host.WorkerCount == 1 && r.Workers.Take(2).All(w => w.Disposed));
        });
        await Test("Late initial completion cannot interfere with restarted model", async () =>
        {
            using var r = new Rig { HoldPrimary = true }; var old = r.Host.EnsureReadyAsync(default); await Until(() => r.Workers.Length == 1);
            r.Host.Stop(); r.HoldPrimary = false; var newer = r.Host.EnsureReadyAsync(default); r.Workers[0].Complete(); await Throws(() => old); await newer; Check(r.Host.Ready && r.Host.WorkerCount == 1);
        });
        await Test("Crashed primary cancels stale upgrade and reloads safely", async () =>
        {
            using var r = await Rig.Dynamic(); r.FireDelay(); await Until(() => r.Workers.Length == 2); var old = r.Host.UpgradeTask; r.Workers[0].Crash();
            await r.Host.EnsureReadyAsync(default); await old; Check(r.Workers[0].Disposed && r.Workers[1].Disposed && r.Host.ActiveModel == TextModels.Current && r.Host.WorkerCount == 1);
        });
        await Test("Repeated stop/dispose and restart leak no fake workers", async () =>
        { using var r = new Rig(); for (var i = 0; i < 20; i++) { await r.Host.EnsureReadyAsync(default); r.Host.Stop(); r.Host.Stop(); Check(r.Host.WorkerCount == 0); } r.Host.Dispose(); r.Host.Dispose(); await Throws(() => r.Host.EnsureReadyAsync(default)); Check(r.Workers.All(w => w.Disposed)); });
        await Test("Dash cleanup preserves paragraphs, flags, hyphens and ranges", () =>
        {
            foreach (var text in new[] { "First.\n\nNext.", "Run --dry-run and --output=file.", "Use state-of-the-art tools.", "The range is 3–5 and 3-5.", "The value is -5." }) Check(EngineHost.CleanPauseDashes(text) == text, text);
            foreach (var pair in new[] { ("Hi — there.", "Hi there."), ("First. — Next.", "First. Next."), ("First.\n\nNext — line.", "First.\n\nNext line."), ("Yes -- please.", "Yes please.") }) Check(EngineHost.CleanPauseDashes(pair.Item1) == pair.Item2, pair.Item1);
            return Task.CompletedTask;
        });
        await Test("Existing numeric and incomplete-output guards remain effective", () =>
        { Check(EngineHost.PreservesNumbers("At 3:30 on 2026-10-05, pay 42.5.", "Pay 42.5 at 3:30 on 2026-10-05.")); Check(!EngineHost.PreservesNumbers("Pay 42.5.", "Pay 45.")); Check(!EngineHost.PlausibleCleanup("Hello", "") && !EngineHost.PlausibleCleanup("Hello", "<think>Hello")); return Task.CompletedTask; });
    }
    private static async Task Integration(string[] args)
    {
        var audioIndex = Array.IndexOf(args, "--audio");
        foreach (var model in new[] { TextModels.Current, TextModels.Qwen35, TextModels.Gemma }.Where(m => !args.Contains("--dynamic-only") && (!args.Contains("--gemma-only") || m == TextModels.Gemma)))
            await Test("REAL speech + cleanup + polish + unload: " + model, async () =>
            {
                using var host = new EngineHost(); host.SetTextModel(model); var watch = Stopwatch.StartNew(); await host.EnsureReadyAsync(default);
                Check(host.Ready && host.ActiveTextModel == model && host.TextWorkerCount == 1);
                Console.WriteLine($"  LOAD {watch.Elapsed.TotalSeconds:F2}s; process RAM {host.ModelWorkingSetBytes()/1073741824.0:F2} GiB");
                if (audioIndex >= 0) { var transcript = await host.TranscribeAsync(File.ReadAllBytes(args[audioIndex + 1]), default); Check(transcript.Contains("country", StringComparison.OrdinalIgnoreCase), "Public audio transcription"); }
                foreach (var source in new[] { "Um, please send the report at 3:30 on 2026-10-05.", "Run --dry-run.\n\nKeep the output in report.txt." })
                {
                    var cleaned = await host.CleanupAsync(source, default); Check(EngineHost.PreservesNumbers(source, cleaned));
                    var polished = await host.SuggestAsync(source, default); Check(EngineHost.PreservesNumbers(source, polished));
                    if (source.Contains("--dry-run")) Check(cleaned.Contains("--dry-run") && polished.Contains("--dry-run") && cleaned.Contains('\n') && polished.Contains('\n'), "Flags/paragraphs preserved end to end");
                }
                host.Stop(); Check(!host.Ready && host.TextWorkerCount == 0);
            });
        if (args.Contains("--gemma-only")) return;
        await Test("REAL dynamic overlap, in-flight edit, Gemma promotion and cleanup", async () =>
        {
            using var host = new EngineHost(); host.SetTextModel(TextModels.Dynamic); var watch = Stopwatch.StartNew();
            host.TextStateChanged += () => Console.WriteLine($"  STATE {watch.Elapsed.TotalSeconds:F2}s {host.TextStatus}; workers={host.TextWorkerCount}");
            await host.EnsureReadyAsync(default); Check(host.ActiveTextModel == TextModels.Current);
            var longText = string.Join(" ", Enumerable.Range(1, 55).Select(i => $"For item {i}, please review the document and confirm the existing details before tomorrow."));
            await Until(() => host.TextWorkerCount == 2); Check(host.ActiveTextModel == TextModels.Current);
            var edit = host.CleanupAsync(longText, default);
            Console.WriteLine($"  OVERLAP RAM {host.ModelWorkingSetBytes()/1073741824.0:F2} GiB");
            await host.UpgradeTask; Check(host.ActiveTextModel == TextModels.Gemma);
            Check(!edit.IsCompleted, "An actual Qwen edit must still be running at promotion");
            Check(host.TextWorkerCount == 2, "Qwen remains resident for its active request");
            var shortEdit = await host.CleanupAsync("Um, send the report at 3:30.", default); Check(shortEdit.Contains("3:30"));
            var originalEdit = await edit; Check(EngineHost.PreservesNumbers(longText, originalEdit));
            await Until(() => host.TextWorkerCount == 1); host.Stop(); Check(host.TextWorkerCount == 0);
        });
        await Test("REAL stop during primary loading and immediate restart", async () =>
        {
            using var host = new EngineHost(); host.SetTextModel(TextModels.Dynamic); var loading = host.EnsureReadyAsync(default); await Task.Delay(250); host.Stop();
            await Throws(() => loading); host.SetTextModel(TextModels.Current); await host.EnsureTextReadyAsync(default); Check(host.ActiveTextModel == TextModels.Current); host.Stop();
        });
        await Test("REAL stop during Gemma loading leaves no owned workers", async () =>
        { using var host = new EngineHost(); host.SetTextModel(TextModels.Dynamic); await host.EnsureReadyAsync(default); await Until(() => host.TextWorkerCount == 2); host.Stop(); await host.UpgradeTask; Check(host.TextWorkerCount == 0 && !host.Ready); });
    }
    private sealed class Rig : IDisposable
    {
        public readonly TextModelHost Host;
        private readonly ConcurrentQueue<FakeWorker> workers = new();
        public FakeWorker[] Workers => workers.ToArray();
        private readonly TaskCompletionSource delayed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool HoldPrimary, FactoryFailure;
        public int DelayCalls;
        public TimeSpan DelayDuration;
        public Rig()
        {
            Host = new(model =>
            {
                if (FactoryFailure) throw new FileNotFoundException();
                var worker = new FakeWorker(model, HoldPrimary || (model == TextModels.Gemma && workers.Count > 0)); workers.Enqueue(worker); return worker;
            }, async (duration, ct) => { DelayDuration = duration; Interlocked.Increment(ref DelayCalls); await delayed.Task.WaitAsync(ct); });
        }
        public static async Task<Rig> Dynamic() { var r = new Rig(); r.Host.SetMode(TextModels.Dynamic); await r.Host.EnsureReadyAsync(default); await Until(() => r.DelayCalls == 1); return r; }
        public void FireDelay() => delayed.TrySetResult();
        public void Dispose() => Host.Dispose();
    }
    private sealed class FakeWorker : ITextWorker
    {
        private readonly TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly bool held;
        public bool Disposed, Alive;
        public string Model { get; }
        public string Url => "http://127.0.0.1:1";
        public string Key => "fake";
        public bool IsAlive => Alive && !Disposed;
        public long WorkingSetBytes => IsAlive ? 1024 : 0;
        public FakeWorker(string model, bool held) { Model = model; this.held = held; }
        public async Task StartAsync(CancellationToken ct) { if (held) await started.Task.WaitAsync(ct); ct.ThrowIfCancellationRequested(); Alive = true; }
        public void Complete() => started.TrySetResult();
        public void Fail(Exception e) => started.TrySetException(e);
        public void Crash() => Alive = false;
        public void Dispose() { Disposed = true; Alive = false; }
    }
}
