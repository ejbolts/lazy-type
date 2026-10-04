# Lazy Type

A Windows tray application for fully local voice dictation, using GPU-accelerated Whisper Large V3 Turbo and Qwen3-4B-Instruct-2507. Intended for ordinary browser inputs, rich-text editors and desktop apps.

## Use

1. Open **Lazy Type** from the Start menu. It also starts hidden when you sign in to Windows. Startup runs only the lightweight app and hotkeys; no speech or cleanup models are loaded.
2. Click the text field where you want to dictate.
3. Press **Ctrl+Alt+Space** once, speak, then press it again to stop. The models load in the background while you speak.
4. A floating indicator shows recording and microphone level, followed by transcription and cleanup. It does not take focus. The microphone closes before model processing starts.
5. After transcription and cleanup, the models unload automatically and your text is pasted into the original field if it is still focused. Otherwise, open Lazy Type and copy the result. Model files remain on disk for the next recording.

**Ctrl+Alt+Shift+Space** toggles recording without grammar cleanup. **Escape** cancels an active recording or processing operation and unloads the models. **Ctrl+Alt+Shift+P** pauses/resumes dictation. Pause and Quit release any model workers and their GPU allocations. Resume only arms dictation; models load on the next recording. The dictation hotkey also resumes automatically when paused.

Closing the settings window hides it to the tray; it does not quit. Use **Quit** to stop everything. The microphone is never opened merely because the app is running. Recording has a two-minute limit and stops/transcribes automatically at that limit. Choose the microphone, hotkey, cleanup preference and startup preference in the app. Models are always released after each dictation, silent recording, cancellation or error; there is no idle retention period.

On the installed RTX 4080 PC, the warm app and model workers used approximately 1.1 GB of system RAM (working set) and about 4 GB of additional GPU memory. Pause stopped both workers and left roughly 100 MB for the app. Loading took about three seconds; an 11-second sample took about 0.4 seconds to transcribe. These are measured samples, not guaranteed limits; longer dictation and other applications can change usage and latency.

For the alternate Ctrl+Shift+Space hotkey, add Alt for raw dictation. For F8, use Shift+F8 for raw dictation. Shortcut conflicts are reported so another combination can be selected.

## Models and privacy

- Whisper Large V3 Turbo Q5_0, approximately 574 MB model file.
- Qwen3-4B-Instruct-2507 Q4_K_M, approximately 2.5 GB model file.
- Silero VAD filters silence before transcription.
- Native CUDA engines use the NVIDIA GPU. Qwen has a 4,096-token context and a single processing slot to bound memory use.
- No cloud transcription or cleanup; the only inference connections are loopback connections on this PC. The text server uses a per-session authentication key.
- Recordings are processed in memory. The last original and edited transcript are kept in the app until exit. There is no persistent transcript history.
- Operational logs exclude dictated content. Model files, runtime archives, settings and logs are stored in `%USERPROFILE%\Applications\LazyType`.
- Model workers are attached to a Windows job so closing/crashing the app also terminates its workers. The app never runs as administrator.

Cleanup can still make mistakes. The original transcript remains available, and any failed or clearly truncated cleanup falls back to it. Check names, numbers and important wording. Password controls are excluded from automatic insertion. Windows can block insertion into elevated applications. Some editors may not expose a distinct focused element, so focus protection is best effort. Clipboard restoration waits for paste handling and does not overwrite newly copied user content.

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

`Test WAV…` runs a local WAV through transcription and cleanup without inserting into another app. It accepts recordings up to two minutes and converts their audio format as needed.

The headless integration check runs the real CUDA engines against the whisper.cpp JFK sample, checks correction/numeric preservation, ensures dictated questions are not answered, checks silent audio, and unloads both models:

```powershell
./src/LazyType/bin/Release/net8.0-windows/LazyType.exe --self-test artifacts/verification/jfk.wav artifacts/verification/models-test.json
```

For reproducible native input testing, first quit the normal instance, then launch with `--test-audio <16kHz-mono-16bit.wav>`. This explicitly labelled test mode uses the fixture when the recording hotkey is toggled and never opens the microphone. All model processing and insertion are real. Use `tests/editor.html` as a local plain/rich-text target. Never enable this switch in the installed startup entry.

Verification artifacts are ignored under `artifacts/`. See `THIRD-PARTY.md` for component sources and licenses.
