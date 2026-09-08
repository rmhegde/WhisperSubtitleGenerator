using WhisperSubtitleGenerator.Core.Video;
using Xunit;

namespace WhisperSubtitleGenerator.Tests;

public class FilterEscapingTests
{
    /// <summary>
    /// The single most common reason subtitle burning fails on Windows. Inside a filtergraph ffmpeg
    /// treats ':' as an option separator, so an unescaped "C:\Videos\ep1.srt" is parsed as filter
    /// "C" with a stray option and fails with an unhelpful "Unable to open" message.
    /// </summary>
    [Fact]
    public void Drive_letter_colon_is_escaped()
    {
        var escaped = SubtitleBurner.EscapeForFilter(@"C:\Videos\ep1.srt");
        Assert.Equal(@"C\:/Videos/ep1.srt", escaped);
    }

    [Fact]
    public void Backslashes_become_forward_slashes()
    {
        // Forward slashes work on Windows and sidestep backslash-as-escape entirely.
        var escaped = SubtitleBurner.EscapeForFilter(@"D:\a\b\c\file.srt");
        Assert.DoesNotContain(@"\\", escaped);
        Assert.Contains("/a/b/c/file.srt", escaped);
    }

    [Fact]
    public void Single_quotes_are_escaped_so_they_cannot_end_the_quoted_argument()
    {
        var escaped = SubtitleBurner.EscapeForFilter(@"C:\Bob's Videos\ep.srt");
        Assert.Contains(@"Bob\'s", escaped);
    }

    [Fact]
    public void Spaces_are_left_alone_because_the_value_is_quoted_by_the_caller()
    {
        var escaped = SubtitleBurner.EscapeForFilter(@"C:\My Videos\ep 1.srt");
        Assert.Contains("My Videos/ep 1.srt", escaped);
    }

    [Fact]
    public void A_unix_style_path_survives_unchanged_apart_from_nothing_to_escape()
    {
        Assert.Equal("/home/me/subs/ep.srt", SubtitleBurner.EscapeForFilter("/home/me/subs/ep.srt"));
    }
}

public class BurnArgumentTests
{
    private const string Video = @"C:\v\clip.mp4";
    private const string Subs  = @"C:\v\clip.srt";

    [Fact]
    public void Hard_burn_re_encodes_video_and_copies_audio()
    {
        var args = SubtitleBurner.BuildArguments(Video, Subs, @"C:\v\out.mp4", new BurnOptions());

        Assert.Contains("-c:v libx264", args);
        // Audio is copied: re-encoding it would waste time and add a lossy generation for nothing.
        Assert.Contains("-c:a copy", args);
        // Video cannot be stream-copied when its pixels are being changed.
        Assert.DoesNotContain("-c:v copy", args);
        Assert.Contains("subtitles=", args);
    }

    [Fact]
    public void Hard_burn_embeds_the_escaped_path_not_the_raw_one()
    {
        var args = SubtitleBurner.BuildArguments(Video, Subs, @"C:\v\out.mp4", new BurnOptions());

        Assert.Contains(@"subtitles='C\:/v/clip.srt'", args);
        Assert.DoesNotContain(@"subtitles='C:\v\clip.srt'", args);
    }

    [Fact]
    public void Style_options_reach_force_style()
    {
        var args = SubtitleBurner.BuildArguments(Video, Subs, @"C:\v\out.mp4",
            new BurnOptions { FontSize = 32, FontColour = "&H00FFFF", Outline = 3 });

        Assert.Contains("FontSize=32", args);
        Assert.Contains("PrimaryColour=&H00FFFF", args);
        Assert.Contains("Outline=3", args);
    }

    [Fact]
    public void Crf_is_honoured()
        => Assert.Contains("-crf 18",
               SubtitleBurner.BuildArguments(Video, Subs, @"C:\v\out.mp4", new BurnOptions { Crf = 18 }));

    /// <summary>
    /// Soft mux exists precisely to avoid a re-encode; if it ever grows a libx264 flag the whole
    /// point of the mode is gone.
    /// </summary>
    [Fact]
    public void Soft_mux_copies_everything_and_never_re_encodes()
    {
        var args = SubtitleBurner.BuildArguments(Video, Subs, @"C:\v\out.mp4",
            new BurnOptions { Mode = BurnMode.SoftMux });

        Assert.Contains("-c copy", args);
        Assert.DoesNotContain("libx264", args);
        Assert.DoesNotContain("subtitles=", args);
    }

    [Theory]
    [InlineData(@"C:\v\out.mp4", "mov_text")]   // MP4 needs mov_text
    [InlineData(@"C:\v\out.mkv", "srt")]        // Matroska takes SRT directly
    public void Soft_mux_picks_the_subtitle_codec_the_container_accepts(string output, string expected)
    {
        var args = SubtitleBurner.BuildArguments(Video, Subs, output,
            new BurnOptions { Mode = BurnMode.SoftMux });
        Assert.Contains($"-c:s {expected}", args);
    }
}

public class BurnSafetyTests
{
    [Fact]
    public void Default_output_inserts_the_suffix_before_the_extension()
    {
        var outPath = SubtitleBurner.DefaultOutputPath(@"C:\v\clip.mp4", "-subtitled");
        Assert.Equal("clip-subtitled.mp4", Path.GetFileName(outPath));
    }

    [Fact]
    public void Default_output_keeps_the_original_container()
        => Assert.EndsWith(".mkv", SubtitleBurner.DefaultOutputPath(@"C:\v\clip.mkv", "-x"));

    /// <summary>
    /// ffmpeg reading and writing the same path truncates it. Losing the source video to a
    /// subtitle step would be unrecoverable, so this is refused before ffmpeg is ever launched.
    /// </summary>
    [Fact]
    public async Task Refuses_to_write_over_the_source_video()
    {
        using var t = new TempDir();
        var video = t.CreateFile("clip.mp4");
        var subs = t.CreateFile("clip.srt");

        var ex = await Assert.ThrowsAsync<SubtitleBurnException>(() =>
            new SubtitleBurner().BurnAsync(video, subs, outputPath: video));

        Assert.Contains("destroy the original", ex.Message);
    }

    [Fact]
    public async Task Reports_a_missing_video_clearly()
    {
        using var t = new TempDir();
        var subs = t.CreateFile("clip.srt");
        var ex = await Assert.ThrowsAsync<SubtitleBurnException>(() =>
            new SubtitleBurner().BurnAsync(Path.Combine(t.Path, "nope.mp4"), subs));
        Assert.Contains("Video not found", ex.Message);
    }

    [Fact]
    public async Task Reports_a_missing_subtitle_file_clearly()
    {
        using var t = new TempDir();
        var video = t.CreateFile("clip.mp4");
        var ex = await Assert.ThrowsAsync<SubtitleBurnException>(() =>
            new SubtitleBurner().BurnAsync(video, Path.Combine(t.Path, "nope.srt")));
        Assert.Contains("Subtitle file not found", ex.Message);
    }
}
