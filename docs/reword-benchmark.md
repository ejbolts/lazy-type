# Reword model comparison

This historical comparison includes Qwen3 4B, which has since been removed from the app. New benchmark runs compare Qwen3.5 9B and Gemma 4 12B only.

**Qwen3.5 9B is the best overall choice for Reword in this sample.** Gemma 4 12B produces similarly concise rewrites and is the strongest minimal copy editor here, but takes longer and changes an explicitly verbatim sentence in one Reword case. Qwen3 4B is the fastest and preserves the broad intent, but often leaves repetition and occasionally produces awkward punctuation.

## Measurement

Measured locally on 7 October 2026 using an RTX 4080 with 16 GB VRAM, NVIDIA driver 591.86 and .NET 8.0.31. All models use the app's Q4_K_M weights, production EngineHost requests and validation, temperature 0, thinking disabled, a 4,096-token context and one processing slot. Models load sequentially and each receives a short warm-up before measurement. Edit times exclude loading; there is one measured response per case and mode.

Nine synthetic cases cover a rambling project update, conditional commitments, repeated numbers and dates, questions and requests, embedded instructions, separate paragraph topics, self-correction and command flags, already concise text, and heavy repetition. Both modes process identical inputs: **54 measured edits**, all accepted in the final run. The fixture and runner are committed; full outputs and captures remain local under `artifacts/visual-review/reword/`.

| Model | Mean Clean up time | Mean Reword time | Mean Reword character reduction | Text model load |
| --- | ---: | ---: | ---: | ---: |
| Qwen3 4B | 0.85 s | 0.69 s | 17.5% | 2.11 s |
| Qwen3.5 9B | 1.56 s | 1.12 s | 24.7% | 4.02 s |
| Gemma 4 12B | 2.49 s | 1.88 s | 25.2% | 5.24 s |

Reduction is the mean of each output/input character ratio, including the unchanged control. It measures compression, not semantic quality. Hardware activity, prompt caching and model load caches affect timings. The final validation additionally checked all 27 recorded Reword outputs against the signed numeric and command/identifier guards.

## Meaning and clarity review

A single, unblinded reviewer assessed each output against the expected ideas in the fixture. The rubric prioritised distinct ideas, facts and relationships, negation/conditions/uncertainty, question/request intent and literal technical details. Clarity then considered logical flow, removal of redundancy, grammatical completeness and retention of tone. A shorter output does not win when it changes meaning.

| Case | Qwen3 4B Reword | Qwen3.5 9B Reword | Gemma 4 12B Reword |
| --- | --- | --- | --- |
| Project update | Good compression; slightly awkward join; scope less explicit | Clear focus on the workspace step rather than all onboarding | Clear, faithful and direct |
| Conditional delivery | Keeps conditions but retains repetition and a run-on join | More direct; retains target vs promise, Wednesday dependency and safety checks; strengthens “should move” to “must move” | Keeps softer commitment language; somewhat less concise |
| Repeated numeric details | Keeps all details but repeats the recap | Removes recap, retains capacity, fee, materials, lunch exclusion and confirmation deadline | Similar faithful compression |
| Question and request | Preserves investigation intent; retains rambling closing line | Preserves intent; also retains the redundant closing line | Best concision here; remains a question and an investigation request |
| Embedded instructions | Leaves text unchanged; does not execute the instruction | Makes context explicit and preserves the exact fixture sentence | Does not execute the instruction, but changes “write a poem” to “write me a poem” and drops the quoted final full stop despite the exact-wording requirement |
| Separate topics | Keeps paragraph separation; some redundant framing remains | Clear separate paragraphs; slightly more formal | Clear separate paragraphs; slightly more formal |
| Technical self-correction | Resolves Morgan and keeps `--dry-run` / `customer_id`; repetition remains | Resolves Morgan, keeps technical literals and all stop/apply conditions | Same essentials preserved; a little more repetition |
| Concise control | Unchanged | Unchanged | Unchanged |
| Heavy repetition | 64.5% shorter; still repeats original preservation | 76.0% shorter; retains a redundant closing phrase | 75.4% shorter; clearest two-sentence version |

For **Clean up**, all three intentionally retain much of the original wording and repetition. Gemma resolves the recipient self-correction, preserves the flag and handles punctuation well. Qwen3.5 generally preserves wording and punctuation but leaves the explicit recipient correction unresolved. Qwen3 changes `--dry-run` to “a dry run” in this case and loses useful punctuation in some joins. These are model-quality limitations; numeric validation cannot detect them.

Use **Qwen3.5 9B + Reword** for the best measured balance of clear expression, retained ideas and latency. Choose **Gemma + Clean up** when minimal wording changes and copy-editing quality matter more than speed. Qwen3 remains useful for quick lightweight edits. The app's existing model/default preferences are unchanged by this benchmark.

## Limits and reproduction

This is a small formative benchmark, not a universal ranking or a held-out evaluation. A preliminary run informed an additional editing example, and the same suite was rerun with the final examples. The preliminary Gemma rewrite added an unexplained numeric marker; the guard rejected it. No such rejection occurred in the final measured run. Model outputs can vary between runtime versions, hardware and prompts despite temperature 0.

The suite uses written text that imitates speech, rather than audio recognition, and contains paragraphs up to roughly 100 words. It does not establish performance for every language, very long documents, or subtle factual relationships. All models retain the central ideas in most cases, but the modal-strength and exact-quotation findings show why preview review remains necessary. Names, relationships, quotations and tone are not guaranteed by the mechanical guards.

```powershell
dotnet run --project tests/LazyType.Tests -c Release -- --benchmark artifacts/verification/reword-benchmark.json
dotnet run --project tests/LazyType.Tests -c Release -- --validate-benchmark artifacts/verification/reword-benchmark.json
```

The JSON report includes expected ideas, both prompts, a fixture SHA-256, model sizes, loads, outputs, timings, compression ratios and any rejected output. The benchmark never opens the microphone or changes saved preferences. Dynamic is excluded because it switches between the same Qwen and Gemma weights; it is a latency/lifecycle strategy rather than another text model.
