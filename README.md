# Whisper Subtitle Generator

Generate `.srt` and `.vtt` subtitles from any audio or video file, locally, on Windows.

Speech recognition runs **entirely on your machine** via [whisper.cpp](https://github.com/ggerganov/whisper.cpp)
(through [Whisper.net](https://github.com/sandrohanea/whisper.net)). No API key, no upload, no per-minute
cost, and it works offline once a model is cached.

![status](https://img.shields.io/badge/status-working-brightgreen) ![license](https://img.shields.io/badge/license-MIT-blue)

## What it does

- **Batch queue** — drag and drop files *or whole folders* onto the window, add a folder
  recursively, or pick files individually. Each row shows its own live status (queued → working →
  done / FAILED), the cue count and how long it took.
- **All 100 languages** Whisper supports, not a hand-picked handful. The picker autocompletes, so
  typing `kan` jumps straight to Kannada.
- **Translate to English** in the same pass instead of transcribing in the source language
- **Skip files that already have subtitles**, so re-running a 200-file batch after adding one new
  episode does not redo 200 transcriptions
- Writes `.srt`, `.vtt`, or both, beside the source file or into a folder you choose
- Five model sizes, from ~75 MB (fast, rough) to ~2.9 GB (slow, best)
- Cues appear in the log as they are recognised, so a long file shows progress rather than freezing
- Cancel mid-run; a file that fails is marked and the batch carries on
- Press **Generate subtitles** again to re-run the whole queue

## Requirements

- **Windows** with the [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)
- **ffmpeg** on `PATH` — used to decode media into the exact PCM format Whisper needs:

  ```
  winget install Gyan.FFmpeg
  ```

  The app checks for it at startup and tells you if it is missing rather than failing later.

Models are **not bundled**. The one you select is downloaded on first use from Hugging Face and
cached under `%LOCALAPPDATA%\WhisperSubtitleGenerator\models`, so later runs are instant and offline.

## Build and run

```bash
git clone https://github.com/ram-wiziiot/WhisperSubtitleGenerator.git
cd WhisperSubtitleGenerator
dotnet build -c Release
dotnet run --project src/WhisperSubtitleGenerator.App
```

Run the tests with:

```bash
dotnet test
```

## Layout

```
src/WhisperSubtitleGenerator.Core/   net8.0    — no UI dependency, reusable
  Audio/AudioExtractor.cs                        ffmpeg -> 16 kHz mono PCM
  Models/WhisperModelCatalog.cs                  model choices, download + cache
  Subtitles/SubtitleWriter.cs                    SRT / WebVTT rendering
  Transcription/SubtitleGenerator.cs             ties it together
src/WhisperSubtitleGenerator.App/    net8.0-windows — WinForms UI
  Transcription/BatchProcessor.cs                sequential queue, per-file state
  Transcription/WhisperLanguages.cs              all 100 languages
src/WhisperSubtitleGenerator.App/    net8.0-windows — WinForms UI
tests/WhisperSubtitleGenerator.Tests/            37 tests over the formatting and batch logic
```

The core is deliberately UI-free and targets plain `net8.0`, so a CLI or a cross-platform front end
can be built on it without touching the transcription code.

## Two things worth knowing if you extend this

**Whisper's input format is not negotiable.** It requires 16 kHz, mono, 16-bit signed PCM. Feeding it
44.1 kHz or stereo does not raise an error — it returns confident, wrong text, which is much harder to
diagnose than a crash. Those constants live in `AudioExtractor` and should stay hardcoded.

**Batches run one file at a time, on purpose.** Whisper already saturates the CPU on a single
file, so running several at once makes the whole batch *slower* while multiplying peak memory — the
large model alone wants several GB. Sequential also keeps the log readable and cancellation
predictable.

**SRT and VTT differ in ways that fail silently.** SRT puts a **comma** before the milliseconds,
WebVTT a **period**; SRT numbers every cue, VTT does not; VTT must open with `WEBVTT`. Give a player
the wrong separator and it typically shows *no subtitles at all* rather than reporting a problem.
`SubtitleWriter` handles each format explicitly instead of string-replacing one into the other, and
the tests pin all three differences.

## Status

Working end to end and verified: synthesized speech at 22 kHz was resampled, transcribed with the
Tiny model, and written to both formats with correct timestamps and no UTF-8 BOM (a BOM shows up as
stray characters in the first cue on some players).

Ideas, in rough order of usefulness:

- [ ] Remember the last model, language and output folder between runs
- [ ] Burn-in / hard-sub via ffmpeg
- [ ] Word-level timestamps and karaoke-style highlighting
- [ ] A CLI front end over the same core
- [ ] Speaker diarization

## Licence

MIT — see [LICENSE](LICENSE).

Whisper models are released by OpenAI under MIT. `whisper.cpp` and Whisper.net are MIT. ffmpeg is
LGPL/GPL depending on build and is **not** distributed with this app — it is invoked as an external
program you install yourself.
