using WhisperSubtitleGenerator.Cli;
using WhisperSubtitleGenerator.Core.Subtitles;
using WhisperSubtitleGenerator.Core.Video;
using Whisper.net.Ggml;
using Xunit;

namespace WhisperSubtitleGenerator.Tests;

public class CliParsingTests
{
    [Fact]
    public void No_arguments_shows_help_rather_than_erroring()
    {
        var o = CliOptions.Parse(Array.Empty<string>());
        Assert.True(o.ShowHelp);
        Assert.Null(o.Error);
    }

    [Fact]
    public void Defaults_are_base_model_auto_language_and_srt()
    {
        var o = CliOptions.Parse(new[] { "clip.mp4" });
        Assert.Null(o.Error);
        Assert.Equal(GgmlType.Base, o.Model);
        Assert.Equal("auto", o.Language);
        Assert.Equal(new[] { SubtitleFormat.Srt }, o.Formats);
        Assert.Null(o.Burn);
    }

    [Theory]
    [InlineData("tiny", GgmlType.Tiny)]
    [InlineData("TINY", GgmlType.Tiny)]
    [InlineData("largev3", GgmlType.LargeV3)]
    [InlineData("Large v3", GgmlType.LargeV3)]   // the display name, spaces and all
    public void Model_accepts_the_type_name_or_the_display_name(string arg, GgmlType expected)
        => Assert.Equal(expected, CliOptions.Parse(new[] { "-m", arg, "c.mp4" }).Model);

    [Fact]
    public void An_unknown_model_is_an_error_that_points_at_the_listing()
    {
        var o = CliOptions.Parse(new[] { "-m", "enormous", "c.mp4" });
        Assert.Contains("Unknown model", o.Error);
        Assert.Contains("--list-models", o.Error);
    }

    [Fact]
    public void An_unknown_language_is_rejected_before_anything_runs()
    {
        // Catching it here beats letting Whisper silently ignore it and transcribe as English.
        Assert.Contains("Unknown language", CliOptions.Parse(new[] { "-l", "zz", "c.mp4" }).Error);
    }

    [Fact]
    public void Format_accepts_a_list_and_the_both_shorthand()
    {
        Assert.Equal(new[] { SubtitleFormat.Srt, SubtitleFormat.Vtt },
                     CliOptions.Parse(new[] { "-f", "srt,vtt", "c.mp4" }).Formats);
        Assert.Equal(new[] { SubtitleFormat.Srt, SubtitleFormat.Vtt },
                     CliOptions.Parse(new[] { "-f", "both", "c.mp4" }).Formats);
    }

    [Fact]
    public void Duplicate_formats_collapse_so_a_file_is_not_written_twice()
    {
        // "-f srt,both" is an easy way to ask for srt twice.
        Assert.Equal(new[] { SubtitleFormat.Srt, SubtitleFormat.Vtt },
                     CliOptions.Parse(new[] { "-f", "srt,both", "c.mp4" }).Formats);
    }

    [Fact]
    public void A_leading_dot_on_a_format_is_tolerated()
        => Assert.Equal(new[] { SubtitleFormat.Vtt }, CliOptions.Parse(new[] { "-f", ".vtt", "c.mp4" }).Formats);

    [Fact]
    public void Burn_without_a_mode_means_hard_burn()
    {
        // The common request is "put the subtitles in the video", which means burned in.
        Assert.Equal(BurnMode.HardBurn, CliOptions.Parse(new[] { "--burn", "c.mp4" }).Burn);
    }

    [Theory]
    [InlineData("hard", BurnMode.HardBurn)]
    [InlineData("soft", BurnMode.SoftMux)]
    public void Burn_accepts_an_explicit_mode(string arg, BurnMode expected)
        => Assert.Equal(expected, CliOptions.Parse(new[] { "--burn", arg, "c.mp4" }).Burn);

