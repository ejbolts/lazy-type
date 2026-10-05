# Lazy Type

Local dictation for Windows with NVIDIA GPUs. Whisper Turbo transcribes your speech, Qwen 3 cleans up grammar and punctuation, and text is inserted into the active application. Processing stays entirely on your PC. Model workers load when needed and release memory when finished.

## Use

1. Press **Ctrl+Alt+Space** (or choose **Ctrl+Shift+Space** or **F8** in the app) to begin speaking.
2. The speech and language models load on demand the first time you speak, using your NVIDIA GPU.
3. When you finish speaking, press the hotkey again (or wait for the two-minute limit).
4. A compact, rounded, translucent indicator follows your mouse pointer and shows recording and microphone level, followed by transcription and cleanup. It stays within the screen's working area and does not take focus. The microphone closes before model processing starts.
5. After transcription and cleanup, the models unload automatically, your text is copied to the clipboard, and it is pasted into the original field if it is still focused. Model files remain on disk for the next recording.

**Ctrl+Alt+Shift+Space** toggles recording without grammar cleanup. **Escape** cancels an active recording or processing operation and unloads the models. **Ctrl+Alt+Shift+P** pauses/resumes dictation. Pause and Quit release any model workers and their GPU allocations. Resume only arms dictation; models load on the next recording. The dictation hotkey also resumes automatically when paused.

Closing the settings window hides it to the tray; it does not quit. Use **Quit** to stop everything. The microphone is never opened merely because the app is running. Recording has a two-minute limit and stops/transcribes automatically at that limit. Choose the microphone, hotkey, cleanup preference and startup preference in the app. Models are always released after each dictation, silent recording, cancellation or error; there is no idle retention period.

Enable **AI suggestions · show a wand after dictation** in the main window to add a small wand beside the field you dictated into and beside **Last result** in Lazy Type. This preference is off by default and saved independently of automatic speech cleanup. Click either wand to request a clearer version of the wording and grammar, then compare the original and suggestion in a preview. Choose **Apply suggestion**, **Copy**, or **Keep original**. An already polished result is shown as unchanged.

Suggestions use the same local Qwen model, loaded only after you click the wand and released immediately afterward. They do not open the microphone or load Whisper. Closing a pending preview, disabling suggestions, pressing Escape, pausing, or quitting cancels the request and releases the model. Editing the last result dismisses its preview. Suggestions support results up to 6,000 characters; failed or incomplete edits leave the current text intact.

The external wand follows the field while it is focused, where the editor exposes its bounds. Replacement is offered only when Windows exposes a unique text range for the dictation and the field's text has not changed since insertion. It replaces that range, preserving surrounding text. Unsupported editors, duplicate text, or edited fields use **Copy** instead; the main-window wand remains available even when an external field cannot be located. Nothing is replaced without clicking the apply button.

For a field in another app, the suggestion is shown in the field itself, similar to a grammar checker. While it is generated, the dictated text pulses with a purple highlight. Then removed words are struck through in red, and added words appear in small green labels just above or below where they go (whichever covers less of your other text), with a green caret marking pure insertions such as a new comma. The pop-up shrinks to the actions plus a change count; **Show wording** expands the full comparison. When nothing needs changing, the marks clear and the pop-up says so.

The marks use the same unique text range as replacement, follow scrolling and window moves, never take focus or block clicks, are hidden wherever another window covers the field, and disappear once the field is edited or the preview closes. If an editor cannot report where the changed words are, or for the **Last result** wand in Lazy Type, the pop-up shows the comparison instead: removed words struck through and added words tinted.

Hover over the memory note or its information icon for a measured usage sample. In the 5 October 2026 RTX 4080 test, both warm model workers used 1.01 GiB of system RAM (summed working sets, excluding the app) and approximately 4.15 GiB of additional GPU memory. Speech detection and cleanup were exercised before five samples were taken. GPU usage returned to roughly its baseline after unloading. GPU memory is estimated from the change in whole-device usage, so other applications can affect it. These are warm samples, not live readings or peak limits; longer dictation can change usage.

For the alternate Ctrl+Shift+Space hotkey, add Alt for raw dictation. For F8, use Shift+F8 for raw dictation. Shortcut conflicts are reported so another combination can be selected.

