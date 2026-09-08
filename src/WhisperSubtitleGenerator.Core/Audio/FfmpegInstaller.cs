using System.IO.Compression;

namespace WhisperSubtitleGenerator.Core.Audio;

public sealed record InstallProgress(string Stage, long BytesDone, long BytesTotal)
{
    /// <summary>0-100, or -1 when the server did not report a content length.</summary>
    public int Percent => BytesTotal > 0 ? (int)(BytesDone * 100 / BytesTotal) : -1;

    public override string ToString() => Percent >= 0
        ? $"{Stage} {Percent}% ({BytesDone / 1048576} of {BytesTotal / 1048576} MB)"
        : Stage;
}

/// <summary>
/// Downloads a working ffmpeg into the app's own folder so the user does not have to install
/// anything by hand.
///
/// <para>Deliberately NOT a winget or system-wide install. Three reasons:</para>
/// <list type="number">
///   <item>No administrator rights are needed.</item>
///   <item>No PATH problem. A system install updates PATH, but an already-running process keeps
///         its inherited environment - so the user installs ffmpeg, the app still says it is
///         missing, and the fix is an unexplained restart. Writing into a folder we control and
///         invoking it by full path sidesteps that entirely.</item>
///   <item>Nothing on the machine changes, so uninstalling is deleting a folder.</item>
/// </list>
/// </summary>
public sealed class FfmpegInstaller
{
    /// <summary>
    /// gyan.dev's "release essentials" build - the one ffmpeg.org itself links for Windows.
    /// This URL always points at the current release, so it is never pinned to a stale version.
    /// The trade-off is that a fixed checksum is impossible; the installer verifies by RUNNING the
    /// extracted binary instead, which is a stronger check of the thing we actually care about.
    /// </summary>
    public const string DownloadUrl = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip";

    private readonly HttpClient _http;

    public FfmpegInstaller(HttpClient? http = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
    }

    /// <summary>
    /// Downloads and installs ffmpeg, returning the path to the executable.
    /// Safe to call when it is already installed - it returns immediately.
    /// </summary>
    public async Task<string> InstallAsync(
        IProgress<InstallProgress>? progress = null,
        CancellationToken ct = default)
    {
        var existing = FfmpegLocator.Locate();
        if (existing.Found) return existing.Path;

        Directory.CreateDirectory(FfmpegLocator.AppLocalDirectory);
        var zipPath = Path.Combine(FfmpegLocator.AppLocalDirectory, "ffmpeg-download.zip");

        try
        {
            await DownloadAsync(zipPath, progress, ct).ConfigureAwait(false);

            progress?.Report(new InstallProgress("Extracting", 0, 0));
            var exePath = ExtractFfmpeg(zipPath, FfmpegLocator.AppLocalDirectory);

            // Verify by running it. A zip can extract cleanly and still leave something that will
            // not execute - wrong architecture, blocked by policy, truncated mid-entry.
            if (!FfmpegLocator.TryRun(exePath, out var version))
            {
                throw new FfmpegInstallException(
                    "ffmpeg was downloaded and extracted, but will not run. " +
                    "Delete " + FfmpegLocator.AppLocalDirectory + " and try again, " +
                    "or install it manually with: winget install Gyan.FFmpeg");
            }

            progress?.Report(new InstallProgress($"Installed: {version}", 0, 0));
            return exePath;
        }
        finally
        {
            // The zip is ~106 MB and useless once extracted.
            try { if (File.Exists(zipPath)) File.Delete(zipPath); } catch { /* best effort */ }
        }
    }

    private async Task DownloadAsync(string zipPath, IProgress<InstallProgress>? progress, CancellationToken ct)
    {
        try
        {
            using var response = await _http
                .GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var total = response.Content.Headers.ContentLength ?? 0;
            await using var src = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var dst = File.Create(zipPath);

            var buffer = new byte[81920];
            long done = 0;
            int lastPercent = -1;
            int read;

            while ((read = await src.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                done += read;

                // Report only when the whole percent changes; a callback per 80 KB would flood a
                // UI log with a thousand lines for one download.
                var p = total > 0 ? (int)(done * 100 / total) : -1;
                if (p != lastPercent)
                {
                    lastPercent = p;
                    progress?.Report(new InstallProgress("Downloading ffmpeg", done, total));
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new FfmpegInstallException(
                $"Could not download ffmpeg from {DownloadUrl}. Check your connection, or install " +
                "it manually with: winget install Gyan.FFmpeg", ex);
        }
    }

    /// <summary>
    /// Pulls just ffmpeg.exe out of the archive, flattening it to the target directory.
    /// <para>The zip nests everything under a version-stamped folder such as
    /// "ffmpeg-7.1-essentials_build/bin/ffmpeg.exe", and that name changes with every release - so
    /// the entry is found by FILENAME rather than by an assumed path. Only ffmpeg.exe is extracted;
    /// the docs, presets, ffplay and the shared libraries are not needed and would add ~150 MB.</para>
    /// </summary>
    internal static string ExtractFfmpeg(string zipPath, string targetDir)
    {
        using var zip = ZipFile.OpenRead(zipPath);

        var entry = zip.Entries.FirstOrDefault(e =>
            string.Equals(e.Name, "ffmpeg.exe", StringComparison.OrdinalIgnoreCase))
            ?? throw new FfmpegInstallException("The downloaded archive did not contain ffmpeg.exe.");

        var destination = Path.Combine(targetDir, "ffmpeg.exe");
        entry.ExtractToFile(destination, overwrite: true);
        return destination;
    }

    /// <summary>Removes the app-local copy. The user's own system install is left alone.</summary>
    public static void Uninstall()
    {
        try
        {
            if (Directory.Exists(FfmpegLocator.AppLocalDirectory))
                Directory.Delete(FfmpegLocator.AppLocalDirectory, recursive: true);
        }
        catch { /* best effort */ }
    }
}

public sealed class FfmpegInstallException : Exception
{
    public FfmpegInstallException(string message) : base(message) { }
    public FfmpegInstallException(string message, Exception inner) : base(message, inner) { }
}