    /// <summary>
    /// "--burn clip.mp4" must treat clip.mp4 as the INPUT, not swallow it as a mode. Getting this
    /// wrong would silently drop the only file the user asked for and then report nothing to do.
    /// </summary>
    [Fact]
    public void Burn_does_not_swallow_the_following_filename()
    {
        var o = CliOptions.Parse(new[] { "--burn", "clip.mp4" });
        Assert.Equal(BurnMode.HardBurn, o.Burn);
        Assert.Equal(new[] { "clip.mp4" }, o.Inputs);
        Assert.Null(o.Error);
    }

    [Fact]
    public void A_flag_missing_its_value_is_reported_rather_than_consuming_the_next_flag()
    {
        // "-m -q clip.mp4" must not set the model to "-q".
        Assert.Contains("needs a value", CliOptions.Parse(new[] { "-m", "-q", "clip.mp4" }).Error);
    }

    [Fact]
    public void An_unknown_option_is_an_error_not_treated_as_a_filename()
    {
        // Otherwise a typo like "--quite" becomes an input path and fails much later with a
        // confusing "file not found".
        Assert.Contains("Unknown option", CliOptions.Parse(new[] { "--quite", "c.mp4" }).Error);
    }

    [Fact]
    public void Options_may_appear_before_or_after_the_inputs()
    {
        var a = CliOptions.Parse(new[] { "-m", "tiny", "c.mp4" });
        var b = CliOptions.Parse(new[] { "c.mp4", "-m", "tiny" });
        Assert.Equal(a.Model, b.Model);
        Assert.Equal(a.Inputs, b.Inputs);
    }

    [Fact]
    public void Missing_inputs_is_an_error()
        => Assert.Contains("No input files", CliOptions.Parse(new[] { "-m", "tiny" }).Error);

    [Fact]
    public void Switches_combine()
    {
        var o = CliOptions.Parse(new[] { "-t", "-q", "-r", "--skip-existing", "c.mp4" });
        Assert.True(o.Translate);
        Assert.True(o.Quiet);
        Assert.True(o.Recursive);
        Assert.True(o.SkipExisting);
    }
}

public class CliInputResolutionTests
{
    [Fact]
    public void A_directory_contributes_the_media_inside_it()
    {
        using var t = new TempDir();
        t.CreateFile("a.mp4");
        t.CreateFile("b.mp3");
        t.CreateFile("notes.txt");        // not media - must be ignored

        var o = CliOptions.Parse(new[] { t.Path });
        var files = o.ResolveInputs();

        Assert.Equal(2, files.Count);
        Assert.DoesNotContain(files, f => f.EndsWith(".txt"));
    }

    [Fact]
    public void Subfolders_are_only_searched_with_recursive()
    {
        using var t = new TempDir();
        t.CreateFile("top.mp4");
        Directory.CreateDirectory(Path.Combine(t.Path, "season2"));
        File.WriteAllText(Path.Combine(t.Path, "season2", "deep.mkv"), "x");

        Assert.Single(CliOptions.Parse(new[] { t.Path }).ResolveInputs());
        Assert.Equal(2, CliOptions.Parse(new[] { t.Path, "-r" }).ResolveInputs().Count);
    }

    /// <summary>
    /// A path that does not exist is passed through rather than filtered out, so the batch reports
    /// a clear per-file failure. Dropping it silently would leave the user wondering why their file
    /// was ignored.
    /// </summary>
    [Fact]
    public void A_nonexistent_file_is_kept_so_it_can_fail_visibly()
    {
        var files = CliOptions.Parse(new[] { "no-such-file.mp4" }).ResolveInputs();
        Assert.Single(files);
    }

    [Fact]
    public void The_same_file_listed_twice_is_only_processed_once()
    {
        using var t = new TempDir();
        var f = t.CreateFile("a.mp4");
        Assert.Single(CliOptions.Parse(new[] { f, f }).ResolveInputs());
    }
}