**App theme** defaults to **System** and follows the Windows app color setting, including changes while Lazy Type is running. Choose **Light** or **Dark** to override it. **Popup theme** independently controls the floating dictation indicator: **Follow app** (default), **Light**, or **Dark**. Both choices are saved. Use **Preview** to see the popup for five seconds without opening the microphone or loading models.

## Models and privacy

- Whisper Large V3 Turbo Q5_0, approximately 574 MB model file.
- Qwen3-4B-Instruct-2507 Q4_K_M, approximately 2.5 GB model file.
- Silero VAD filters silence before transcription.
- Native CUDA engines use the NVIDIA GPU. Qwen has a 4,096-token context and a single processing slot to bound memory use.
- No cloud transcription or cleanup; the only inference connections are loopback connections on this PC. The text server uses a per-session authentication key.
- Recordings are processed in memory. The last original and edited transcript are kept in the app until exit. There is no persistent transcript history.
- Operational logs exclude dictated content. Model files, runtime archives, settings and logs are stored in `%USERPROFILE%\Applications\LazyType`.
- Model workers are attached to a Windows job so closing/crashing the app also terminates its workers. The app never runs as administrator.

Cleanup can still make mistakes. The original transcript remains available, and any failed or clearly truncated cleanup falls back to it. Check names, numbers and important wording. Password controls are excluded from automatic insertion. Windows can block insertion into elevated applications. Some editors may not expose a distinct focused element, so focus protection is best effort. Dictated text is always copied to the clipboard so it remains available to paste anywhere.

## Build and install

Requirements: Windows 10/11 x64, .NET 8 desktop runtime, .NET 8+ SDK to build, Python 3.10+ for the downloader, and an NVIDIA driver compatible with CUDA 12.4. No Python inference environment or CUDA toolkit installation is required.

```powershell
./scripts/install.ps1
```

The script downloads pinned assets, verifies SHA-256 hashes, publishes to `%USERPROFILE%\Applications\LazyType\app`, adds a Start menu shortcut, and enables startup for the current Windows account. `-SkipModels` reuses installed models; `-NoLaunch` installs without starting. If script execution is restricted, run its commands individually according to your machine's policy.

```powershell
dotnet build src/LazyType/LazyType.csproj -c Release
```

To disable startup, clear **Start with Windows**. To remove the application, quit it, disable startup and remove the `Lazy Type` Start menu shortcut and `%USERPROFILE%\Applications\LazyType` directory. That directory contains only this app's assets/settings.

## Verification

To repeat the memory measurement with the installed models and NVIDIA driver (wait until dictation is idle first):

```powershell
./src/LazyType/bin/Release/net8.0-windows/LazyType.exe --memory-test artifacts/verification/memory-test.json
```

This explicit diagnostic loads both models, exercises speech detection and cleanup with synthetic input, records RAM and whole-device GPU readings, then unloads both workers. It never opens the microphone. The UI tooltip is a dated reference sample; rerunning the diagnostic writes a JSON report without changing that reference.

`Test WAV…` runs a local WAV through transcription and cleanup without inserting into another app. It accepts recordings up to two minutes and converts their audio format as needed.

The headless integration check runs the real CUDA engines against the whisper.cpp JFK sample, checks cleanup and suggestion preservation of numbers and dates, ensures questions and embedded instructions are only edited, checks safe replacement matching and preference serialization, verifies text-only suggestion loading, checks silent audio, and unloads both models:

```powershell
./src/LazyType/bin/Release/net8.0-windows/LazyType.exe --self-test artifacts/verification/jfk.wav artifacts/verification/models-test.json
```

For reproducible native input testing, first quit the normal instance, then launch with `--test-audio <16kHz-mono-16bit.wav>`. This explicitly labelled test mode uses the fixture when the recording hotkey is toggled and never opens the microphone. All model processing and insertion are real. Use `tests/editor.html` as a local plain/rich-text target. Never enable this switch in the installed startup entry.

Add `--test-session` alongside `--test-audio` to test without closing the installed app. This separate instance uses F8 / Shift+F8, fresh in-memory settings, and disabled startup controls. It does not save preferences, listen on the normal instance's command pipe, or register the pause shortcut. Turn on suggestions in this test window, dictate into the local test page, click the floating wand, and review/apply or copy the rewrite. Also check the main-window wand, unchanged wording, dismiss/Escape, disabling the toggle, and editing the target before applying. Keep any browser or desktop target limited to disposable sample text.

Verification artifacts are ignored under `artifacts/`. See `THIRD-PARTY.md` for component sources and licenses.
