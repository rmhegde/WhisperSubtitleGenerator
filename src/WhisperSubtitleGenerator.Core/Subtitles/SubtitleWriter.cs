using System.Globalization;
using System.Text;

namespace WhisperSubtitleGenerator.Core.Subtitles;

public enum SubtitleFormat
{
    /// <summary>SubRip. Timestamps use a COMMA before the milliseconds.</summary>
    Srt,

    /// <summary>WebVTT. Timestamps use a PERIOD, and the file starts with a WEBVTT header.</summary>
    Vtt
}

/// <summary>
/// Renders segments as SRT or WebVTT.
///
/// <para>The two formats differ in three ways that are easy to get wrong, so they are handled
/// explicitly rather than by string-replacing one into the other:</para>
/// <list type="number">
///   <item>SRT separates seconds from milliseconds with a comma, WebVTT with a period. A player
///         given the wrong separator typically shows no subtitles at all rather than erroring.</item>
///   <item>SRT numbers every cue starting at 1; WebVTT cue identifiers are optional and omitted here.</item>
///   <item>WebVTT requires the literal "WEBVTT" on the first line.</item>
/// </list>
/// </summary>
public static class SubtitleWriter
{
    /// <summary>
    /// Formats a timestamp for the given format.
    /// <para>Hours are NOT clamped to two digits. A cue past 100 hours keeps all its digits rather
    /// than silently wrapping to 00 - a wrong timestamp is worse than an unusual one, and both
    /// formats tolerate the extra digit.</para>
    /// </summary>
    public static string FormatTimestamp(TimeSpan t, SubtitleFormat format)
    {
        // Negative timestamps are not representable in either format; clamp rather than emit "-00:..".
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;

        char msSeparator = format == SubtitleFormat.Srt ? ',' : '.';
        long hours = (long)t.TotalHours;

        return string.Format(
            CultureInfo.InvariantCulture,
            "{0:00}:{1:00}:{2:00}{3}{4:000}",
            hours, t.Minutes, t.Seconds, msSeparator, t.Milliseconds);
    }

    /// <summary>
    /// Renders the segments. Empty or whitespace-only cues are dropped: they would otherwise
    /// produce a numbered entry with no text, which some players render as a blank flash.
    /// </summary>
    public static string Write(IEnumerable<SubtitleSegment> segments, SubtitleFormat format)
    {
        var sb = new StringBuilder();

        if (format == SubtitleFormat.Vtt)
        {
            sb.Append("WEBVTT\n\n");
        }

        int index = 1;
        foreach (var seg in segments)
        {
            if (seg.IsEmpty) continue;

            if (format == SubtitleFormat.Srt)
            {
                sb.Append(index.ToString(CultureInfo.InvariantCulture)).Append('\n');
            }

            sb.Append(FormatTimestamp(seg.Start, format))
              .Append(" --> ")
              .Append(FormatTimestamp(seg.End, format))
              .Append('\n')
              .Append(seg.NormalizedText)
              .Append("\n\n");

            index++;
        }

        return sb.ToString();
    }

    /// <summary>
    /// Writes to disk as UTF-8. A BOM is deliberately NOT emitted: some hardware players and older
    /// versions of VLC display the BOM as stray characters in the first cue.
    /// </summary>
    public static async Task WriteFileAsync(
        IEnumerable<SubtitleSegment> segments,
        string path,
        SubtitleFormat format,
        CancellationToken ct = default)
    {
        var text = Write(segments, format);
        var utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        await File.WriteAllTextAsync(path, text, utf8NoBom, ct).ConfigureAwait(false);
    }

    public static string ExtensionFor(SubtitleFormat format) =>
        format == SubtitleFormat.Srt ? ".srt" : ".vtt";
}
