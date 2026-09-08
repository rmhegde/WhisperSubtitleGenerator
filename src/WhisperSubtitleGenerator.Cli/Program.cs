using System.Runtime.InteropServices;
using WhisperSubtitleGenerator.Cli;
using WhisperSubtitleGenerator.Core.Audio;
using WhisperSubtitleGenerator.Core.Models;
using WhisperSubtitleGenerator.Core.Transcription;
using WhisperSubtitleGenerator.Core.Video;

// Exit codes, so this is usable from a script:
//   0  everything succeeded (or was skipped)
//   1  usage error, or a missing prerequisite
//   2  ran, but at least one file failed
const int ExitOk = 0, ExitUsage = 1, ExitSomeFailed = 2;

var options = CliOptions.Parse(args);

if (options.ShowHelp)      { Help.Print(); return ExitOk; }
if (options.ListModels)    { Help.PrintModels(); return ExitOk; }
if (options.ListLanguages) { Help.PrintLanguages(); return ExitOk; }

if (options.Error is not null)
{
    Console.Error.WriteLine("wsg: " + options.Error);
    return ExitUsage;
}

void Log(string message)
{
    // Progress goes to STDERR so stdout carries only the result lines. That keeps
    // `wsg clip.mp4 | something` usable, and lets `2>/dev/null` silence the chatter.
    if (!options.Quiet) Console.Error.WriteLine(message);
}

// ---- prerequisites ---------------------------------------------------------------------------
var ffmpeg = FfmpegLocator.Locate();
if (!ffmpeg.Found)
{
    Console.Error.WriteLine("wsg: ffmpeg was not found, and it is required to read audio and video.");
    if (OperatingSystem.IsWindows())
    {
        Console.Error.WriteLine("     Install it with:  winget install Gyan.FFmpeg");
        Console.Error.WriteLine("     Or run the desktop app once, which can download it for you.");
    }
    else if (OperatingSystem.IsMacOS())
    {
        Console.Error.WriteLine("     Install it with:  brew install ffmpeg");
    }
    else
    {
        Console.Error.WriteLine("     Install it with:  sudo apt install ffmpeg   (or your package manager)");
    }
    Console.Error.WriteLine($"     Or set {FfmpegLocator.OverrideVariable} to the full path of an ffmpeg binary.");
    return ExitUsage;
}
Log($"ffmpeg: {ffmpeg.Version}");

var files = options.ResolveInputs();
if (files.Count == 0)
{
    Console.Error.WriteLine("wsg: nothing to do - no media files matched the inputs given.");
    return ExitUsage;
}

// ---- run -------------------------------------------------------------------------------------
var items = files.Select(f => new BatchItem(f)).ToList();
var model = WhisperModelCatalog.All.First(m => m.Type == options.Model);

Log($"{items.Count} file(s), {model.DisplayName} model, language {options.Language}" +
    (options.Translate ? ", translating to English" : "") +
    (options.Burn is { } b ? $", {(b == BurnMode.HardBurn ? "burning in" : "adding a subtitle track")}" : ""));

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    // Handle Ctrl+C ourselves so a long transcription stops cleanly and its temp WAV is removed,
    // rather than the process being torn down mid-write.
    e.Cancel = true;
    Console.Error.WriteLine("wsg: cancelling after the current file...");
    cts.Cancel();
};

var summary = await new BatchProcessor().RunAsync(
    items,
    options.ToTranscriptionOptions(),
    skipExisting: options.SkipExisting,
    burn: options.Burn is { } mode ? new BurnOptions { Mode = mode } : null,
    status: new Progress<string>(Log),
    onItemChanged: item =>
    {
        if (item.State == BatchItemState.Running) Log($"--- {item.FileName}");
        else if (item.State == BatchItemState.Failed)
            Console.Error.WriteLine($"FAILED  {item.FileName}: {item.Message}");
    },
    ct: cts.Token);

