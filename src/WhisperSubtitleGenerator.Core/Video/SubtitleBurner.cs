using System.Diagnostics;
using System.Globalization;

namespace WhisperSubtitleGenerator.Core.Video;

public enum BurnMode
{
    /// <summary>
    /// Render the subtitles into the video pixels. Plays anywhere, cannot be turned off, and
    /// requires a full video re-encode - so it is slow and loses a little quality.
    /// </summary>
    HardBurn,

    /// <summary>
    /// Embed the subtitle file as a selectable track. Near-instant because nothing is re-encoded,
    /// and the viewer can toggle it - but the player has to support soft subtitles, and some
    /// hardware players and most social platforms ignore them.
    /// </summary>
    SoftMux
}

public sealed record BurnOptions
{
    public BurnMode Mode { get; init; } = BurnMode.HardBurn;

    /// <summary>Subtitle font size in points. ffmpeg's default is 24 or so.</summary>
    public int FontSize { get; init; } = 24;

    /// <summary>Primary text colour as &amp;HBBGGRR (ASS order - blue, green, red).</summary>
    public string FontColour { get; init; } = "&HFFFFFF";

    /// <summary>Outline thickness. 0 removes the outline, which hurts legibility on light scenes.</summary>
    public int Outline { get; init; } = 2;

    /// <summary>
    /// x264 quality. Lower is better and bigger; 18 is visually lossless, 23 is the ffmpeg default.
    /// Only used for <see cref="BurnMode.HardBurn"/>.
    /// </summary>
    public int Crf { get; init; } = 20;

    /// <summary>Suffix inserted before the extension of the output file.</summary>
    public string OutputSuffix { get; init; } = "-subtitled";
}

/// <summary>
/// Writes subtitles into a video with ffmpeg, either burned into the picture or muxed as a track.
/// </summary>
public sealed class SubtitleBurner
{
    private readonly string _ffmpegPath;

    public SubtitleBurner(string? ffmpegPath = null) => _ffmpegPath = ffmpegPath ?? "ffmpeg";

    /// <summary>
    /// Escapes a Windows path for use inside ffmpeg's <c>subtitles=</c> filter argument.
    ///
    /// <para>This is the single most common reason subtitle burning fails, and the error message
    /// ffmpeg gives is unhelpful. Inside a filtergraph the parser treats <c>:</c> as an option
    /// separator and <c>\</c> as an escape, so a plain Windows path like
    /// <c>C:\Videos\ep1.srt</c> is read as filter "C" with option "\Videos\ep1.srt" and fails with
    /// "Unable to open". The path must become <c>C\:/Videos/ep1.srt</c>: backslashes turned into
    /// forward slashes, the drive colon escaped, and single quotes escaped for the enclosing
    /// quotes.</para>
    /// </summary>
    public static string EscapeForFilter(string path)
    {
        // Forward slashes work on Windows and avoid the escape-character problem entirely.
        var p = path.Replace('\\', '/');
        // The drive-letter colon still has to be escaped, or it reads as an option separator.
        p = p.Replace(":", "\\:");
        // A quote inside the value would terminate the quoted argument early.
        p = p.Replace("'", "\\'");
        return p;
    }

