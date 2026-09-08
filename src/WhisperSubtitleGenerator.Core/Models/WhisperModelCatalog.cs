using Whisper.net.Ggml;

namespace WhisperSubtitleGenerator.Core.Models;

/// <summary>A Whisper model the user can pick, with the trade-off spelled out.</summary>
public sealed record ModelChoice(
    GgmlType Type,
    string DisplayName,
    string ApproxSize,
    string Notes)
{
    public override string ToString() => $"{DisplayName}  ({ApproxSize}) - {Notes}";
}

/// <summary>
/// The models offered, and where they are cached on disk.
///
/// <para>Models are NOT shipped with the app - the smallest is 75 MB and the largest is around
/// 3 GB, and licensing them for redistribution is a question we do not need to answer. They are
/// downloaded on first use from Hugging Face and cached under LocalAppData, so a second run of the
/// same model is instant and works offline.</para>
/// </summary>
public static class WhisperModelCatalog
{
    public static IReadOnlyList<ModelChoice> All { get; } = new[]
    {
        new ModelChoice(GgmlType.Tiny,   "Tiny",   "~75 MB",  "fastest, roughly transcribes; fine for a quick draft"),
        new ModelChoice(GgmlType.Base,   "Base",   "~142 MB", "good speed/quality balance - a sensible default"),
        new ModelChoice(GgmlType.Small,  "Small",  "~466 MB", "noticeably better on accents and background noise"),
        new ModelChoice(GgmlType.Medium, "Medium", "~1.5 GB", "strong accuracy, several times slower than Small"),
        new ModelChoice(GgmlType.LargeV3,"Large v3","~2.9 GB","best accuracy; wants a lot of RAM and time"),
    };

    public static ModelChoice Default => All[1]; // Base

    /// <summary>Where downloaded models are cached. Created on demand.</summary>
    public static string CacheDirectory
    {
        get
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WhisperSubtitleGenerator", "models");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string PathFor(GgmlType type) =>
        Path.Combine(CacheDirectory, $"ggml-{type.ToString().ToLowerInvariant()}.bin");

    public static bool IsDownloaded(GgmlType type)
    {
        var p = PathFor(type);
        // A zero-length file is the fingerprint of an interrupted download; treat it as absent so
        // the next run re-fetches instead of handing Whisper a truncated model.
        return File.Exists(p) && new FileInfo(p).Length > 0;
    }

    /// <summary>
    /// Ensures the model is on disk, downloading it if needed, and returns its path.
    /// <para>The download goes to a temporary file and is moved into place only on success, so an
    /// interrupted download can never leave a half-written model that looks valid.</para>
    /// </summary>
    public static async Task<string> EnsureDownloadedAsync(
        GgmlType type,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var finalPath = PathFor(type);
        if (IsDownloaded(type)) return finalPath;

        progress?.Report($"Downloading the {type} model - first use only, it is cached afterwards...");

        var tempPath = finalPath + ".partial";
        try
        {
            using (var stream = await WhisperGgmlDownloader.Default
                       .GetGgmlModelAsync(type, cancellationToken: ct).ConfigureAwait(false))
            using (var file = File.Create(tempPath))
            {
                await stream.CopyToAsync(file, ct).ConfigureAwait(false);
            }

            if (File.Exists(finalPath)) File.Delete(finalPath);
            File.Move(tempPath, finalPath);
            progress?.Report($"Model ready: {Path.GetFileName(finalPath)}");
            return finalPath;
        }
        catch
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { /* best effort */ }
            throw;
        }
    }
}
