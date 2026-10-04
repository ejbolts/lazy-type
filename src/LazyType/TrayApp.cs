using System.IO.Pipes;
using NAudio.Wave;

namespace LazyType;

internal sealed class TrayApp : ApplicationContext
{
    private readonly MainForm form = new();
    private readonly RecordingOverlay overlay = new();
    private readonly EngineHost engines = new();
    private readonly AppSettings settings = AppSettings.Load();
    private readonly ThemeController themes;
    private readonly NotifyIcon tray = new();
    private readonly CancellationTokenSource shutdown = new();
    private CancellationTokenSource lifetime = new();
    private CancellationTokenSource? operation;
    private Microphone? mic;
    private TextTarget? target;
    private bool recording, processing, paused, closing, rawMode;
    private int generation;
    private readonly List<(int Id, string Name)> devices;
    private readonly byte[]? testAudio;
    public TrayApp(bool show, string? testAudioPath = null)
    {
        if (testAudioPath != null)
        {
            using var test = new WaveFileReader(testAudioPath);
            if (test.WaveFormat.SampleRate != 16000 || test.WaveFormat.BitsPerSample != 16 || test.WaveFormat.Channels != 1 || test.TotalTime > TimeSpan.FromMinutes(2))
                throw new ArgumentException("Test audio must be a mono 16 kHz, 16-bit WAV under two minutes.");
            testAudio = File.ReadAllBytes(testAudioPath);
            form.Text = "Lazy Type · Test audio";
        }
        _ = form.Handle;
        themes = new ThemeController(form, overlay, settings, settings.Save);
        devices = Microphone.Devices();
        foreach (var item in devices) form.Mic.Items.Add(item.Name);
        form.Mic.SelectedIndex = Math.Max(0, devices.FindIndex(d => d.Id == settings.Microphone));
        settings.Microphone = devices[form.Mic.SelectedIndex].Id;
        form.Clean.Checked = settings.Cleanup; form.Startup.Checked = AppSettings.Startup;
        form.HotkeyChoice.SelectedItem = settings.Hotkey;
        if (form.HotkeyChoice.SelectedIndex < 0) form.HotkeyChoice.SelectedIndex = 0;
        form.Mic.SelectedIndexChanged += (_, _) => { settings.Microphone = devices[form.Mic.SelectedIndex].Id; settings.Save(); };
        form.Clean.CheckedChanged += (_, _) => { settings.Cleanup = form.Clean.Checked; settings.Save(); };
        form.Startup.CheckedChanged += (_, _) => { try { AppSettings.Startup = form.Startup.Checked; } catch (Exception e) { Report(e); } };
        form.HotkeyChoice.SelectedIndexChanged += (_, _) => { settings.Hotkey = form.HotkeyChoice.SelectedItem!.ToString()!; settings.Save(); RegisterHotkeys(); };
        form.HotkeyPressed += id =>
        {
            if (id == 3) Cancel();
            else if (id == 4) TogglePause();
            else _ = ToggleAsync(id == 2);
        };
        form.PauseRequested += TogglePause; form.QuitRequested += Quit; form.ImportRequested += () => _ = ImportAsync();
        overlay.AudioLevel = () => mic?.Level ?? 0;
        overlay.Owner = form;
        tray.Icon = Native.MakeIcon(Color.FromArgb(65, 98, 211)); tray.Text = "Lazy Type · Local dictation";
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open Lazy Type", null, (_, _) => form.ShowWindow());
        menu.Items.Add("Pause / resume dictation", null, (_, _) => TogglePause());
        menu.Items.Add("Quit and free memory", null, (_, _) => Quit());
        tray.ContextMenuStrip = menu; tray.DoubleClick += (_, _) => form.ShowWindow(); tray.Visible = true;
        RegisterHotkeys();
        Status("Ready · models unloaded", "Microphone off · Models load only when you start dictating");
        if (show) form.ShowWindow();
        _ = ListenAsync();
    }
    private void UI(Action action) { if (!closing && !form.IsDisposed) try { if (form.InvokeRequired) form.BeginInvoke(action); else action(); } catch { } }
    private void Status(string title, string explanation)
    {
        form.SetStatus(title, explanation, paused);
        tray.Text = "Lazy Type · " + (recording ? "Microphone on" : paused ? "Paused" : "Microphone off");
    }
    private void RegisterHotkeys()
    {
        foreach (var id in new[] { 1, 2, 4 }) Native.UnregisterHotKey(form.Handle, id);
        uint modifiers = settings.Hotkey == "F8" ? 0u : settings.Hotkey == "Ctrl+Shift+Space" ? 6u : 3u;
        uint key = settings.Hotkey == "F8" ? 0x77u : 0x20u;
        // For Ctrl+Shift+Space, add Alt for the alternate raw shortcut.
        var alternate = settings.Hotkey == "Ctrl+Shift+Space" ? 7u : modifiers | 4u;
        var first = Native.RegisterHotKey(form.Handle, 1, modifiers | 0x4000, key);
        var second = Native.RegisterHotKey(form.Handle, 2, alternate | 0x4000, key);
        var pauseKey = Native.RegisterHotKey(form.Handle, 4, 7 | 0x4000, 0x50);
        form.SetShortcut(settings.Hotkey);
        if (!first || !second)
        {
            form.ShowWindow();
            MessageBox.Show(form, "A dictation shortcut is already used by another application. Choose another Dictation hotkey here.", "Shortcut conflict", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        if (!pauseKey) AppLog.Write("Optional pause shortcut unavailable; use the pause button.");
    }
    private void EscapeEnabled(bool enabled)
    {
        Native.UnregisterHotKey(form.Handle, 3);
        if (enabled) Native.RegisterHotKey(form.Handle, 3, 0x4000, 0x1B);
    }
    private async Task WarmAsync()
    {
        var token = lifetime.Token;
        try
        {
            await engines.EnsureReadyAsync(token);
        }
        // Recording continues if preload fails; processing will retry and report errors.
        catch (Exception) { if (!token.IsCancellationRequested) AppLog.Write("Background model load failed; will retry after recording."); }
    }
    private void ReleaseModels()
    {
        var previous = lifetime;
        previous.Cancel(); engines.Stop();
        lifetime = new(); previous.Dispose();
    }
    private async Task ToggleAsync(bool raw)
    {
        if (closing) return;
        if (recording) { await FinishRecordingAsync(); return; }
        if (processing) return;
        try
        {
            if (paused) Resume();
            target = TextTarget.Capture();
            rawMode = raw || !settings.Cleanup;
            if (testAudio == null)
            {
                var next = new Microphone(settings.Microphone);
                mic = next;
                next.LimitReached += () => UI(() => { if (recording) _ = FinishRecordingAsync(); });
                next.Start();
            }
            recording = true;
            EscapeEnabled(true);
            overlay.Present(testAudio != null ? "Test audio" : rawMode ? "Listening · raw" : "Listening", true);
            Status(testAudio == null ? "Listening" : "Test recording", testAudio == null ? "Microphone on · Press the same hotkey again to stop · 2 minute limit" : "Test fixture selected · Microphone off · Press the same hotkey to process");
            _ = WarmAsync();
        }
        catch (Exception e) { mic?.Dispose(); mic = null; recording = false; EscapeEnabled(false); overlay.Dismiss(); ReleaseModels(); Report(e); }
    }
    private async Task FinishRecordingAsync()
    {
        var currentMic = mic;
        if (!recording || (currentMic == null && testAudio == null)) return;
        recording = false; processing = true;
        var currentGeneration = ++generation;
        using var currentOperation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        operation = currentOperation;
        var ct = currentOperation.Token; var destination = target; var raw = rawMode;
        try
        {
            overlay.Present("Stopping microphone…");
            var wav = testAudio ?? await currentMic!.StopAsync();
            var speech = testAudio != null || currentMic!.HasSpeech; currentMic?.Dispose(); if (mic == currentMic) mic = null;
            ct.ThrowIfCancellationRequested();
            if (!speech) { Status("No speech detected · models unloaded", "Microphone off · Try speaking closer or select another microphone."); return; }
            await ProcessAudioAsync(wav, raw, destination, ct);
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { if (!ct.IsCancellationRequested) Report(e); }
        finally
        {
            if (operation == currentOperation) operation = null;
            currentMic?.Dispose(); if (mic == currentMic) mic = null;
            if (currentGeneration == generation) { ReleaseModels(); processing = false; overlay.Dismiss(); EscapeEnabled(false); }
        }
    }
    private async Task ProcessAudioAsync(byte[] wav, bool raw, TextTarget? destination, CancellationToken ct)
    {
        overlay.Present(engines.Ready ? "Transcribing…" : "Loading local models…");
        Status("Transcribing your speech", "Microphone off · Processing locally");
        await engines.EnsureReadyAsync(ct);
        overlay.Present("Transcribing…");
        var timer = Stopwatch.StartNew();
        var transcript = await engines.TranscribeAsync(wav, ct);
        ct.ThrowIfCancellationRequested(); form.Original.Text = transcript;
        if (string.IsNullOrWhiteSpace(transcript)) { Status("No speech detected · models unloaded", "Microphone off · Nothing inserted."); return; }
        var result = transcript; var fallback = false;
        if (!raw)
        {
            overlay.Present("Cleaning up…"); Status("Cleaning up your wording", "Microphone off · Qwen is editing locally");
            try { result = await engines.CleanupAsync(transcript, ct); }
            catch (Exception) when (!ct.IsCancellationRequested) { fallback = true; AppLog.Write("Cleanup failed; original transcript retained."); }
        }
        ct.ThrowIfCancellationRequested(); form.Result.Text = result;
        // Inference is finished; release GPU allocations before clipboard insertion.
        engines.Stop();
        overlay.Dismiss();
        var inserted = destination != null && await destination.InsertAsync(result, ct);
        ct.ThrowIfCancellationRequested();
        var seconds = timer.Elapsed.TotalSeconds;
        AppLog.Write($"Dictation complete: {seconds:F1}s; inserted={inserted}; raw={raw}; fallback={fallback}.");
        if (!inserted && destination != null)
        {
            tray.ShowBalloonTip(4000, "Your text is ready", "Focus changed or insertion was blocked. Open Lazy Type to copy your result.", ToolTipIcon.Info);
            Status("Text ready to copy · models unloaded", "The original field is no longer focused. Your result is safely available below.");
        }
        else Status("Ready · models unloaded", fallback ? "Cleanup was unavailable. Your original text was kept." : $"Microphone off · Last dictation processed in {seconds:F1}s · All processing stayed on this PC");
    }
    private async Task ImportAsync()
    {
        if (recording || processing) return;
        using var dialog = new OpenFileDialog { Filter = "WAV audio (*.wav)|*.wav", Title = "Test local transcription with a WAV recording" };
        if (dialog.ShowDialog(form) != DialogResult.OK) return;
        if (paused) Resume();
        processing = true; var id = ++generation;
        using var currentOperation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        operation = currentOperation; var ct = currentOperation.Token;
        EscapeEnabled(true);
        try
        {
            using var reader = new WaveFileReader(dialog.FileName);
            if (reader.TotalTime > TimeSpan.FromMinutes(2)) throw new InvalidOperationException("Please use a test recording shorter than two minutes.");
            using var resampler = new MediaFoundationResampler(reader, new WaveFormat(16000, 16, 1));
            using var buffer = new MemoryStream();
            WaveFileWriter.WriteWavFileToStream(buffer, resampler);
            await ProcessAudioAsync(buffer.ToArray(), !settings.Cleanup, null, ct);
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { if (!ct.IsCancellationRequested) Report(e); }
        finally
        {
            if (operation == currentOperation) operation = null;
            if (id == generation) { ReleaseModels(); processing = false; overlay.Dismiss(); EscapeEnabled(false); }
        }
    }
    private void Cancel()
    {
        ++generation; operation?.Cancel();
        mic?.Dispose(); mic = null;
        ReleaseModels();
        recording = false; processing = false; overlay.Dismiss(); EscapeEnabled(false);
        Status(paused ? "Paused · models unloaded" : "Cancelled · models unloaded", "Microphone off · No new text will be inserted.");
    }
    private void TogglePause() { if (paused) Resume(); else Pause("Microphone off · GPU and model memory released. Resume when you are ready."); }
    private void Pause(string reason)
    {
        paused = true; Cancel();
        Status("Paused · models unloaded", reason); AppLog.Write("Paused; model workers terminated.");
    }
    private void Resume()
    {
        paused = false;
        Status("Ready · models unloaded", "Microphone off · Models load when you start dictating");
    }
    private void Report(Exception e)
    {
        AppLog.Write("Operation failed: " + e.GetType().Name);
        Status("Needs attention", e.Message);
        tray.ShowBalloonTip(5000, "Lazy Type", e.Message, ToolTipIcon.Warning);
    }
    private async Task ListenAsync()
    {
        while (!shutdown.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream("LazyType-show", PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(shutdown.Token);
                using var reader = new StreamReader(pipe);
                var command = await reader.ReadLineAsync(shutdown.Token);
                UI(() => { if (command == "quit") Quit(); else if (command == "pause") { if (!paused) TogglePause(); } else form.ShowWindow(); });
            }
            catch (OperationCanceledException) { break; }
            catch { await Task.Delay(300); }
        }
    }
    private void Quit()
    {
        if (closing) return;
        closing = true; shutdown.Cancel(); operation?.Cancel(); lifetime.Cancel();
        mic?.Dispose(); themes.Dispose(); overlay.Dispose(); engines.Dispose();
        for (var id = 1; id <= 4; id++) Native.UnregisterHotKey(form.Handle, id);
        tray.Visible = false; tray.Icon?.Dispose(); tray.Dispose(); form.Quitting = true; form.Close(); form.Dispose();
        AppLog.Write("Exited; microphone closed and models released."); ExitThread();
    }
}