// Result lines on stdout, one output path per line, so the caller can pipe them.
foreach (var item in items.Where(i => i.State == BatchItemState.Done))
{
    foreach (var path in item.OutputPaths) Console.WriteLine(path);
}

Log($"Done in {summary.Elapsed.TotalSeconds:F1}s - " +
    $"{summary.Done} succeeded, {summary.Failed} failed, {summary.Skipped} skipped.");

return summary.Failed > 0 ? ExitSomeFailed : ExitOk;

// ---- help ------------------------------------------------------------------------------------
static class Help
{
    public static void Print()
    {
        var rid = RuntimeInformation.RuntimeIdentifier;
        var models = string.Join(" | ", WhisperModelCatalog.All.Select(m => m.Type.ToString().ToLowerInvariant()));
        var cache = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WhisperSubtitleGenerator", "models");

        // Plain concatenation rather than a raw interpolated string: the help text contains braces
        // and the escaping needed to keep them literal makes it unreadable.
        Console.WriteLine("wsg - Whisper Subtitle Generator  (" + rid + ")");
        Console.WriteLine();
        Console.WriteLine("Generates .srt / .vtt subtitles locally. No API key, no upload.");
        Console.WriteLine();
        Console.WriteLine("USAGE");
        Console.WriteLine("  wsg <file-or-folder>... [options]");
        Console.WriteLine();
        Console.WriteLine("OPTIONS");
        Console.WriteLine("  -m, --model <name>       " + models + "   (default: base)");
        Console.WriteLine("  -l, --language <code>    ISO code, or 'auto' to detect        (default: auto)");
        Console.WriteLine("  -t, --translate          translate to English instead of transcribing");
        Console.WriteLine("  -f, --format <list>      srt | vtt | both                     (default: srt)");
        Console.WriteLine("  -o, --output <dir>       where to write             (default: beside each source)");
        Console.WriteLine("      --burn [hard|soft]   write subtitles into the video; 'hard' re-encodes and");
        Console.WriteLine("                           plays anywhere, 'soft' adds a switchable track fast");
        Console.WriteLine("      --skip-existing      skip files whose subtitles already exist");
        Console.WriteLine("  -r, --recursive          search folders recursively");
        Console.WriteLine("  -q, --quiet              suppress progress on stderr");
        Console.WriteLine("      --list-models        show the models and their sizes");
        Console.WriteLine("      --list-languages     show all supported languages");
        Console.WriteLine("  -h, --help               this text");
        Console.WriteLine();
        Console.WriteLine("EXAMPLES");
        Console.WriteLine("  wsg lecture.mp4");
        Console.WriteLine("  wsg *.mkv -m small -l en -f both");
        Console.WriteLine("  wsg ./season1 -r --skip-existing -o ./subs");
        Console.WriteLine("  wsg interview.mov -l hi --translate");
        Console.WriteLine("  wsg promo.mp4 --burn hard");
        Console.WriteLine();
        Console.WriteLine("NOTES");
        Console.WriteLine("  Progress goes to stderr and result paths to stdout, so this pipes cleanly.");
        Console.WriteLine("  Exit codes: 0 all good, 1 usage or missing prerequisite, 2 some files failed.");
        Console.WriteLine("  ffmpeg must be installed. Set " + FfmpegLocator.OverrideVariable +
                          " to point at a specific binary.");
        Console.WriteLine("  Models download once into " + cache);
    }

    public static void PrintModels()
    {
        Console.WriteLine("MODELS  (--model)");
        foreach (var m in WhisperModelCatalog.All)
        {
            var cached = WhisperModelCatalog.IsDownloaded(m.Type) ? "  [downloaded]" : "";
            Console.WriteLine($"  {m.Type,-8} {m.ApproxSize,-9} {m.Notes}{cached}");
        }
    }

    public static void PrintLanguages()
    {
        Console.WriteLine($"LANGUAGES  (--language)   {WhisperLanguages.Count} supported");
        foreach (var l in WhisperLanguages.All)
        {
            Console.WriteLine($"  {l.Code,-5} {l.Name}");
        }
    }
}
