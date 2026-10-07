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
        using (var form = new MainForm())
        {
            var buttons = Descendants(form).OfType<Button>().Where(b => b.Text is TextEditModes.Cleanup or TextEditModes.Reword).ToArray();
            Check(buttons.Length == 2 && buttons.All(b => !b.Enabled), "Empty result disables both manual actions");
            Check(form.Reword.Enabled && !form.Reword.Checked, "Reword is opt-in and independent of cleanup");
            form.Clean.Checked = true;
            form.Result.Text = "Sample paragraph.";
            Check(form.Reword.Enabled && buttons.All(b => b.Enabled), "Manual edits work without AI suggestions");
            form.SetSuggestionBusy(true);
            form.Result.Text = "Changed while busy.";
            Check(!form.Clean.Enabled && !form.Reword.Enabled && buttons.All(b => !b.Enabled));
            form.SetSuggestionBusy(false);
            Check(form.Clean.Enabled && form.Reword.Enabled && buttons.All(b => b.Enabled));
            form.Clean.Checked = false;
            Check(form.Reword.Enabled && buttons.All(b => b.Enabled), "Cleanup off does not disable Reword or manual edits");
            form.Reword.Checked = true; Check(form.Reword.Checked);
            form.ResultText = "First paragraph.\n\nSecond paragraph.";
            Check(form.Result.Lines.SequenceEqual(new[] { "First paragraph.", "", "Second paragraph." }), "Native editor displays the blank paragraph break");
            Check(form.ResultText == "First paragraph.\n\nSecond paragraph.", "App text stays consistent for preview comparisons");
            passed++; Console.WriteLine("PASS Native reword toggle and empty/busy/manual action states");
        }
        using (var form = new Form { ForeColor = Color.FromArgb(240, 242, 248) })
        {
            var selector = new ModelSelector(); form.Controls.Add(selector); selector.ApplyTheme(true);
            selector.SelectedModel = TextModels.Qwen35;
            var labels = selector.Controls.Cast<Control>().SelectMany(Descendants).OfType<Label>().Where(l => TextModels.Choices.Any(l.Text.StartsWith)).ToArray();
            var boxes = selector.Controls.Cast<Control>().SelectMany(Descendants).OfType<CheckBox>().ToArray();
            selector.Locked = true;
            Check(selector.Enabled && labels.Length == 2 && labels.All(l => l.Enabled && l.ForeColor == Color.FromArgb(150, 165, 185)), "Locked labels stay enabled and readable");
            Check(boxes.All(b => !b.Enabled), "Locked checkboxes are disabled");
            labels.Single(l => l.Text == TextModels.Gemma).GetType().GetMethod("OnClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(labels.Single(l => l.Text == TextModels.Gemma), new object[] { EventArgs.Empty });
            Check(selector.SelectedModel == TextModels.Qwen35, "Locked label click cannot change the model");
            selector.Locked = false;
            Check(labels.All(l => l.ForeColor == form.ForeColor) && boxes.All(b => b.Enabled), "Unlocked labels inherit the theme colour");
            passed++; Console.WriteLine("PASS Locked model choice stays readable");
        }
        using (var usageForm = new UsageForm(false))
        {
            var usage = JsonSerializer.Deserialize<ModelUsage>("{\"Models\":{\"Qwen3 4B\":{\"Dynamic\":20,\"Words\":317},\"Gemma 4 12B\":{\"Dynamic\":9,\"Words\":452}}}")!;
            usage.Record(TextModels.Qwen35, false, "send it now");
            usageForm.ShowStats(usage);
            var texts = usageForm.Controls.Cast<Control>().SelectMany(Descendants).OfType<Label>().Select(l => l.Text).ToArray();
            Check(!texts.Any(t => t.Contains("Qwen3 4B") || t.Contains("previous") || t.Contains("Historical model")), "Retired model is not listed");
            Check(TextModels.Choices.All(texts.Contains), "Both current models are listed");
            Check(texts.Any(t => t.StartsWith("10 edited dictations · 455 words spoken")), "Totals cover listed models only");
            Check(texts.Contains("9 · 90%") && texts.Contains("1 · 10%"), "Shares add up across listed models");
            Check(JsonSerializer.Deserialize<ModelUsage>(JsonSerializer.Serialize(usage))!.For("Qwen3 4B").Dynamic == 20, "Retired counts stay saved");
            passed++; Console.WriteLine("PASS Usage window lists only current models");
        }
        using (var preview = new SuggestionForm("Graph icon", true, true))
        {
            preview.ShowSuggestion("The graph icon", TextDiff.Compare("Graph icon", "The graph icon"), true, inline: true);
            preview.SetApplying();
            preview.ShowApplyFailure("Couldn't replace the text. Copy the suggestion to use it.");
            Check(preview.SuggestedText == "The graph icon", "Failed replacement retains the copyable suggestion");
            var controls = preview.Controls.Cast<Control>().SelectMany(Descendants).ToArray();
            Check(controls.OfType<Label>().Any(l => l.Text == "Couldn't apply suggestion"), "Failure is shown in the popup");
            Check(controls.OfType<RichTextBox>().Single().Text.Contains("Copy the suggestion"), "Popup explains the recovery action");
            Check(controls.OfType<Button>().Any(b => b.Text == "Apply suggestion" && !b.Enabled), "Failed apply clears the busy caption and disables retries");
            Check(controls.OfType<Button>().Any(b => b.Text == "Copy" && b.Enabled), "Copy stays available");
            preview.ShowFailure("Suggestion unavailable. Close and try again.");
            Check(controls.OfType<Label>().Any(l => l.Text == "Suggestion unavailable"), "Generation errors have the correct caption");
            passed++; Console.WriteLine("PASS Replacement failure appears in popup and retains Copy");
        }
        SynchronizationContext.SetSynchronizationContext(null);
        if (args.Contains("--benchmark")) return await RewordBenchmark.Run(args);
        if (args.Contains("--validate-benchmark")) return RewordBenchmark.Validate(args[Array.IndexOf(args, "--validate-benchmark") + 1]);
        if (args.Contains("--integration")) await Integration(args);
        else await Unit();
        Console.WriteLine($"RESULT {passed} passed, {failed} failed");
        return failed == 0 ? 0 : 1;
    }
    private static IEnumerable<Control> Descendants(Control c) => new[] { c }.Concat(c.Controls.Cast<Control>().SelectMany(Descendants));
    private static async Task Unit()
    {
        await Test("Reword settings migrate and round-trip", () =>
        {
            Check(JsonSerializer.Deserialize<AppSettings>("{\"Cleanup\":false}")!.EditMode == TextEditModes.Cleanup);
            Check(JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(new AppSettings { EditMode = TextEditModes.Reword }))!.EditMode == TextEditModes.Reword);
            Check(TextEditModes.Normalize("unknown") == TextEditModes.Cleanup);
            return Task.CompletedTask;
        });
        await Test("Speech toggle routing preserves cleanup and raw preferences", () =>
        {
            Check(TextEditModes.ForSpeech(true, TextEditModes.Cleanup) == TextEditModes.Cleanup);
            Check(TextEditModes.ForSpeech(false, TextEditModes.Cleanup) == null);
            Check(TextEditModes.ForSpeech(true, TextEditModes.Reword) == TextEditModes.Reword);
            Check(TextEditModes.ForSpeech(false, TextEditModes.Reword) == TextEditModes.Reword);
            return Task.CompletedTask;
        });
        await Test("Reword always selects Gemma; subsequent cleanup and polish restore selection", async () =>
        {
            foreach (var selected in TextModels.Choices)
            {
                using var rig = new Rig();
                rig.Host.SetMode(TextModels.ForEdit(selected, TextEditModes.Reword)); await rig.Host.EnsureReadyAsync(default);
                Check(rig.Host.ActiveModel == TextModels.Gemma && rig.Host.WorkerCount == 1);
                foreach (var mode in new string?[] { TextEditModes.Cleanup, null })
                {
                    rig.Host.SetMode(TextModels.ForEdit(selected, mode)); await rig.Host.EnsureReadyAsync(default);
                    Check(rig.Host.ActiveModel == selected && rig.Host.WorkerCount == 1);
                }
            }
        });
        await Test("Reword permits repetition removal and guards numeric details", () =>
        {
            var raw = string.Concat(Enumerable.Repeat("Please send the report at 3:30. ", 20));
            Check(!EngineHost.PlausibleCleanup(raw, "Please send the report at 3:30."));
            Check(EngineHost.PlausibleReword(raw, "Please send the report at 3:30."));
            Check(EngineHost.PreservesRewordNumbers(raw, "Please send the report at 3:30."));
            Check(!EngineHost.PreservesRewordNumbers("Send 3 items by 4:30.", "Send 3 items."));
            Check(!EngineHost.PreservesRewordNumbers("Send 3 items.", "Send 3 items by 4:30."));
            Check(!EngineHost.PreservesRewordNumbers("The value is -5.", "The value is 5."));
            Check(!EngineHost.PlausibleReword(raw, "") && !EngineHost.PlausibleReword(raw, "<think>"));
            Check(EngineHost.PreservesRewordLiterals("Run --dry-run for customer_id.", "For customer_id, run --dry-run."));
            Check(!EngineHost.PreservesRewordLiterals("Run --dry-run for customer_id.", "Run a dry run for customer_id."));
            Check(!EngineHost.PreservesRewordLiterals("Run --dry-run.", "Run --dry-runner."));
            return Task.CompletedTask;
        });
        await Test("Empty and oversized reword input fails before loading workers", async () =>
        {
            using var host = new EngineHost();
            foreach (var raw in new[] { " ", new string('x', 6001) })
            {
                try { await host.RewordAsync(raw, default); throw new Exception("Expected validation error"); }
                catch (InvalidOperationException e) { Check(e.Message.Contains("6,000")); }
                Check(host.TextWorkerCount == 0);
            }
        });
        await Test("Only two models; legacy and unknown settings migrate to Qwen3.5", () =>
        {
            Check(TextModels.Choices.SequenceEqual(new[] { TextModels.Qwen35, TextModels.Gemma }));
            Check(JsonSerializer.Deserialize<AppSettings>("{}")!.TextModel == TextModels.Qwen35);
            foreach (var model in TextModels.Choices) Check(JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(new AppSettings { TextModel = model }))!.TextModel == model);
            foreach (var legacy in new string?[] { null, "unknown", "Qwen3 4B", "Dynamic" })
            {
                Check(TextModels.Normalize(legacy) == TextModels.Qwen35);
                var migrated = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(new { TextModel = legacy }))!;
                Check(migrated.TextModel == TextModels.Qwen35);
                Check(JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(migrated))!.TextModel == TextModels.Qwen35);
                Check(TextModels.PathFor(legacy!) == TextModels.PathFor(TextModels.Qwen35));
            }
            return Task.CompletedTask;
        });
        foreach (var model in TextModels.Choices)
            await Test("Manual loads only " + model, async () => { using var r = new Rig(); r.Host.SetMode(model); await r.Host.EnsureReadyAsync(default); Check(r.Workers.Length == 1 && r.Host.ActiveModel == model); });
        await Test("Concurrent preload calls create one worker", async () =>
        {
            using var r = new Rig { HoldPrimary = true };
            var tasks = Enumerable.Range(0, 12).Select(_ => r.Host.EnsureReadyAsync(default)).ToArray();
            await Until(() => r.Workers.Length == 1); Check(!r.Host.Ready);
            r.Workers[0].Complete(); await Task.WhenAll(tasks); Check(r.Host.Ready && r.Workers.Length == 1);
        });
        await Test("Changing selected model cancels requests and releases old worker", async () =>
        {
            using var r = new Rig(); await r.Host.EnsureReadyAsync(default); using var lease = r.Host.Acquire();
            r.Host.SetMode(TextModels.Gemma); Check(lease.SessionToken.IsCancellationRequested && r.Workers[0].Disposed && r.Host.WorkerCount == 0);
            await r.Host.EnsureReadyAsync(default); Check(r.Host.ActiveModel == TextModels.Gemma && r.Host.WorkerCount == 1);
        });
        await Test("Stop cancels requests and releases selected worker", async () =>
        {
            using var r = new Rig(); await r.Host.EnsureReadyAsync(default); using var lease = r.Host.Acquire();
            r.Host.Stop(); Check(lease.SessionToken.IsCancellationRequested && r.Workers[0].Disposed && r.Host.WorkerCount == 0);
        });
        await Test("Factory failure leaves no worker and can retry", async () =>
        {
            using var r = new Rig { FactoryFailure = true }; await Throws(() => r.Host.EnsureReadyAsync(default));
            Check(!r.Host.Ready && r.Host.WorkerCount == 0); r.FactoryFailure = false;
            await r.Host.EnsureReadyAsync(default); Check(r.Host.Ready && r.Host.WorkerCount == 1);
        });
        await Test("Manual initial failure cleans worker and can retry", async () =>
        {
            using var r = new Rig { HoldPrimary = true }; var load = r.Host.EnsureReadyAsync(default); await Until(() => r.Workers.Length == 1); r.Workers[0].Fail(new Exception("load failure")); await Throws(() => load);
            Check(r.Host.WorkerCount == 0 && r.Workers[0].Disposed); r.HoldPrimary = false; await r.Host.EnsureReadyAsync(default); Check(r.Host.Ready);
        });
        await Test("Cancelling initial load disposes partial worker", async () =>
        { using var r = new Rig { HoldPrimary = true }; using var ct = new CancellationTokenSource(); var load = r.Host.EnsureReadyAsync(ct.Token); await Until(() => r.Workers.Length == 1); ct.Cancel(); await Throws(() => load); Check(r.Workers[0].Disposed && r.Host.WorkerCount == 0); });
        await Test("Mode change during loading cancels candidate; new session loads chosen model", async () =>
        {
            using var r = new Rig { HoldPrimary = true }; var old = r.Host.EnsureReadyAsync(default); await Until(() => r.Workers.Length == 1);
            r.Host.SetMode(TextModels.Gemma); r.HoldPrimary = false;
            var newer = r.Host.EnsureReadyAsync(default); r.Workers[0].Fail(new Exception("late failure"));
            await Throws(() => old); await newer;
            Check(r.Host.Ready && r.Host.ActiveModel == TextModels.Gemma && r.Host.WorkerCount == 1 && r.Workers[0].Disposed);
        });
        await Test("Late initial completion cannot interfere with restarted model", async () =>
        {
            using var r = new Rig { HoldPrimary = true }; var old = r.Host.EnsureReadyAsync(default); await Until(() => r.Workers.Length == 1);
            r.Host.Stop(); r.HoldPrimary = false; var newer = r.Host.EnsureReadyAsync(default); r.Workers[0].Complete(); await Throws(() => old); await newer; Check(r.Host.Ready && r.Host.WorkerCount == 1);
        });
        await Test("Crashed worker cancels requests and reloads safely", async () =>
        {
            using var r = new Rig(); await r.Host.EnsureReadyAsync(default); using var old = r.Host.Acquire(); r.Workers[0].Crash();
            await r.Host.EnsureReadyAsync(default);
            Check(old.SessionToken.IsCancellationRequested && r.Workers[0].Disposed && r.Host.ActiveModel == TextModels.Qwen35 && r.Host.WorkerCount == 1);
        });
        await Test("Repeated stop/dispose and restart leak no fake workers", async () =>
        { using var r = new Rig(); for (var i = 0; i < 20; i++) { await r.Host.EnsureReadyAsync(default); r.Host.Stop(); r.Host.Stop(); Check(r.Host.WorkerCount == 0); } r.Host.Dispose(); r.Host.Dispose(); await Throws(() => r.Host.EnsureReadyAsync(default)); Check(r.Workers.All(w => w.Disposed)); });
        await Test("Model usage counts each cleanup per model, mode and words; survives save format", () =>
        {
            var usage = new ModelUsage();
            Check(usage.Dictations == 0 && usage.For(TextModels.Gemma).Dictations == 0);
            usage.Record(TextModels.Qwen35, true, "Um, send the report at 3:30, okay?");
            usage.Record(TextModels.Gemma, true, "It's state-of-the-art work.");
            usage.Record(TextModels.Qwen35, false, "");
            Check(usage.For(TextModels.Qwen35) is { Dynamic: 1, Manual: 1, Dictations: 2, Words: 7 }, "Qwen counts");
            Check(usage.For(TextModels.Gemma) is { Dynamic: 1, Manual: 0, Words: 3 } && usage.Dictations == 3 && usage.Words == 10, "Gemma counts");
            var json = JsonSerializer.Serialize(usage);
            Check(!json.Contains("Dictations") && !json.Contains("report"), "Only counts are stored");
            var loaded = JsonSerializer.Deserialize<ModelUsage>(json)!;
            Check(loaded.Dictations == 3 && loaded.Since == usage.Since && loaded.For(TextModels.Gemma).Words == 3, "Round-trip");
            return Task.CompletedTask;
        });
        await Test("Model usage persists across restarts and keeps an unreadable file", () =>
        {
            var folder = Path.Combine(Path.GetTempPath(), "lazytype-usage-" + Guid.NewGuid().ToString("N"));
            try
            {
                var file = Path.Combine(folder, "usage.json");
                Check(ModelUsage.Load(file).Dictations == 0, "Missing file starts empty");
                var first = ModelUsage.Load(file); first.Record(TextModels.Gemma, true, "one two three"); first.Save(file);
                var second = ModelUsage.Load(file); second.Record(TextModels.Qwen35, false, "four"); second.Save(file);
                var third = ModelUsage.Load(file);
                Check(third.Dictations == 2 && third.Words == 4 && third.Since == first.Since && !File.Exists(file + ".tmp"), "Counts accumulate across loads");
                File.WriteAllText(file, "{ not json");
                Check(ModelUsage.Load(file).Dictations == 0 && File.Exists(file + ".unreadable") && !File.Exists(file), "Corrupt file is set aside");
            }
            finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
            return Task.CompletedTask;
        });
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
        foreach (var model in TextModels.Choices.Where(m => !args.Contains("--gemma-only") || m == TextModels.Gemma))
            await Test("REAL speech + cleanup + polish + reword + unload: " + model, async () =>
            {
                using var host = new EngineHost(); host.SetTextModel(model); var watch = Stopwatch.StartNew(); await host.EnsureReadyAsync(default);
                Check(host.Ready && host.ActiveTextModel == model && host.TextWorkerCount == 1);
                Console.WriteLine($"  LOAD {watch.Elapsed.TotalSeconds:F2}s; process RAM {host.ModelWorkingSetBytes()/1073741824.0:F2} GiB");
                if (audioIndex >= 0) { var transcript = await host.TranscribeAsync(File.ReadAllBytes(args[audioIndex + 1]), default); Check(transcript.Contains("country", StringComparison.OrdinalIgnoreCase), "Public audio transcription"); }
                foreach (var source in new[] { "Um, please send the report at 3:30 on 2026-10-05.", "Run --dry-run.\n\nKeep the output in report.txt." })
                {
                    var (cleaned, editor) = await host.RewriteWithModelAsync(source, TextEditModes.Cleanup, default);
                    Check(editor == model && EngineHost.PreservesNumbers(source, cleaned), "Cleanup reports its actual model and preserves numbers");
                    var (reworded, rewordEditor) = await host.RewriteWithModelAsync(source, TextEditModes.Reword, default);
                    Check(rewordEditor == model && EngineHost.PreservesRewordNumbers(source, reworded), "Reword reports its actual worker and preserves numbers");
                    var polished = await host.SuggestAsync(source, default); Check(EngineHost.PreservesNumbers(source, polished));
                    if (source.Contains("--dry-run")) Check(cleaned.Contains("--dry-run") && polished.Contains("--dry-run") && cleaned.Contains('\n') && polished.Contains('\n'), "Flags/paragraphs preserved end to end");
                }
                host.Stop(); Check(!host.Ready && host.TextWorkerCount == 0);
            });
        if (args.Contains("--gemma-only")) return;
        await Test("REAL stop during model loading and restart with different model", async () =>
        {
            using var host = new EngineHost(); host.SetTextModel(TextModels.Gemma); var loading = host.EnsureReadyAsync(default); await Task.Delay(250); host.Stop();
            await Throws(() => loading); Check(host.TextWorkerCount == 0);
            host.SetTextModel(TextModels.Qwen35); await host.EnsureTextReadyAsync(default); Check(host.ActiveTextModel == TextModels.Qwen35 && host.TextWorkerCount == 1); host.Stop();
        });
    }
    private sealed class Rig : IDisposable
    {
        public readonly TextModelHost Host;
        private readonly ConcurrentQueue<FakeWorker> workers = new();
        public FakeWorker[] Workers => workers.ToArray();
        public bool HoldPrimary, FactoryFailure;
        public Rig()
        {
            Host = new(model =>
            {
                if (FactoryFailure) throw new FileNotFoundException();
                var worker = new FakeWorker(model, HoldPrimary); workers.Enqueue(worker); return worker;
            });
        }
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
