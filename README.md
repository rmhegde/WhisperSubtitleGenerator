# Whisper Subtitle Generator

**Generate subtitles for any audio or video file — on your own machine, for free.**

Speech recognition runs entirely locally through [whisper.cpp](https://github.com/ggerganov/whisper.cpp)
(via [Whisper.net](https://github.com/sandrohanea/whisper.net)). No API key, no account, no upload,
no per-minute charge, and it keeps working with the network off once a model is cached.

[![build](https://github.com/rmhegde/WhisperSubtitleGenerator/actions/workflows/build.yml/badge.svg)](https://github.com/rmhegde/WhisperSubtitleGenerator/actions/workflows/build.yml)
![license](https://img.shields.io/badge/license-MIT-blue)
![platform](https://img.shields.io/badge/platform-Windows-lightgrey)
![.NET](https://img.shields.io/badge/.NET-8.0-512BD4)

---

## Contents

- [What it does](#what-it-does)
- [Install](#install)
- [Quick start](#quick-start)
- [Choosing a model](#choosing-a-model)
- [Writing subtitles into the video](#writing-subtitles-into-the-video)
- [Batch processing](#batch-processing)
- [Languages](#languages)
- [Where files go](#where-files-go)
- [Troubleshooting](#troubleshooting)
- [Building from source](#building-from-source)
- [How it works](#how-it-works)
- [Contributing](#contributing)
- [Licence](#licence)

---

## What it does

| | |
|---|---|
| **Transcribe** | Any format ffmpeg reads — `.mp4` `.mkv` `.mov` `.webm` `.avi` `.mp3` `.wav` `.m4a` `.flac` `.ogg` … |
| **Output** | `.srt`, `.vtt`, or both |
| **Burn into video** | Permanently rendered into the picture, **or** added as a switchable track |
| **Batch** | Drag in whole folders; per-file status, skip-already-done, cancel any time |
| **Languages** | All **100** Whisper supports, with auto-detect |
| **Translate** | Any language → English subtitles in the same pass |
| **Offline** | After the first model download, nothing leaves your machine |

---

## Install

**Download the setup and run it.** That is the whole install.

> Releases: **https://github.com/rmhegde/WhisperSubtitleGenerator/releases**

The installer is self-contained — **no .NET runtime to install first**, nothing else to set up. It
installs per-user by default so **no administrator prompt appears**; choose an all-users install in
the wizard if you prefer one.

On first launch it checks for **ffmpeg**, which it needs to read audio and video. If it is missing,
the app offers to fetch it:

> ffmpeg is needed to read audio and video files, and it is not installed.
> Download it now? It is about 106 MB, goes into this app's own folder, and needs no
> administrator rights — nothing else on your PC is changed.

Say yes and it downloads, extracts and verifies a current ffmpeg build into
`%LOCALAPPDATA%\WhisperSubtitleGeneratorfmpeg`. Nothing is installed system-wide, no PATH is
touched, and removing it later is deleting that folder.

### Prefer to install ffmpeg yourself?

Say No and use whichever you like:

```powershell
winget install Gyan.FFmpeg
```

The app finds a system install automatically. It also probes the usual install locations directly,
**so you do not need to restart it** — a program that is already running keeps the `PATH` it started
with, which is the usual reason a freshly installed ffmpeg still looks "missing". Press
**Set up ffmpeg** to re-check.

To point at a specific binary, set `WSG_FFMPEG` to its full path; that overrides everything else.

### Uninstalling

Through Windows "Apps & features" as usual. It asks whether to delete the downloaded models and
ffmpeg — say **no** if you plan to reinstall, and they are reused instead of downloaded again.

## Quick start

1. **Drag a video onto the window.** Files *and folders* both work — dropping a folder queues every
   media file inside it.
2. **Pick a model.** Start with **Base**; see [Choosing a model](#choosing-a-model).
3. **Pick a language,** or leave it on **Auto-detect**.
4. **Press Generate subtitles.**

The first run downloads the model you selected — a one-off, cached afterwards. Cues appear in the log
as they are recognised, so you can watch it work rather than stare at a frozen window.

When it finishes you get `yourvideo.srt` next to `yourvideo.mp4`.

---

## Choosing a model

Bigger models are more accurate and much slower. There is no single right answer — it depends on
audio quality, accent, and how much you mind fixing mistakes by hand.

| Model | Size | Use it when |
|---|---|---|
| **Tiny** | ~75 MB | You want a rough draft fast — clear speech, one speaker |
| **Base** | ~142 MB | **Good default.** Clean audio, common accents |
| **Small** | ~466 MB | Background noise, accents, more than one speaker |
| **Medium** | ~1.5 GB | Accuracy matters more than time |
| **Large v3** | ~2.9 GB | Best available. Wants plenty of RAM and patience |

Rough guide: **Tiny** is around real-time on a modern CPU; **Large** can take several times the length
of the recording. Everything runs on CPU — no GPU required.

Models download once from Hugging Face into `%LOCALAPPDATA%\WhisperSubtitleGenerator\models` and are
reused from then on.

---

## Writing subtitles into the video

Tick **Write subtitles into the video** and choose a mode. Both apply to video only — audio files
still get their subtitle files.

### Burn in (permanent)

The text is drawn into the pixels. **Plays everywhere** — phones, TVs, Instagram, WhatsApp,
projectors — and cannot be switched off.

- Requires a full video **re-encode**, so it is slow and costs a little quality
- Audio is copied untouched, so no extra loss there
- Output: `yourvideo-subtitled.mp4`

Use this for social media, anything shared widely, or any player you do not control.

### Add as a track (switchable)

The subtitle file is embedded as a selectable track.

- **Near instant** — nothing is re-encoded, and the file barely changes size
- The viewer can toggle subtitles on and off
- Needs a player that supports soft subtitles: VLC, MPV and Plex do; many phones and social platforms
  ignore them entirely
- Output: `yourvideo-subtitled.mp4` with an extra subtitle stream

Use this for a personal library.

> The original video is never modified, and the app refuses to write over it.

---

## Batch processing

Built for doing a season of episodes in one go.

- **Add files**, **Add folder** (searches subfolders), or **drag and drop** files or folders
- Each row shows its own **live status** — `queued` → `working...` → `done` / `FAILED` — plus the cue
  count and how long it took
- **Skip files that already have subtitles**: re-running a 200-file batch after adding one episode
  transcribes one file, not 201
- **One failure does not stop the queue.** A file with no audio track is marked `FAILED` in red and
  the batch carries on
- **Cancel** stops after the current file
- Press **Generate subtitles** again to re-run the whole queue

Files are processed **one at a time on purpose** — see [How it works](#how-it-works).

---

## Languages

All **100** languages Whisper supports, not a handful. The picker autocompletes: type `kan` and it
jumps to Kannada.

**Auto-detect** works well on clear audio. Set the language explicitly when the audio is noisy, when
it is a language Whisper sees rarely, or when a file opens with music or silence — those are where
detection guesses wrong.

**Translate to English** produces English subtitles from any source language in the same pass. It is
one-directional: Whisper can translate *into* English, but not into any other language.

> Accuracy varies a lot by language. English has by far the most training data; smaller languages
> work noticeably better on the larger models.

---

## Where files go

| What | Where |
|---|---|
| Subtitles | Beside the source file, or the **Folder** you choose |
| Burned video | Beside the source, as `<name>-subtitled.<ext>` |
| Models | `%LOCALAPPDATA%\WhisperSubtitleGenerator\models` |
| Temp audio | System temp, deleted automatically even if the run fails |

Subtitles are written as **UTF-8 without a BOM** — a BOM shows up as stray characters in the first
cue on some hardware players.

---

## Troubleshooting

**ffmpeg is missing**
Press **Set up ffmpeg** and let the app download it — no admin rights needed. If you would rather
install it yourself, `winget install Gyan.FFmpeg` then press **Set up ffmpeg** to re-check; the app
probes real install locations, so a restart is not required.

**The first run sits on "Downloading the model"**
Expected. The model is 75 MB – 2.9 GB depending on your choice, downloaded once. Later runs skip it.

**Transcription is very slow**
Normal for the larger models on CPU. Try Base or Small — each step up in model size is roughly 2–3×
slower.

**The text is wrong, or in the wrong language**
Set the language explicitly instead of Auto-detect, and try a larger model. Noisy audio, heavy accents
and overlapping speakers are where the small models fall apart.

**"produced no audio. It may have no audio track."**
Exactly that — the file has nothing to transcribe.

**Subtitles do not appear after "Add as a track"**
That mode needs a player that supports soft subtitles. Use **Burn in** for anything that has to play
everywhere.

**Burned subtitles are too small or hard to read**
Font size, colour and outline live in `BurnOptions` in the Core library. They are not exposed in the
UI yet — [contributions welcome](#contributing).

---

## Building from source

Needs the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or newer.

```bash
git clone https://github.com/rmhegde/WhisperSubtitleGenerator.git
cd WhisperSubtitleGenerator

dotnet build -c Release        # build everything
dotnet test                    # 68 tests, no ffmpeg or network needed
dotnet run --project src/WhisperSubtitleGenerator.App
```

### Building the installer

```powershell
.\installeruild-installer.ps1
```

Publishes self-contained win-x64, drops the Linux/macOS/x86/arm64 native runtimes that a Windows
x64 build cannot use, and compiles `installer\WhisperSubtitleGenerator.iss` with Inno Setup.
Output lands in `artifacts\installer`. Needs [Inno Setup](https://jrsoftware.org/isinfo.php)
(`winget install JRSoftware.InnoSetup`).

A 162 MB payload compresses to roughly a 49 MB setup.

### Layout

```
src/WhisperSubtitleGenerator.Core/    net8.0 — no UI dependency, reusable
  Audio/AudioExtractor.cs               ffmpeg -> 16 kHz mono PCM
  Audio/FfmpegLocator.cs                finds ffmpeg: override, app-local, PATH, known dirs
  Audio/FfmpegInstaller.cs              one-click download into the app's own folder
  Models/WhisperModelCatalog.cs         model choices, download + cache
  Subtitles/SubtitleWriter.cs           SRT / WebVTT rendering
  Transcription/SubtitleGenerator.cs    one file, end to end
  Transcription/BatchProcessor.cs       the queue, per-file state
  Transcription/WhisperLanguages.cs     all 100 languages
  Video/SubtitleBurner.cs               burn-in and soft-mux via ffmpeg
src/WhisperSubtitleGenerator.App/     net8.0-windows — WinForms UI
tests/WhisperSubtitleGenerator.Tests/ 68 tests
```

The core targets plain `net8.0`, **not** `net8.0-windows`, so a CLI or a cross-platform front end can
reuse the whole pipeline without dragging in WinForms.

---

## How it works

```
media file → ffmpeg → 16 kHz mono WAV → whisper.cpp → cues → .srt / .vtt → (optional) ffmpeg burn
```

Three decisions are worth knowing before changing anything.

**Whisper's input format is not negotiable.** It requires **16 kHz, mono, 16-bit signed PCM**. Give it
44.1 kHz or stereo and it does *not* raise an error — it returns confident, completely wrong text,
which is far harder to diagnose than a crash. Those constants are hardcoded in `AudioExtractor` and
should stay that way.

**SRT and WebVTT differ in ways that fail silently.** SRT puts a **comma** before the milliseconds,
WebVTT a **period**; SRT numbers every cue, VTT does not; VTT must open with `WEBVTT`. Hand a player
the wrong separator and it usually shows *no subtitles at all* rather than complaining.
`SubtitleWriter` renders each format explicitly instead of string-replacing one into the other.

**Batches run one file at a time, deliberately.** Whisper already saturates the CPU on a single file,
so running several at once makes the whole batch *slower* while multiplying peak memory — the large
model alone wants several GB. Sequential also keeps the log readable and cancellation predictable.

One more if you touch the burner: **ffmpeg's `subtitles=` filter needs its path escaped.** Inside a
filtergraph `:` separates options, so a plain `C:\Videos\ep1.srt` is parsed as filter `C` and fails
with an unhelpful "Unable to open". It has to become `C\:/Videos/ep1.srt`.
`SubtitleBurner.EscapeForFilter` handles it, with tests pinning it — this is the single most common
reason subtitle burning fails on Windows.

---

## Contributing

Issues and pull requests welcome. Good first things to pick up:

- [ ] Expose subtitle font size, colour and position in the UI (already in `BurnOptions`)
- [ ] Remember the last model, language and output folder between runs
- [ ] A CLI front end over the same core (the core is already UI-free)
- [ ] Word-level timestamps and karaoke-style highlighting
- [ ] An editor to fix cues before writing
- [ ] Speaker diarization
- [ ] GPU acceleration (Whisper.net ships CUDA and Vulkan runtimes)

Please keep `dotnet test` green — the tests cover subtitle formatting, batch rules and ffmpeg argument
construction, which is where the fiddly bugs live.

---

## Supporting this project

It is free and MIT licensed, and stays that way. If it saves you time and you want to chip in,
sponsorship links are in the repo sidebar once they are set up — see
[`.github/FUNDING.yml`](.github/FUNDING.yml).

Contributions of code, bug reports and documentation are just as welcome, and often more useful.

---

## Licence

**MIT** — see [LICENSE](LICENSE).

| Component | Licence |
|---|---|
| Whisper models | MIT (OpenAI) |
| whisper.cpp / Whisper.net | MIT |
| ffmpeg | LGPL/GPL depending on build — **not** redistributed; invoked as a program you install |
