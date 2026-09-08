using System.Diagnostics;

namespace WhisperSubtitleGenerator.Core.Audio;

/// <summary>Where an ffmpeg executable was found.</summary>
public enum FfmpegSource
{
    NotFound,

    /// <summary>An explicit path supplied by the caller or the WSG_FFMPEG environment variable.</summary>
    Explicit,

    /// <summary>The copy this app downloaded into its own folder.</summary>
    AppLocal,

    /// <summary>Found on PATH - a system-wide install the user made themselves.</summary>
    Path,

    /// <summary>Found at a known install location even though PATH was not updated.</summary>
    KnownLocation
}

public sealed record FfmpegInfo(string Path, FfmpegSource Source, string? Version)
{
    public bool Found => Source != FfmpegSource.NotFound;
    public static FfmpegInfo Missing { get; } = new("", FfmpegSource.NotFound, null);
}

/// <summary>
/// Finds a usable ffmpeg, in a deliberate order of preference.
///
/// <para>Checking known install locations after PATH matters more than it looks. winget installs
/// ffmpeg under LocalAppData and updates the user PATH, but a process that was ALREADY RUNNING
/// keeps its inherited environment - so a user who installs ffmpeg while the app is open and then
/// wonders why it is still "not found" is hitting Windows behaviour, not a bug. Probing the real
/// directories means the app finds it anyway, without a restart.</para>
/// </summary>
public static class FfmpegLocator
{
    /// <summary>Set this to point at a specific binary; overrides everything else.</summary>
    public const string OverrideVariable = "WSG_FFMPEG";

    /// <summary>
    /// "ffmpeg.exe" on Windows, "ffmpeg" elsewhere. The Core library targets plain net8.0 so it
    /// can back a cross-platform front end; hardcoding the .exe suffix would make it Windows-only
    /// for no reason.
    /// </summary>
    private static string ExeName => OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";

    /// <summary>Where <see cref="FfmpegInstaller"/> puts a downloaded copy.</summary>
    public static string AppLocalDirectory => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WhisperSubtitleGenerator", "ffmpeg");

    public static string AppLocalExePath => System.IO.Path.Combine(AppLocalDirectory, ExeName);

    /// <summary>
    /// Resolves ffmpeg. Every candidate is actually EXECUTED before being accepted - a file
    /// existing at the right path proves nothing if it is a truncated download or the wrong
    /// architecture.
    /// </summary>
    public static FfmpegInfo Locate(string? explicitPath = null)
    {
        var fromEnv = Environment.GetEnvironmentVariable(OverrideVariable);

        foreach (var (candidate, source) in Candidates(explicitPath, fromEnv))
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            if (TryRun(candidate, out var version))
            {
                return new FfmpegInfo(candidate, source, version);
            }
        }

        return FfmpegInfo.Missing;
    }

    private static IEnumerable<(string Path, FfmpegSource Source)> Candidates(string? explicitPath, string? fromEnv)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath)) yield return (explicitPath!, FfmpegSource.Explicit);
        if (!string.IsNullOrWhiteSpace(fromEnv))      yield return (fromEnv!,      FfmpegSource.Explicit);

        // Prefer our own copy: it is a known-good build we downloaded, and it cannot be shadowed by
        // whatever else happens to be on PATH.
        if (File.Exists(AppLocalExePath)) yield return (AppLocalExePath, FfmpegSource.AppLocal);

        // Bare name: let the OS resolve it through PATH.
        yield return (ExeName, FfmpegSource.Path);

        foreach (var known in KnownLocations())
        {
            if (File.Exists(known)) yield return (known, FfmpegSource.KnownLocation);
        }
    }

    /// <summary>
    /// Places a system-wide install lands, checked directly in case PATH has not propagated into
    /// this process.
    /// </summary>
    private static IEnumerable<string> KnownLocations()
    {
        if (!OperatingSystem.IsWindows())
        {
            // On Linux and macOS a package manager put ffmpeg on PATH, which the previous
            // candidate already covers. These are the two spots a manual install still lands.
            yield return "/usr/local/bin/ffmpeg";
            yield return "/opt/homebrew/bin/ffmpeg";
            yield break;
        }

        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var wingetPackages = System.IO.Path.Combine(local, "Microsoft", "WinGet", "Packages");

        // winget's folder carries the package id and the build's own version, neither of which is
        // predictable - so glob rather than guess.
        if (Directory.Exists(wingetPackages))
        {
            IEnumerable<string> hits;
            try
            {
                hits = Directory.EnumerateFiles(wingetPackages, ExeName, SearchOption.AllDirectories);
            }
            catch
            {
                hits = Array.Empty<string>();
            }
            foreach (var h in hits) yield return h;
        }

        foreach (var root in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                     @"C:\ffmpeg",
                     System.IO.Path.Combine(local, "Programs")
                 })
        {
            if (string.IsNullOrWhiteSpace(root)) continue;
            yield return System.IO.Path.Combine(root, "ffmpeg", "bin", ExeName);
            yield return System.IO.Path.Combine(root, "ffmpeg", ExeName);
        }
    }

    /// <summary>Runs "ffmpeg -version" and returns the first line, or false if it will not run.</summary>
    internal static bool TryRun(string path, out string? version)
    {
        version = null;
        try
        {
            using var p = Process.Start(new ProcessStartInfo(path, "-version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (p is null) return false;

            var first = p.StandardOutput.ReadLine();
            if (!p.WaitForExit(15_000))
            {
                try { p.Kill(entireProcessTree: true); } catch { /* best effort */ }
                return false;
            }
            if (p.ExitCode != 0) return false;

            version = first;
            return true;
        }
        catch
        {
            // Missing file, wrong architecture, blocked by policy - all mean "cannot use this one".
            return false;
        }
    }
}
