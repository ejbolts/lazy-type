using System.IO.Pipes;
using NAudio.Wave;

namespace LazyType;

internal sealed class TrayApp : ApplicationContext
{
    private readonly MainForm form = new();
    private readonly RecordingOverlay overlay = new();
    private readonly TextHighlight highlight = new();
    private SuggestionForm? suggestionForm;
    // The open suggestion: the field it marks, the wording it was compared against, and that comparison.
    private TextTarget? suggestionDestination;
    private string? suggestionSource;
    private IReadOnlyList<DiffPart>? suggestionParts;
    private IReadOnlyList<ChangeMark>? suggestionMarks;
    private bool updatingResult;
    // The field to check automatically once the dictation that was inserted into it has finished.
    private TextTarget? pendingSuggestion;
    private readonly EngineHost engines = new();
    private readonly AppSettings settings;
    private readonly bool testSession;
    private readonly ThemeController themes;
    private readonly NotifyIcon tray = new();
    private readonly CancellationTokenSource shutdown = new();
    private CancellationTokenSource lifetime = new();
    private CancellationTokenSource? operation;
    private Microphone? mic;
    private TextTarget? target;
    private bool recording, processing, paused, closing, rawMode, suggesting;
    private int generation;
    private readonly List<(int Id, string Name)> devices;
    private readonly byte[]? testAudio;

    public TrayApp(bool show, string? testAudioPath = null, bool testSession = false)
    {
        this.testSession = testSession;
        settings = testSession ? new AppSettings { Hotkey = "F8" } : AppSettings.Load();
        if (testAudioPath != null)
        {
            using var test = new WaveFileReader(testAudioPath);
            if (test.WaveFormat.SampleRate != 16000 || test.WaveFormat.BitsPerSample != 16 || test.WaveFormat.Channels != 1 || test.TotalTime > TimeSpan.FromMinutes(2))
                throw new ArgumentException("Test audio must be a mono 16 kHz, 16-bit WAV under two minutes.");
            testAudio = File.ReadAllBytes(testAudioPath);
            form.Text = testSession ? "Lazy Type · Isolated test" : "Lazy Type · Test audio";
        }
        _ = form.Handle;
        themes = new ThemeController(form, overlay, settings, SaveSettings);
        devices = Microphone.Devices();
        foreach (var item in devices) form.Mic.Items.Add(item.Name);
        form.Mic.SelectedIndex = Math.Max(0, devices.FindIndex(d => d.Id == settings.Microphone));
        settings.Microphone = devices[form.Mic.SelectedIndex].Id;
        form.Clean.Checked = settings.Cleanup;
        form.Startup.Checked = !testSession && AppSettings.Startup;
        form.Startup.Enabled = !testSession;
        form.Suggestions.Checked = settings.Suggestions;
        settings.TextModel = TextModels.Normalize(settings.TextModel);
        form.Models.SelectedModel = settings.TextModel;
        engines.SetTextModel(settings.TextModel);
        form.Models.SelectionChanged += () =>
        {
            settings.TextModel = form.Models.SelectedModel;
            engines.SetTextModel(settings.TextModel);
            SaveSettings();
        };
        engines.TextStateChanged += () => UI(() => form.Models.SetStatus(engines.TextStatus));
        form.HotkeyChoice.SelectedItem = settings.Hotkey;
        if (form.HotkeyChoice.SelectedIndex < 0) form.HotkeyChoice.SelectedIndex = 0;
        form.Mic.SelectedIndexChanged += (_, _) => { settings.Microphone = devices[form.Mic.SelectedIndex].Id; SaveSettings(); };
        form.Clean.CheckedChanged += (_, _) => { settings.Cleanup = form.Clean.Checked; SaveSettings(); };
        form.Suggestions.CheckedChanged += (_, _) =>
        {
            settings.Suggestions = form.Suggestions.Checked; SaveSettings();
            if (!settings.Suggestions) CloseSuggestion();
        };
        form.SuggestionRequested += () => _ = SuggestAsync(null);
        form.Result.TextChanged += (_, _) => { if (!updatingResult) CloseSuggestion(); };
        highlight.ChangeClicked += index => _ = ApplyChangeAsync(index);
        form.Startup.CheckedChanged += (_, _) => { try { AppSettings.Startup = form.Startup.Checked; } catch (Exception e) { Report(e); } };
        form.HotkeyChoice.SelectedIndexChanged += (_, _) => { settings.Hotkey = form.HotkeyChoice.SelectedItem!.ToString()!; SaveSettings(); RegisterHotkeys(); };
        form.HotkeyChoice.Enabled = !testSession;
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
        if (!testSession) _ = ListenAsync();
    }