    /// <summary>Default output path: "clip.mp4" + "-subtitled" -> "clip-subtitled.mp4".</summary>
    public static string DefaultOutputPath(string videoPath, string suffix)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(videoPath)) ?? ".";
        var name = Path.GetFileNameWithoutExtension(videoPath);
        var ext = Path.GetExtension(videoPath);
        return Path.Combine(dir, name + suffix + ext);
    }

    /// <summary>Builds the ffmpeg arguments. Exposed so the exact command can be tested and logged.</summary>
    public static string BuildArguments(string videoPath, string subtitlePath, string outputPath, BurnOptions o)
    {
        if (o.Mode == BurnMode.SoftMux)
        {
            // Copy both streams and add the subtitles as a track. mov_text is the subtitle codec
            // MP4 accepts; Matroska takes SRT directly, but mov_text is understood by both.
            var subCodec = Path.GetExtension(outputPath).Equals(".mkv", StringComparison.OrdinalIgnoreCase)
                ? "srt"
                : "mov_text";
            return $"-hide_banner -loglevel error -i \"{videoPath}\" -i \"{subtitlePath}\" " +
                   $"-c copy -c:s {subCodec} -metadata:s:s:0 language=und -y \"{outputPath}\"";
        }

        var style = string.Format(
            CultureInfo.InvariantCulture,
            "FontSize={0},PrimaryColour={1},Outline={2}",
            o.FontSize, o.FontColour, o.Outline);

        // The whole filter value is single-quoted so spaces in the path survive; the path itself is
        // escaped by EscapeForFilter.
        var filter = $"subtitles='{EscapeForFilter(subtitlePath)}':force_style='{style}'";

        // Video must be re-encoded - you cannot -c copy a stream whose pixels are being changed.
        // Audio is copied, which saves time and avoids a needless generation of lossy re-encoding.
        return $"-hide_banner -loglevel error -i \"{videoPath}\" -vf \"{filter}\" " +
               $"-c:v libx264 -crf {o.Crf} -preset medium -c:a copy -y \"{outputPath}\"";
    }

    /// <summary>
    /// Runs ffmpeg and returns the output path.
    /// </summary>
    /// <exception cref="SubtitleBurnException">ffmpeg failed, or the output was not produced.</exception>
    public async Task<string> BurnAsync(
        string videoPath,
        string subtitlePath,
        BurnOptions? options = null,
        string? outputPath = null,
        IProgress<string>? status = null,
        CancellationToken ct = default)
    {
        var o = options ?? new BurnOptions();

        if (!File.Exists(videoPath))
            throw new SubtitleBurnException($"Video not found: {videoPath}");
        if (!File.Exists(subtitlePath))
            throw new SubtitleBurnException($"Subtitle file not found: {subtitlePath}");

        outputPath ??= DefaultOutputPath(videoPath, o.OutputSuffix);

        // Refuse to overwrite the source. ffmpeg reading and writing the same file truncates it,
        // and losing the original video to a subtitle step would be unforgivable.
        if (string.Equals(Path.GetFullPath(outputPath), Path.GetFullPath(videoPath),
                          StringComparison.OrdinalIgnoreCase))
        {
            throw new SubtitleBurnException(
                "The output path is the same as the source video. That would destroy the original.");
        }

        var args = BuildArguments(videoPath, subtitlePath, outputPath, o);
        status?.Report(o.Mode == BurnMode.HardBurn
            ? $"Burning subtitles into {Path.GetFileName(videoPath)} (re-encoding, this takes a while)..."
            : $"Muxing subtitles into {Path.GetFileName(videoPath)} (no re-encode)...");

        var psi = new ProcessStartInfo(_ffmpegPath, args)
        {
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            using var proc = Process.Start(psi)
                ?? throw new SubtitleBurnException("Could not start ffmpeg.");

            // Drain stderr concurrently - a full pipe blocks ffmpeg indefinitely, which looks
            // exactly like a hang on a long encode.
            var stderrTask = proc.StandardError.ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct).ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);

            if (proc.ExitCode != 0)
            {
                TryDelete(outputPath);
                throw new SubtitleBurnException(
                    $"ffmpeg failed (exit {proc.ExitCode}): " +
                    (string.IsNullOrWhiteSpace(stderr) ? "no diagnostics" : stderr.Trim()));
            }

            if (!File.Exists(outputPath) || new FileInfo(outputPath).Length == 0)
            {
                TryDelete(outputPath);
                throw new SubtitleBurnException("ffmpeg reported success but produced no output file.");
            }

            status?.Report($"Wrote {Path.GetFileName(outputPath)}");
            return outputPath;
        }
        catch (OperationCanceledException)
        {
            // A cancelled encode leaves a partial file that would look like a finished video.
            TryDelete(outputPath);
            throw;
        }
        catch (SubtitleBurnException)
        {
            throw;
        }
        catch (Exception ex)
        {
            TryDelete(outputPath);
            throw new SubtitleBurnException($"Could not run ffmpeg ('{_ffmpegPath}').", ex);
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* best effort */ }
    }
}

public sealed class SubtitleBurnException : Exception
{
    public SubtitleBurnException(string message) : base(message) { }
    public SubtitleBurnException(string message, Exception inner) : base(message, inner) { }
}
