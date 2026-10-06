# Lazy Type

Local dictation for Windows with NVIDIA GPUs. Whisper Turbo transcribes your speech, your selected local model cleans up grammar and punctuation, and text is inserted into the active application. Processing stays entirely on your PC. Model workers load when needed and release memory when finished.

## Use

1. Press **Ctrl+Alt+Space** (or choose **Ctrl+Shift+Space** or **F8** in the app) to begin speaking.
2. The speech and language models load on demand the first time you speak, using your NVIDIA GPU.
3. When you finish speaking, press the hotkey again (or wait for the two-minute limit).
4. A compact, rounded, translucent indicator follows your mouse pointer and shows recording and microphone level, followed by transcription and cleanup. It stays within the screen's working area and does not take focus. The microphone closes before model processing starts.
5. After transcription and cleanup, your text is copied to the clipboard and pasted into the original field if it is still focused. The models then unload automatically (after the wording check, when AI suggestions are on); model files remain on disk for the next recording.
6. With **AI suggestions** on, the wording is checked straight after pasting and any suggested changes are marked in the field (see [AI suggestions](#ai-suggestions)).

**Ctrl+Alt+Shift+Space** toggles recording without grammar cleanup. **Escape** cancels an active recording or processing operation and unloads the models. **Ctrl+Alt+Shift+P** pauses/resumes dictation. Pause and Quit release any model workers and their GPU allocations. Resume only arms dictation; models load on the next recording. The dictation hotkey also resumes automatically when paused.

Closing the settings window hides it to the tray; it does not quit. Use **Quit** to stop everything. The microphone is never opened merely because the app is running. Recording has a two-minute limit and stops/transcribes automatically at that limit. Choose the microphone, hotkey, cleanup preference and startup preference in the app. Models are always released after each dictation, silent recording, cancellation or error; there is no idle retention period.

Hover over the memory note or its information icon for a measured usage sample. In the 5 October 2026 RTX 4080 test, both warm model workers used 1.01 GiB of system RAM (summed working sets, excluding the app) and approximately 4.15 GiB of additional GPU memory. Speech detection and cleanup were exercised before five samples were taken. GPU usage returned to roughly its baseline after unloading. GPU memory is estimated from the change in whole-device usage, so other applications can affect it. These are warm samples, not live readings or peak limits; longer dictation can change usage.

For the alternate Ctrl+Shift+Space hotkey, add Alt for raw dictation. For F8, use Shift+F8 for raw dictation. Shortcut conflicts are reported so another combination can be selected.

**App theme** defaults to **System** and follows the Windows app color setting, including changes while Lazy Type is running. Choose **Light** or **Dark** to override it. **Popup theme** independently controls the floating dictation indicator: **Follow app** (default), **Light**, or **Dark**. Both choices are saved. Use **Preview** to see the popup for five seconds without opening the microphone or loading models.

## Text model selection

Choose one checkbox in **Text model**. The selection is saved, and existing settings default to **Qwen3 4B (current)**. Manual choices are **Qwen3 4B**, **Qwen3.5 9B**, and **Gemma 4 12B**. Only the selected model loads. Choices are locked while recording or editing; change them when idle or paused.

**Dynamic** loads the current Qwen first. Five seconds after Qwen passes its readiness check, Gemma starts loading in the background. Qwen remains available during loading. Once Gemma passes its readiness check, new edits use Gemma; edits already running on Qwen finish on Qwen before it unloads. A failed or missing Gemma load leaves Qwen available and shows **Gemma unavailable**. There is no automatic retry loop during that recording. If a short dictation finishes before the handover, it uses Qwen and cancels the pending upgrade. Escape, Pause, Quit, and normal completion release both workers, including a partially loaded Gemma.

The overlap needs memory for Whisper, Qwen, and Gemma together, in addition to other applications. This flow is tested on an RTX 4080 with 16 GB VRAM. Select a manual model if your GPU cannot accommodate the overlap. Dynamic does not re-edit an already completed result.

The additional models are optional. Install all choices with:

```powershell
python scripts/setup_models.py --text-model all
```

Use `--text-model qwen35`, `gemma`, or `dynamic` to install only the corresponding additional weights, or pass `-TextModel all` to `scripts/install.ps1`. Defaults still download the current model only. Pinned Q4_K_M files are approximately 5.68 GB for Qwen3.5 and 7.12 GB for Gemma. Existing benchmark downloads under `benchmarks/*/models` are reused without copying them. Selecting a model does not download it; a missing manual model is reported when used. All text models run with thinking disabled for bounded editing responses.

## Model usage

Click **Usage** beside **Text model** in the main window (or choose **Model usage** from the gear or tray menu) to see how often each text model has cleaned your dictation. Each cleaned dictation counts once, for the model that actually edited it: in **Dynamic**, that is Qwen before the handover and Gemma after it. Manual selections count the same way. Each model shows its share of all dictations, the words you spoke to it, the average words per dictation, and how many came from Dynamic or a manual choice.

Raw dictations, failed cleanups and wording checks are not counted. Counts update live while the window is open. **Reset counts** clears them after confirmation. Counts are saved after every dictation in `usage.json` (in the app folder above), so they carry over when the app restarts or updates. Only counts are saved; your words are never stored. An unreadable file is kept as `usage.json.unreadable` rather than overwritten. Isolated test sessions keep their counts in memory only.

## AI suggestions

Turn on **AI suggestions · check wording after dictation** in the main window. It is off by default and saved separately from speech cleanup.

1. Dictate into a field in another app as usual. Cleanup fixes the text and it is pasted.
2. The dictated text pulses purple while its wording is checked, usually for about a second.
3. If nothing needs changing, the highlight clears and nothing else appears.
4. Otherwise the changes are marked in the field, like a grammar checker:
   - removed words are struck through in red;
   - added words appear in small green labels just above or below where they go;
   - a green caret marks a pure insertion, such as a new comma;
   - a removal with no replacement gets a small red **×** label.
5. Click any label to apply just that change; the rest stay marked. A compact bar shows the change count: **Apply suggestion** applies everything left, **Keep original** dismisses the marks, **Copy** copies the full suggestion, and **Show wording** expands the full comparison.

The marks and bar never take focus, so you can keep typing. Labels sit on whichever side covers less of your other text. The wand beside **Last result** in Lazy Type runs the same check on demand and shows the comparison in the pop-up.

**How this differs from cleanup.** Both use the selected local text model with different instructions. Cleanup is automatic and minimal: it corrects grammar, punctuation and capitalisation, removes fillers, false starts and pause dashes, resolves self-corrections, and otherwise keeps your wording. Suggestions may reword awkward phrasing and improve flow, so they are only applied when you choose. Both keep facts, names, numbers and dates, and never answer questions or follow instructions in the dictation. A suggestion that changes too much or alters a number is discarded.

**Limits and safety.**

- Marks and replacement need Windows to expose the field's text and a unique range for the dictation. Editing the dictated text yourself, duplicate text, or editors that report the pasted text differently disable them; **Copy** still works. The reason (never the dictated text) is written to the log.
- Only the dictated range is replaced; surrounding text is preserved. Nothing is replaced until you click a label or **Apply suggestion**.
- Marks follow scrolling and window moves, are hidden wherever another window covers the field, and clear once the field is edited or the bar is closed. If an editor cannot locate every change, the pop-up shows the comparison instead.
- After a dictation, the text model stays loaded from cleanup for the check and is released straight afterward; from the **Last result** wand it loads only when clicked. The microphone is never opened. Escape, closing the bar, disabling suggestions, pausing or quitting cancels a check and releases the model.
- Suggestions support results up to 6,000 characters. Failed or incomplete edits leave your text unchanged.

## Models and privacy

- Whisper Large V3 Turbo Q5_0, approximately 574 MB model file.
- Qwen3-4B-Instruct-2507 Q4_K_M, approximately 2.5 GB model file.
- Silero VAD filters silence before transcription.
- Native CUDA engines use the NVIDIA GPU. Each text worker has a 4,096-token context and a single processing slot to bound memory use.
- No cloud transcription or cleanup; the only inference connections are loopback connections on this PC. The text server uses a per-session authentication key.
- Recordings are processed in memory. The last original and edited transcript are kept in the app until exit. There is no persistent transcript history; model usage stores counts only.
- Operational logs exclude dictated content. Model files, runtime archives, settings and logs are stored in `%USERPROFILE%\Applications\LazyType`.
- Model workers are attached to a Windows job so closing/crashing the app also terminates its workers. The app never runs as administrator.

Cleanup can still make mistakes. The original transcript remains available, and any failed or clearly truncated cleanup falls back to it. Check names, numbers and important wording. Password controls are excluded from automatic insertion. Windows can block insertion into elevated applications. Some editors may not expose a distinct focused element, so focus protection is best effort. Dictated text is always copied to the clipboard so it remains available to paste anywhere.

## Build and install

Requirements: Windows 10/11 x64, .NET 8 desktop runtime, .NET 8+ SDK to build, Python 3.10+ for the downloader, and an NVIDIA driver compatible with CUDA 12.4. No Python inference environment or CUDA toolkit installation is required.

```powershell
./scripts/install.ps1
```

The script downloads pinned assets, verifies SHA-256 hashes, publishes to `%USERPROFILE%\Applications\LazyType\app`, adds a Start menu shortcut, and enables startup for the current Windows account. `-SkipModels` reuses installed models; `-NoLaunch` installs without starting. If script execution is restricted, run its commands individually according to your machine's policy. If antivirus blocks a freshly built `LazyType.exe`, run the same build through the signed .NET host with `dotnet LazyType.dll`, or add the build and install folders to its exceptions.

```powershell
dotnet build src/LazyType/LazyType.csproj -c Release
```

To disable startup, clear **Start with Windows**. To remove the application, quit it, disable startup and remove the `Lazy Type` Start menu shortcut and `%USERPROFILE%\Applications\LazyType` directory. That directory contains only this app's assets/settings.

## Verification

Run the deterministic model lifecycle and editing regressions:

```powershell
dotnet run --project tests/LazyType.Tests -c Release
python -m unittest discover -s tests -p "test_*.py"
```

To exercise all installed text models with real speech inference, the five-second handover, an edit in flight, and cancellation (no microphone or insertion):

```powershell
dotnet run --project tests/LazyType.Tests -c Release -- --integration --audio path/to/public-16k-mono.wav
```

These optional integration checks need all three models and a compatible GPU. Use synthetic or public audio. The tests keep model files on disk.

To repeat the memory measurement with the installed models and NVIDIA driver (wait until dictation is idle first):

```powershell
./src/LazyType/bin/Release/net8.0-windows/LazyType.exe --memory-test artifacts/verification/memory-test.json
```

This explicit diagnostic loads both models, exercises speech detection and cleanup with synthetic input, records RAM and whole-device GPU readings, then unloads both workers. It never opens the microphone. The UI tooltip is a dated reference sample; rerunning the diagnostic writes a JSON report without changing that reference.

`Test WAV…` runs a local WAV through transcription and cleanup without inserting into another app. It accepts recordings up to two minutes and converts their audio format as needed.

The headless integration check runs the real CUDA engines against the whisper.cpp JFK sample, checks cleanup and suggestion preservation of numbers and dates, ensures questions and embedded instructions are only edited, checks safe replacement matching, suggestion change marking and single-change application, preference serialization, verifies text-only suggestion loading, checks silent audio, and unloads both models:

```powershell
./src/LazyType/bin/Release/net8.0-windows/LazyType.exe --self-test artifacts/verification/jfk.wav artifacts/verification/models-test.json
```

For reproducible native input testing, first quit the normal instance, then launch with `--test-audio <16kHz-mono-16bit.wav>`. This explicitly labelled test mode uses the fixture when the recording hotkey is toggled and never opens the microphone. All model processing and insertion are real. Use `tests/editor.html` as a local plain/rich-text target. Never enable this switch in the installed startup entry.

Add `--test-session` alongside `--test-audio` to test without closing the installed app. This separate instance uses F8 / Shift+F8, fresh in-memory settings, and disabled startup controls. It does not save preferences, listen on the normal instance's command pipe, or register the pause shortcut. Turn on suggestions in this test window and dictate into the plain and rich-text fields of the local test page; the marks appear automatically. Click one label, then apply the rest. Also check already clean wording (nothing should appear), **Show wording**, **Keep original**, Escape, disabling the toggle, editing the field before applying, and the **Last result** wand. Keep any browser or desktop target limited to disposable sample text.

Verification artifacts are ignored under `artifacts/`. See `THIRD-PARTY.md` for component sources and licenses.