    private void SaveSettings() { if (!testSession) settings.Save(); }
    private void SetBusy(bool busy)
    {
        form.SetSuggestionBusy(busy);
        form.Models.Enabled = !recording && !processing;
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
        var pauseKey = testSession || Native.RegisterHotKey(form.Handle, 4, 7 | 0x4000, 0x50);
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
        CloseSuggestion();
        try
        {
            if (paused) Resume();
            target = testSession ? null : TextTarget.Capture();
            rawMode = raw || !settings.Cleanup;
            if (testAudio == null)
            {
                var next = new Microphone(settings.Microphone);
                mic = next;
                next.LimitReached += () => UI(() => { if (recording) _ = FinishRecordingAsync(); });
                next.Start();
            }
            recording = true;
            SetBusy(true);
            EscapeEnabled(true);
            overlay.Present(testAudio != null ? "Test audio" : rawMode ? "Listening · raw" : "Listening", true);
            Status(testAudio == null ? "Listening" : "Test recording", testAudio == null ? "Microphone on · Press the same hotkey again to stop · 2 minute limit" : "Test fixture selected · Microphone off · Press the same hotkey to process");
            _ = WarmAsync();
        }
        catch (Exception e) { mic?.Dispose(); mic = null; recording = false; SetBusy(false); EscapeEnabled(false); overlay.Dismiss(); ReleaseModels(); Report(e); }
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
            if (currentGeneration == generation)
            {
                var next = pendingSuggestion; pendingSuggestion = null;
                processing = false; overlay.Dismiss(); EscapeEnabled(false);
                // Keep the text model loaded and check the wording straight away; it is released afterwards.
                if (next != null && !ct.IsCancellationRequested && !closing && !paused) _ = SuggestAsync(next, automatic: true);
                else { ReleaseModels(); SetBusy(paused); }
            }
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
            overlay.Present("Cleaning up…"); Status("Cleaning up your wording", "Microphone off · Editing locally");
            try { result = await engines.CleanupAsync(transcript, ct); }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                fallback = true;
                result = EngineHost.CleanPauseDashes(transcript);
                AppLog.Write("Cleanup failed; original transcript retained with pause dashes removed.");
            }
        }
        ct.ThrowIfCancellationRequested(); form.Result.Text = result;
        if (!testSession) Native.SetClipboardText(result);
        // Inference is finished; release GPU allocations before clipboard insertion, unless the text model is
        // about to check the wording of this dictation.
        var autoCheck = settings.Suggestions && destination != null && result.Length <= 6000;
        if (!autoCheck) engines.Stop();
        overlay.Dismiss();
        var inserted = destination != null && await destination.InsertAsync(result, ct);
        ct.ThrowIfCancellationRequested();
        if (inserted && settings.Suggestions && destination != null)
        {
            destination.RememberInsertion(result);
            pendingSuggestion = destination;
        }
        var seconds = timer.Elapsed.TotalSeconds;
        AppLog.Write($"Dictation complete: {seconds:F1}s; inserted={inserted}; raw={raw}; fallback={fallback}.");
        if (!inserted && destination != null)
        {
            tray.ShowBalloonTip(4000, "Your text is ready", "Focus changed or insertion was blocked. Your text is copied to your clipboard and ready to paste.", ToolTipIcon.Info);
            Status("Text copied to clipboard · models unloaded", "The original field is no longer focused. Your result is copied to your clipboard.");
        }
        else if (destination == null)
        {
            Status(testSession ? "Test result ready · models unloaded" : "Text copied to clipboard · models unloaded",
                testSession ? "Sample audio processed · Microphone off · Your result is shown below." : "Your result was copied to the clipboard and is ready to paste.");
        }
        else Status("Ready · models unloaded", fallback ? "Cleanup was unavailable. Your original text was kept." : $"Microphone off · Last dictation processed in {seconds:F1}s · All processing stayed on this PC");
    }

    private async Task ImportAsync()
    {
        if (recording || processing) return;
        using var dialog = new OpenFileDialog { Filter = "WAV audio (*.wav)|*.wav", Title = "Test local transcription with a WAV recording" };
        if (dialog.ShowDialog(form) != DialogResult.OK) return;
        CloseSuggestion();
        if (paused) Resume();
        processing = true; var id = ++generation;
        SetBusy(true);
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
            if (id == generation) { ReleaseModels(); processing = false; SetBusy(paused); overlay.Dismiss(); EscapeEnabled(false); }
        }
    }

    private void CloseSuggestion()
    {
        highlight.Dismiss();
        suggestionDestination = null; suggestionSource = null; suggestionParts = null; suggestionMarks = null;
        var open = suggestionForm;
        open?.Close();
        // A preview that was never shown (an automatic check) gets no close event, so release it here.
        if (open != null && suggestionForm == open && !open.Visible) { suggestionForm = null; open.Dispose(); }
    }

    // automatic: run straight after dictation. The preview only appears, without taking focus, when there
    // is something to change; otherwise it closes quietly.
    private async Task SuggestAsync(TextTarget? destination, bool automatic = false)
    {
        if (!settings.Suggestions || recording || processing || paused || closing || string.IsNullOrWhiteSpace(form.Result.Text)) return;
        var source = form.Result.Text;
        CloseSuggestion();
        var popupDark = ThemeController.Resolve(settings.PopupTheme, ThemeController.Resolve(settings.Theme, ThemeController.IsSystemDark));
        var preview = new SuggestionForm(source, destination != null, popupDark) { Passive = automatic };
        suggestionForm = preview;
        var anchor = destination != null && destination.TryGetBounds(out var field) ? field : form.Bounds;
        void PositionPreview()
        {
            var area = Screen.FromRectangle(anchor).WorkingArea;
            var x = destination == null ? anchor.Left + (anchor.Width - preview.Width) / 2 : anchor.Right - preview.Width;
            var y = destination == null ? anchor.Top + (anchor.Height - preview.Height) / 2 : anchor.Bottom + 8;
            if (destination != null && y + preview.Height > area.Bottom) y = anchor.Top - preview.Height - 8;
            preview.Location = new Point(Math.Clamp(x, area.Left, Math.Max(area.Left, area.Right - preview.Width)),
                Math.Clamp(y, area.Top, Math.Max(area.Top, area.Bottom - preview.Height)));
        }
        PositionPreview();
        preview.Shown += (_, _) => PositionPreview();
        preview.FormClosed += (_, _) =>
        {
            if (suggestionForm != preview) return;
            suggestionForm = null;
            // Keep original or the close button: stop any check and clear the marks in the field.
            if (suggesting) Cancel(); else CloseSuggestion();
        };
        suggestionDestination = destination;
        preview.ApplyRequested += () => _ = ApplySuggestionAsync(preview, destination);
        // Mark the dictated text in the field first so the preview stays above the marker.
        if (destination != null) highlight.Present(destination);
        if (!automatic) preview.Show();
        processing = true; suggesting = true; SetBusy(true); EscapeEnabled(true);
        var id = ++generation;
        using var currentOperation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        operation = currentOperation; var ct = currentOperation.Token;
        Status("Suggesting clearer wording", "Microphone off · Editing locally · Esc cancels");
        try
        {
            if (source.Length > 6000) throw new InvalidOperationException("Suggestions support up to 6,000 characters. Shorten the result and try again.");
            await engines.EnsureTextReadyAsync(ct);
            var text = await engines.SuggestAsync(source, ct);
            ct.ThrowIfCancellationRequested();
            if (suggestionForm == preview && form.Result.Text == source)
            {
                if (automatic && !TextDiff.HasChanges(TextDiff.Compare(source, text)))
                {
                    suggesting = false; CloseSuggestion();
                    Status("Ready · models unloaded", "Microphone off · Your wording already reads clearly, so nothing was marked.");
                    return;
                }
                ShowSuggestion(preview, destination, source, text);
                PositionPreview();
                if (!preview.Visible) preview.Show();
            }
            Status("Suggestion ready · models unloaded", "Review the suggestion before applying. Your original transcript is still available.");
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            if (!ct.IsCancellationRequested && suggestionForm == preview && automatic)
            {
                suggesting = false; CloseSuggestion();
                Status("Suggestion unavailable · models unloaded", e.Message);
                AppLog.Write("Automatic suggestion failed: " + e.GetType().Name);
            }
            else if (!ct.IsCancellationRequested && suggestionForm == preview)
            {
                preview.ShowFailure("Suggestion unavailable. Your text is unchanged. Close and try again.");
                highlight.Dismiss();
                Status("Suggestion unavailable · models unloaded", e.Message);
                AppLog.Write("Suggestion failed: " + e.GetType().Name);
            }
        }
        finally
        {
            if (operation == currentOperation) operation = null;
            if (id == generation) { ReleaseModels(); processing = false; suggesting = false; SetBusy(paused); EscapeEnabled(false); }
        }
    }

    // Compares the current wording with the suggestion and shows what is left to change.
    private void ShowSuggestion(SuggestionForm preview, TextTarget? destination, string source, string text)
    {
        var parts = TextDiff.Compare(source, text);
        var marks = TextDiff.HasChanges(parts) ? TextDiff.Marks(source, parts) : new List<ChangeMark>();
        var canApply = destination == null || destination.CanReplaceInsertion();
        // Mark the edits in the field when the editor can locate them; otherwise show the full comparison.
        var inline = destination != null && canApply && highlight.Settle(marks);
        if (!inline) highlight.Dismiss();
        if (destination != null && !inline && marks.Count > 0)
            AppLog.Write(canApply ? "Suggestion marks unavailable: the editor could not locate every change." : "Suggestion marks unavailable: the field changed since dictation or cannot be replaced.");
        suggestionSource = source; suggestionParts = parts; suggestionMarks = inline ? marks : null;
        preview.ShowSuggestion(text, parts, canApply, inline);
    }

    // Applies one marked change by replacing the dictated range with the wording plus just that edit.
    private async Task ApplyChangeAsync(int index)
    {
        var preview = suggestionForm; var destination = suggestionDestination; var source = suggestionSource;
        var parts = suggestionParts; var marks = suggestionMarks;
        if (processing || preview == null || destination == null || source == null || parts == null || marks == null
            || index < 0 || index >= marks.Count || form.Result.Text != source || !settings.Suggestions) { highlight.Release(); return; }
        var text = TextDiff.ApplyOne(parts, marks[index]);
        var suggested = preview.SuggestedText;
        processing = true; suggesting = true; SetBusy(true);
        var id = ++generation;
        using var currentOperation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        operation = currentOperation; var ct = currentOperation.Token;
        try
        {
            if (await destination.ReplaceInsertionAsync(text, ct))
            {
                ct.ThrowIfCancellationRequested();
                destination.RememberInsertion(text);
                updatingResult = true;
                try { form.Result.Text = text; }
                finally { updatingResult = false; }
                if (TextDiff.HasChanges(TextDiff.Compare(text, suggested)))
                {
                    ShowSuggestion(preview, destination, text, suggested);
                    Status("Change applied", "Only that part of your dictation was changed. Click another mark or apply the rest.");
                }
                else
                {
                    suggesting = false; CloseSuggestion();
                    Status("Suggestion applied", "Every suggested change has been applied. Your original transcript is still available.");
                }
            }
            else if (!preview.IsDisposed)
            {
                highlight.Release();
                Status("Couldn't apply that change", "The field changed or replacement was blocked. Your text is unchanged.");
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception) { highlight.Release(); }
        finally
        {
            if (operation == currentOperation) operation = null;
            if (id == generation) { processing = false; suggesting = false; SetBusy(paused); }
        }
    }

    private async Task ApplySuggestionAsync(SuggestionForm preview, TextTarget? destination)
    {
        var source = suggestionSource;
        if (processing || suggestionForm != preview || source == null || form.Result.Text != source || !settings.Suggestions) return;
        var text = preview.SuggestedText;
        if (string.IsNullOrWhiteSpace(text)) return;
        if (destination == null)
        {
            CloseSuggestion(); form.Result.Text = text;
            Status("Suggestion applied", "The last result has been updated. Your original transcript is still available.");
            return;
        }
        processing = true; suggesting = true; SetBusy(true); preview.SetApplying();
        var id = ++generation;
        using var currentOperation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        operation = currentOperation; var ct = currentOperation.Token;
        try
        {
            if (await destination.ReplaceInsertionAsync(text, ct))
            {
                ct.ThrowIfCancellationRequested();
                suggesting = false; CloseSuggestion(); form.Result.Text = text;
                Status("Suggestion applied", "Only the dictated text was replaced. Your original transcript is still available.");
            }
            else if (!preview.IsDisposed)
            {
                preview.ShowFailure("The field changed or replacement was blocked. Copy the suggestion to use it.");
                preview.Activate();
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception) { if (!preview.IsDisposed) preview.ShowFailure("Couldn't replace the text. Copy the suggestion to use it."); }
        finally
        {
            if (operation == currentOperation) operation = null;
            if (id == generation) { processing = false; suggesting = false; SetBusy(paused); }
        }
    }

    private void Cancel()
    {
        ++generation; operation?.Cancel(); pendingSuggestion = null;
        mic?.Dispose(); mic = null;
        ReleaseModels();
        recording = false; processing = false; suggesting = false; CloseSuggestion(); SetBusy(paused); overlay.Dismiss(); EscapeEnabled(false);
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
        SetBusy(false);
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
        suggesting = false; suggestionForm?.Close(); highlight.Dispose();
        mic?.Dispose(); themes.Dispose(); overlay.Dispose(); engines.Dispose();
        for (var id = 1; id <= 4; id++) Native.UnregisterHotKey(form.Handle, id);
        tray.Visible = false; tray.Icon?.Dispose(); tray.Dispose(); form.Quitting = true; form.Close(); form.Dispose();
        AppLog.Write("Exited; microphone closed and models released."); ExitThread();
    }
}
