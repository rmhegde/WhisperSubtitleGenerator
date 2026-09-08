using System.Diagnostics;

namespace WhisperSubtitleGenerator.Core.Audio;

/// <summary>
/// Converts any media file ffmpeg can read into the exact PCM format Whisper requires.
///
/// <para>Whisper does not accept arbitrary audio. It needs <b>16 kHz, mono, 16-bit signed
/// little-endian PCM</b>. Feeding it 44.1 kHz or stereo does not error - it produces confident,
/// completely wrong transcriptions, which is far harder to diagnose than a crash. Those three
/// parameters are therefore hardcoded below and should not be made configurable.</para>
/// </summary>
public sealed class AudioExtractor
{
    public const int RequiredSampleRate = 16_000;
    public const int RequiredChannels = 1;

    private readonly string _ffmpegPath;

    public AudioExtractor(string? ffmpegPath = null)
    {
        // Resolve through the locator rather than assuming the bare name works. That covers the
        // app-local copy we may have downloaded, and known install locations that PATH has not
        // propagated to this process yet.
        var found = FfmpegLocator.Locate(ffmpegPath);
        _ffmpegPath = found.Found ? found.Path : (ffmpegPath ?? "ffmpeg");
    }

    /// <summary>
    /// True when ffmpeg can actually be launched. Checked up front so the UI can report a missing
    /// dependency before the user picks files and waits, rather than failing mid-run.
    /// </summary>
    public bool IsAvailable(out string? version) => FfmpegLocator.TryRun(_ffmpegPath, out version);

    /// <summary>The ffmpeg this extractor will actually invoke - useful for logging.</summary>
    public string FfmpegPath => _ffmpegPath;

    /// <summary>
    /// Decodes <paramref name="inputPath"/> to a temporary 16 kHz mono WAV and returns its path.
    /// The caller owns the file and should delete it when done.
    /// </summary>
    /// <exception cref="AudioExtractionException">ffmpeg is missing, or could not decode the input.</exception>
    public async Task<string> ExtractWavAsync(string inputPath, CancellationToken ct = default)
    {
        if (!File.Exists(inputPath))
            throw new AudioExtractionException($"Input file not found: {inputPath}");

        var wavPath = Path.Combine(
            Path.GetTempPath(),
            $"wsg_{Guid.NewGuid():N}.wav");

        // -vn            drop any video stream
        // -ac 1          mono
        // -ar 16000      16 kHz
        // -c:a pcm_s16le 16-bit signed little-endian PCM
        // -y             overwrite (the GUID makes a collision essentially impossible, but be explicit)
        var args = $"-hide_banner -loglevel error -i \"{inputPath}\" -vn -ac {RequiredChannels} " +
                   $"-ar {RequiredSampleRate} -c:a pcm_s16le -y \"{wavPath}\"";

        var psi = new ProcessStartInfo(_ffmpegPath, args)
        {
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            using var proc = Process.Start(psi)
                ?? throw new AudioExtractionException("Could not start ffmpeg.");

            // ffmpeg writes diagnostics to stderr. Read it concurrently: a full stderr pipe blocks
            // ffmpeg forever, which would look to the user like a hung conversion.
            var stderrTask = proc.StandardError.ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct).ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);

            if (proc.ExitCode != 0)
            {
                TryDelete(wavPath);
                throw new AudioExtractionException(
                    $"ffmpeg failed (exit {proc.ExitCode}) on '{Path.GetFileName(inputPath)}': " +
                    (string.IsNullOrWhiteSpace(stderr) ? "no diagnostics" : stderr.Trim()));
            }

            if (!File.Exists(wavPath) || new FileInfo(wavPath).Length == 0)
            {
                TryDelete(wavPath);
                throw new AudioExtractionException(
                    $"'{Path.GetFileName(inputPath)}' produced no audio. It may have no audio track.");
            }

            return wavPath;
        }
        catch (OperationCanceledException)
        {
            TryDelete(wavPath);
            throw;
        }
        catch (AudioExtractionException)
        {
            throw;
        }
        catch (Exception ex)
        {
            TryDelete(wavPath);
            throw new AudioExtractionException(
                $"Could not run ffmpeg ('{_ffmpegPath}'). Is it installed and on PATH?", ex);
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* best effort */ }
    }
}

public sealed class AudioExtractionException : Exception
{
    public AudioExtractionException(string message) : base(message) { }
    public AudioExtractionException(string message, Exception inner) : base(message, inner) { }
}
