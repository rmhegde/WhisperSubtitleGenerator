using WhisperSubtitleGenerator.Core.Subtitles;
using Xunit;

namespace WhisperSubtitleGenerator.Tests;

public class TimestampTests
{
    /// <summary>
    /// The single most consequential difference between the formats. SRT uses a comma before the
    /// milliseconds and WebVTT uses a period; a player handed the wrong one usually shows no
    /// subtitles at all rather than reporting an error, so this is worth pinning explicitly.
    /// </summary>
    [Fact]
    public void Srt_uses_a_comma_and_Vtt_uses_a_period()
    {
        var t = new TimeSpan(0, 1, 2, 3, 456);

        Assert.Equal("01:02:03,456", SubtitleWriter.FormatTimestamp(t, SubtitleFormat.Srt));
        Assert.Equal("01:02:03.456", SubtitleWriter.FormatTimestamp(t, SubtitleFormat.Vtt));
    }

    [Fact]
    public void Zero_is_fully_padded()
    {
        Assert.Equal("00:00:00,000", SubtitleWriter.FormatTimestamp(TimeSpan.Zero, SubtitleFormat.Srt));
    }

    [Fact]
    public void Milliseconds_are_three_digits_not_truncated()
    {
        // 7 ms must render as 007, not 7 - a two-digit field silently shifts every later cue.
        var t = TimeSpan.FromMilliseconds(7);
        Assert.Equal("00:00:00,007", SubtitleWriter.FormatTimestamp(t, SubtitleFormat.Srt));
    }

    /// <summary>
    /// TimeSpan.Hours wraps at 24 because it reports the hours COMPONENT of a duration, so a
    /// 25-hour offset would render as 01 and place the cue a day early. Total hours must be used.
    /// </summary>
    [Fact]
    public void Hours_past_a_day_do_not_wrap()
    {
        var t = new TimeSpan(1, 1, 30, 0);   // 1 day 1:30 == 25 hours 30 min
        Assert.Equal("25:30:00,000", SubtitleWriter.FormatTimestamp(t, SubtitleFormat.Srt));
    }

    [Fact]
    public void Negative_time_is_clamped_to_zero_not_rendered_with_a_minus()
    {
        var t = TimeSpan.FromSeconds(-5);
        Assert.Equal("00:00:00,000", SubtitleWriter.FormatTimestamp(t, SubtitleFormat.Srt));
    }
}

public class WriteTests
{
    private static readonly SubtitleSegment[] Two =
    {
        new(TimeSpan.Zero,               TimeSpan.FromSeconds(2), "Hello world"),
        new(TimeSpan.FromSeconds(2),     TimeSpan.FromSeconds(4), "Second line"),
    };

    [Fact]
    public void Srt_numbers_cues_from_one_and_has_no_header()
    {
        var srt = SubtitleWriter.Write(Two, SubtitleFormat.Srt);
        var lines = srt.Split('\n');

        Assert.Equal("1", lines[0]);
        Assert.Equal("00:00:00,000 --> 00:00:02,000", lines[1]);
        Assert.Equal("Hello world", lines[2]);
        Assert.Equal("", lines[3]);
        Assert.Equal("2", lines[4]);
        Assert.DoesNotContain("WEBVTT", srt);
    }

    [Fact]
    public void Vtt_starts_with_the_required_header_and_omits_cue_numbers()
    {
        var vtt = SubtitleWriter.Write(Two, SubtitleFormat.Vtt);

        Assert.StartsWith("WEBVTT\n\n", vtt);
        // A bare "1" line would be read as a cue identifier - harmless but not what we emit.
        Assert.DoesNotContain("\n1\n", vtt);
        Assert.Contains("00:00:02.000 --> 00:00:04.000", vtt);
    }

