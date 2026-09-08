using System.IO.Compression;
using WhisperSubtitleGenerator.Core.Audio;
using Xunit;

namespace WhisperSubtitleGenerator.Tests;

public class FfmpegLocatorTests
{
    [Fact]
    public void An_explicit_path_that_does_not_exist_is_rejected_rather_than_trusted()
    {
        // A path existing - or being configured - proves nothing. Every candidate must actually
        // run, or a truncated download would be accepted and fail later mid-transcription.
        Assert.False(FfmpegLocator.TryRun(Path.Combine(Path.GetTempPath(), "definitely-not-ffmpeg.exe"), out _));
    }

    [Fact]
    public void A_file_that_exists_but_is_not_an_executable_is_rejected()
    {
        using var t = new TempDir();
        var fake = t.CreateFile("ffmpeg.exe");   // a text file wearing the right name
        Assert.False(FfmpegLocator.TryRun(fake, out _));
    }

    [Fact]
    public void The_app_local_directory_is_under_LocalAppData_not_Program_Files()
    {
        // Deliberate: writing under LocalAppData needs no administrator rights, which is what lets
        // the one-click setup work for a normal user.
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        Assert.StartsWith(local, FfmpegLocator.AppLocalDirectory);
        Assert.EndsWith("ffmpeg.exe", FfmpegLocator.AppLocalExePath);
    }

    [Fact]
    public void Missing_reports_not_found_and_carries_no_path()
    {
        Assert.False(FfmpegInfo.Missing.Found);
        Assert.Equal(FfmpegSource.NotFound, FfmpegInfo.Missing.Source);
    }

    /// <summary>
    /// Not an assertion about this machine - it documents that Locate never throws, whatever the
    /// environment looks like. A locator that threw on a machine without ffmpeg would crash the
    /// app at startup, which is precisely the case it exists to handle gracefully.
    /// </summary>
    [Fact]
    public void Locate_never_throws()
    {
        var ex = Record.Exception(() => FfmpegLocator.Locate());
        Assert.Null(ex);
    }
}

public class FfmpegInstallerTests
{
    [Fact]
    public void Extract_reports_a_clear_error_when_the_archive_has_no_ffmpeg()
    {
        using var t = new TempDir();
        var zipPath = Path.Combine(t.Path, "empty.zip");
        using (var z = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            z.CreateEntry("readme.txt");
        }

        var ex = Assert.Throws<FfmpegInstallException>(() => FfmpegInstaller.ExtractFfmpeg(zipPath, t.Path));
        Assert.Contains("did not contain ffmpeg.exe", ex.Message);
    }

    /// <summary>
    /// The archive nests everything under a version-stamped folder such as
    /// "ffmpeg-7.1-essentials_build/bin/ffmpeg.exe", and that name changes with every release.
    /// Extraction must therefore match on the FILENAME, not an assumed path - otherwise the
    /// installer silently breaks the next time upstream cuts a release.
    /// </summary>
    [Fact]
    public void Extract_finds_ffmpeg_however_deeply_it_is_nested_and_flattens_it()
    {
        using var t = new TempDir();
        var zipPath = Path.Combine(t.Path, "nested.zip");
        using (var z = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            z.CreateEntry("ffmpeg-99.9-essentials_build/doc/readme.txt");
            var e = z.CreateEntry("ffmpeg-99.9-essentials_build/bin/ffmpeg.exe");
            using var w = new StreamWriter(e.Open());
            w.Write("not a real binary, but in the right place");
        }

        var extracted = FfmpegInstaller.ExtractFfmpeg(zipPath, t.Path);

        Assert.Equal(Path.Combine(t.Path, "ffmpeg.exe"), extracted);
        Assert.True(File.Exists(extracted));
    }

    [Fact]
    public void Extract_overwrites_a_previous_copy_rather_than_failing()
    {
        using var t = new TempDir();
        t.CreateFile("ffmpeg.exe");                       // a stale copy already sitting there
        var zipPath = Path.Combine(t.Path, "z.zip");
        using (var z = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var e = z.CreateEntry("build/bin/ffmpeg.exe");
            using var w = new StreamWriter(e.Open());
            w.Write("newer");
        }

        var extracted = FfmpegInstaller.ExtractFfmpeg(zipPath, t.Path);
        Assert.Equal("newer", File.ReadAllText(extracted));
    }

    [Fact]
    public void The_download_url_points_at_the_current_release_not_a_pinned_version()
    {
        // A pinned version would rot. The trade-off is that a fixed checksum is impossible, which
        // is why the installer verifies by RUNNING the extracted binary instead.
        Assert.Contains("ffmpeg-release-essentials.zip", FfmpegInstaller.DownloadUrl);
        Assert.StartsWith("https://", FfmpegInstaller.DownloadUrl);
    }

    [Fact]
    public async Task Install_returns_immediately_when_ffmpeg_is_already_available()
    {
        // Guarded so it only asserts on a machine that HAS ffmpeg; elsewhere it would try a 106 MB
        // download, which is not something a unit test should ever do.
        var existing = FfmpegLocator.Locate();
        if (!existing.Found) return;

        // Awaited rather than blocked on: .GetAwaiter().GetResult() in a test can deadlock, and
        // xUnit's analyzer flags it (xUnit1031).
        var path = await new FfmpegInstaller().InstallAsync();
        Assert.Equal(existing.Path, path);
    }
}

public class InstallProgressTests
{
    [Fact]
    public void Percent_is_computed_from_the_byte_counts()
        => Assert.Equal(50, new InstallProgress("Downloading", 50, 100).Percent);

    [Fact]
    public void Percent_is_minus_one_when_the_server_gave_no_content_length()
    {
        // Not 0 - that would render as a progress bar stuck at the start rather than as "unknown".
        Assert.Equal(-1, new InstallProgress("Downloading", 500, 0).Percent);
    }

    [Fact]
    public void Text_reports_megabytes_because_bytes_are_unreadable_at_this_size()
        => Assert.Contains("MB", new InstallProgress("Downloading", 52_428_800, 104_857_600).ToString());

    [Fact]
    public void Text_omits_the_numbers_entirely_when_the_total_is_unknown()
        => Assert.Equal("Extracting", new InstallProgress("Extracting", 0, 0).ToString());
}
