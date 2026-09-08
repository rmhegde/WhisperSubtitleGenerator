namespace WhisperSubtitleGenerator.Core.Subtitles;

/// <summary>
/// One subtitle cue: a span of time and the text shown during it.
/// </summary>
public sealed record SubtitleSegment(TimeSpan Start, TimeSpan End, string Text)
{
    /// <summary>
    /// How long the cue is on screen. Never negative - a segment whose end precedes its start is
    /// treated as zero-length rather than throwing, because Whisper occasionally emits them at
    /// chunk boundaries and one bad cue should not abort a two-hour transcription.
    /// </summary>
    public TimeSpan Duration => End > Start ? End - Start : TimeSpan.Zero;

    /// <summary>Text with surrounding whitespace removed and internal line breaks normalised to \n.</summary>
    public string NormalizedText =>
        Text.Replace("\r\n", "\n").Replace('\r', '\n').Trim();

    public bool IsEmpty => string.IsNullOrWhiteSpace(Text);
}
