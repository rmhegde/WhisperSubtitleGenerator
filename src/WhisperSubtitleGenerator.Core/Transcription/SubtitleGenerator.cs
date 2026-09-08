using Whisper.net;
using Whisper.net.Ggml;
using WhisperSubtitleGenerator.Core.Audio;
using WhisperSubtitleGenerator.Core.Models;
using WhisperSubtitleGenerator.Core.Subtitles;

namespace WhisperSubtitleGenerator.Core.Transcription;

public sealed record TranscriptionOptions
{
    public GgmlType Model { get; init; } = WhisperModelCatalog.Default.Type;

    /// <summary>ISO code such as "en", or "auto" to let Whisper detect it.</summary>
    public string Language { get; init; } = "auto";

    /// <summary>Translate to English instead of transcribing in the source language.</summary>
    public bool TranslateToEnglish { get; init; }

    public IReadOnlyList<SubtitleFormat> Formats { get; init; } = new[] { SubtitleFormat.Srt };

    /// <summary>Where to write. Null means beside the source file.</summary>
    public string? OutputDirectory { get; init; }
}

public sealed record TranscriptionResult(
    string InputPath,
    IReadOnlyList<string> OutputPaths,
    IReadOnlyList<SubtitleSegment> Segments,
    TimeSpan Elapsed);

/// <summary>
/// Ties the pieces together: media file -> 16 kHz WAV -> Whisper -> subtitle files.
/// </summary>
public sealed class SubtitleGenerator
{
    private readonly AudioExtractor _audio;

    public SubtitleGenerator(AudioExtractor? audio = null) => _audio = audio ?? new AudioExtractor();

    /// <summary>
    /// Transcribes one file and writes a subtitle file per requested format.
    /// </summary>
    /// <param name="onSegment">
    /// Called as each cue is recognised, so a caller can show text arriving live. Whisper emits
    /// segments progressively, and on a long file the first cue can take a while - showing them as
    /// they land is the difference between "working" and "frozen" to a user.
    /// </param>
    public async Task<TranscriptionResult> GenerateAsync(
        string inputPath,
        TranscriptionOptions options,
        IProgress<string>? status = null,
        Action<SubtitleSegment>? onSegment = null,
        CancellationToken ct = default)
    {
        var startedAt = DateTime.UtcNow;
        var modelPath = await WhisperModelCatalog
            .EnsureDownloadedAsync(options.Model, status, ct).ConfigureAwait(false);

        status?.Report($"Extracting audio from {Path.GetFileName(inputPath)}...");
        var wavPath = await _audio.ExtractWavAsync(inputPath, ct).ConfigureAwait(false);

        var segments = new List<SubtitleSegment>();
        try
        {
            status?.Report("Loading model...");
            using var factory = WhisperFactory.FromPath(modelPath);

            var builder = factory.CreateBuilder();
            builder = options.TranslateToEnglish
                ? builder.WithLanguage(options.Language).WithTranslate()
                : builder.WithLanguage(options.Language);

            using var processor = builder.Build();

            status?.Report("Transcribing...");
            await using var wav = File.OpenRead(wavPath);

            await foreach (var seg in processor.ProcessAsync(wav, ct).ConfigureAwait(false))
            {
                var s = new SubtitleSegment(seg.Start, seg.End, seg.Text);
                segments.Add(s);
                onSegment?.Invoke(s);
            }
        }
        finally
        {
            // The temp WAV can be large - a two-hour film is around 200 MB at 16 kHz mono. Always
            // clean it up, including when transcription was cancelled or threw.
            try { if (File.Exists(wavPath)) File.Delete(wavPath); } catch { /* best effort */ }
        }

        var outDir = options.OutputDirectory
                     ?? Path.GetDirectoryName(Path.GetFullPath(inputPath))
                     ?? Directory.GetCurrentDirectory();
        Directory.CreateDirectory(outDir);

        var baseName = Path.GetFileNameWithoutExtension(inputPath);
        var written = new List<string>();

        foreach (var format in options.Formats)
        {
            var outPath = Path.Combine(outDir, baseName + SubtitleWriter.ExtensionFor(format));
            await SubtitleWriter.WriteFileAsync(segments, outPath, format, ct).ConfigureAwait(false);
            written.Add(outPath);
            status?.Report($"Wrote {Path.GetFileName(outPath)}");
        }

        return new TranscriptionResult(inputPath, written, segments, DateTime.UtcNow - startedAt);
    }
}