    /// <summary>
    /// Whisper regularly emits blank segments at silence boundaries. Written out, they become a
    /// numbered cue with no text, which some players flash as an empty box.
    /// </summary>
    [Fact]
    public void Empty_segments_are_dropped_and_do_not_consume_a_number()
    {
        var withBlanks = new SubtitleSegment[]
        {
            new(TimeSpan.Zero,           TimeSpan.FromSeconds(1), "first"),
            new(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), "   "),
            new(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3), "second"),
        };

        var srt = SubtitleWriter.Write(withBlanks, SubtitleFormat.Srt);

        Assert.Contains("1\n00:00:00,000 --> 00:00:01,000\nfirst", srt);
        // "second" must be cue 2, not cue 3 - a gap in the numbering breaks strict parsers.
        Assert.Contains("2\n00:00:02,000 --> 00:00:03,000\nsecond", srt);
        Assert.Equal(2, srt.Split("-->").Length - 1);
    }

    [Fact]
    public void Empty_input_produces_an_empty_srt_but_a_still_valid_vtt()
    {
        Assert.Equal("", SubtitleWriter.Write(Array.Empty<SubtitleSegment>(), SubtitleFormat.Srt));
        // A VTT with no cues is still a valid file, and must keep its header.
        Assert.Equal("WEBVTT\n\n", SubtitleWriter.Write(Array.Empty<SubtitleSegment>(), SubtitleFormat.Vtt));
    }

    [Fact]
    public void Windows_line_endings_inside_a_cue_are_normalised()
    {
        var seg = new[] { new SubtitleSegment(TimeSpan.Zero, TimeSpan.FromSeconds(1), "line one\r\nline two") };
        var srt = SubtitleWriter.Write(seg, SubtitleFormat.Srt);

        Assert.DoesNotContain("\r", srt);
        Assert.Contains("line one\nline two", srt);
    }

    [Fact]
    public async Task WriteFileAsync_writes_utf8_without_a_bom()
    {
        var path = Path.Combine(Path.GetTempPath(), $"wsg_test_{Guid.NewGuid():N}.srt");
        try
        {
            var seg = new[] { new SubtitleSegment(TimeSpan.Zero, TimeSpan.FromSeconds(1), "café £ ünïcode") };
            await SubtitleWriter.WriteFileAsync(seg, path, SubtitleFormat.Srt);

            var bytes = await File.ReadAllBytesAsync(path);
            // EF BB BF would be displayed as stray glyphs in the first cue by some players.
            Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF,
                         "file should not start with a UTF-8 BOM");
            Assert.Contains("café £ ünïcode", await File.ReadAllTextAsync(path));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Extension_matches_the_format()
    {
        Assert.Equal(".srt", SubtitleWriter.ExtensionFor(SubtitleFormat.Srt));
        Assert.Equal(".vtt", SubtitleWriter.ExtensionFor(SubtitleFormat.Vtt));
    }
}

public class SegmentTests
{
    [Fact]
    public void Duration_of_a_reversed_segment_is_zero_rather_than_negative()
    {
        // Whisper occasionally emits these at chunk boundaries; one bad cue must not abort a run.
        var seg = new SubtitleSegment(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(3), "backwards");
        Assert.Equal(TimeSpan.Zero, seg.Duration);
    }

    [Fact]
    public void NormalizedText_trims_the_leading_space_whisper_prepends()
    {
        // Whisper.net returns segments with a leading space almost every time.
        var seg = new SubtitleSegment(TimeSpan.Zero, TimeSpan.FromSeconds(1), " Hello. ");
        Assert.Equal("Hello.", seg.NormalizedText);
    }

    [Theory]
    [InlineData("",     true)]
    [InlineData("   ",  true)]
    [InlineData("\n",   true)]
    [InlineData("text", false)]
    public void IsEmpty_treats_whitespace_as_empty(string text, bool expected)
    {
        Assert.Equal(expected, new SubtitleSegment(TimeSpan.Zero, TimeSpan.FromSeconds(1), text).IsEmpty);
    }
}
