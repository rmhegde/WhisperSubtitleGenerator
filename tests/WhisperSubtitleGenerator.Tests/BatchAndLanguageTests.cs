using WhisperSubtitleGenerator.Core.Subtitles;
using WhisperSubtitleGenerator.Core.Transcription;
using Xunit;

namespace WhisperSubtitleGenerator.Tests;

public class LanguageCatalogTests
{
    [Fact]
    public void Offers_every_language_whisper_supports()
    {
        // 100, not 99: the original Whisper release shipped 99 languages, and Cantonese ("yue")
        // was added with large-v3. Shipping a hand-picked subset silently denies users the rest,
        // so the count is pinned - if a future model adds a language, this test should fail and
        // prompt the catalog to be updated rather than quietly drifting behind.
        Assert.Equal(100, WhisperLanguages.Count);
        Assert.NotNull(WhisperLanguages.Find("yue"));
    }

    [Fact]
    public void Auto_detect_is_first_and_the_rest_are_alphabetical_by_name()
    {
        var all = WhisperLanguages.All;

        Assert.True(all[0].IsAuto);
        var names = all.Skip(1).Select(l => l.Name).ToList();
        Assert.Equal(names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase), names);
    }

    [Fact]
    public void Codes_are_unique()
    {
        var codes = WhisperLanguages.All.Select(l => l.Code).ToList();
        Assert.Equal(codes.Count, codes.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    /// <summary>
    /// Two codes are deliberately not ISO-639-1 because they are what whisper.cpp itself expects.
    /// "Correcting" them to ISO codes would make those languages fail to select.
    /// </summary>
    [Theory]
    [InlineData("haw", "Hawaiian")]
    [InlineData("yue", "Cantonese")]
    public void Whisper_specific_three_letter_codes_are_preserved(string code, string name)
    {
        var lang = WhisperLanguages.Find(code);
        Assert.NotNull(lang);
        Assert.Equal(name, lang!.Name);
    }

    [Theory]
    [InlineData("en", "English")]
    [InlineData("kn", "Kannada")]
    [InlineData("EN", "English")]      // lookup is case-insensitive
    public void Find_resolves_a_code_to_its_language(string code, string expected)
        => Assert.Equal(expected, WhisperLanguages.Find(code)!.Name);

    [Fact]
    public void Find_returns_null_for_an_unknown_code()
        => Assert.Null(WhisperLanguages.Find("zz"));

    [Fact]
    public void NameFor_falls_back_to_the_raw_code_rather_than_throwing()
        => Assert.Equal("zz", WhisperLanguages.NameFor("zz"));

    [Fact]
    public void Display_text_shows_the_code_so_two_similar_names_stay_distinguishable()
    {
        Assert.Equal("English (en)", WhisperLanguages.Find("en")!.ToString());
        Assert.Equal("Auto-detect", WhisperLanguages.Auto.ToString());
    }

    [Fact]
    public void Common_shortcuts_all_resolve_to_real_languages()
    {
        Assert.NotEmpty(WhisperLanguages.Common);
        Assert.All(WhisperLanguages.Common, l => Assert.NotNull(WhisperLanguages.Find(l.Code)));
    }
}

public class BatchSkipTests
{
    private static TranscriptionOptions Opts(string dir, params SubtitleFormat[] formats) =>
        new() { Formats = formats, OutputDirectory = dir };

    [Fact]
    public void Not_skipped_when_no_output_exists()
    {
        using var t = new TempDir();
        var input = t.CreateFile("clip.mp4");
        Assert.False(BatchProcessor.AllOutputsExist(input, Opts(t.Path, SubtitleFormat.Srt)));
    }

    [Fact]
    public void Skipped_when_the_single_requested_format_exists()
    {
        using var t = new TempDir();
        var input = t.CreateFile("clip.mp4");
        t.CreateFile("clip.srt");
        Assert.True(BatchProcessor.AllOutputsExist(input, Opts(t.Path, SubtitleFormat.Srt)));
    }

    /// <summary>
    /// The important case: asking for both formats when only one exists must NOT skip, or the
    /// missing format would never be produced.
    /// </summary>
    [Fact]
    public void Not_skipped_when_only_some_requested_formats_exist()
    {
        using var t = new TempDir();
        var input = t.CreateFile("clip.mp4");
        t.CreateFile("clip.srt");
        Assert.False(BatchProcessor.AllOutputsExist(input, Opts(t.Path, SubtitleFormat.Srt, SubtitleFormat.Vtt)));

        t.CreateFile("clip.vtt");
        Assert.True(BatchProcessor.AllOutputsExist(input, Opts(t.Path, SubtitleFormat.Srt, SubtitleFormat.Vtt)));
    }

    [Fact]
    public void Never_skips_when_no_format_was_requested()
    {
        using var t = new TempDir();
        var input = t.CreateFile("clip.mp4");
        // All() is true for an empty set, so without the explicit count check this would report
        // "already done" and silently skip every file in the batch.
        Assert.False(BatchProcessor.AllOutputsExist(input, Opts(t.Path)));
    }

    [Fact]
    public async Task A_failing_file_does_not_stop_the_batch()
    {
        using var t = new TempDir();
        var items = new[]
        {
            new BatchItem(Path.Combine(t.Path, "missing-one.mp4")),
            new BatchItem(Path.Combine(t.Path, "missing-two.mp4")),
        };

        // Neither file exists, so both fail in audio extraction - the point is that the second is
        // still attempted after the first throws.
        var summary = await new BatchProcessor().RunAsync(items, Opts(t.Path, SubtitleFormat.Srt));

        Assert.Equal(2, summary.Total);
        Assert.Equal(2, summary.Failed);
        Assert.All(items, i => Assert.Equal(BatchItemState.Failed, i.State));
        Assert.All(items, i => Assert.False(string.IsNullOrWhiteSpace(i.Message)));
    }

    [Fact]
    public async Task Items_start_pending_and_report_every_state_change()
    {
        using var t = new TempDir();
        var item = new BatchItem(Path.Combine(t.Path, "nope.mp4"));
        Assert.Equal(BatchItemState.Pending, item.State);

        var states = new List<BatchItemState>();
        await new BatchProcessor().RunAsync(
            new[] { item }, Opts(t.Path, SubtitleFormat.Srt),
            onItemChanged: i => states.Add(i.State));

        // Running must be reported before the terminal state, or the UI cannot show "in progress".
        Assert.Equal(new[] { BatchItemState.Running, BatchItemState.Failed }, states);
    }

    [Fact]
    public async Task Cancelling_before_the_run_marks_pending_items_cancelled_not_pending()
    {
        using var t = new TempDir();
        var items = new[] { new BatchItem(Path.Combine(t.Path, "a.mp4")) };
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var summary = await new BatchProcessor().RunAsync(
            items, Opts(t.Path, SubtitleFormat.Srt), ct: cts.Token);

        Assert.Equal(BatchItemState.Cancelled, items[0].State);
        Assert.Equal(0, summary.Done);
    }
}

/// <summary>A temp directory that cleans itself up.</summary>
internal sealed class TempDir : IDisposable
{
    public string Path { get; }

    public TempDir()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "wsg_t_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string CreateFile(string name)
    {
        var p = System.IO.Path.Combine(Path, name);
        File.WriteAllText(p, "x");
        return p;
    }

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); } catch { /* best effort */ }
    }
}
