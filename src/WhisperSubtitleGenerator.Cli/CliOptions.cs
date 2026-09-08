using WhisperSubtitleGenerator.Core.Models;
using WhisperSubtitleGenerator.Core.Subtitles;
using WhisperSubtitleGenerator.Core.Transcription;
using WhisperSubtitleGenerator.Core.Video;
using Whisper.net.Ggml;

namespace WhisperSubtitleGenerator.Cli;

/// <summary>What the command line asked for, once parsed.</summary>
public sealed class CliOptions
{
    public List<string> Inputs { get; } = new();
    public GgmlType Model { get; set; } = WhisperModelCatalog.Default.Type;
    public string Language { get; set; } = "auto";
    public bool Translate { get; set; }
    public List<SubtitleFormat> Formats { get; } = new();
    public string? OutputDirectory { get; set; }
    public BurnMode? Burn { get; set; }
    public bool SkipExisting { get; set; }
    public bool Quiet { get; set; }
    public bool Recursive { get; set; }

    public bool ShowHelp { get; set; }
    public bool ListLanguages { get; set; }
    public bool ListModels { get; set; }

    /// <summary>Populated when parsing failed; the caller prints it and exits with a usage code.</summary>
    public string? Error { get; private set; }

    private static readonly string[] MediaExtensions =
    {
        ".mp4", ".mkv", ".avi", ".mov", ".webm", ".wmv", ".flv", ".m4v",
        ".mp3", ".wav", ".m4a", ".flac", ".ogg", ".aac", ".wma", ".opus"
    };

    public static bool IsMedia(string path) =>
        MediaExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Parses argv by hand rather than pulling in a command-line library.
    /// <para>The surface here is a dozen flags. System.CommandLine is still a prerelease package,
    /// and taking a preview dependency into an open-source project to save a hundred lines is a
    /// poor trade - it would be the only unstable thing in the dependency graph.</para>
    /// </summary>
    public static CliOptions Parse(string[] args)
    {
        var o = new CliOptions();

        if (args.Length == 0)
        {
            o.ShowHelp = true;
            return o;
        }

        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i];

            string? Next(string flag)
            {
                if (i + 1 >= args.Length || args[i + 1].StartsWith('-'))
                {
                    o.Error = $"{flag} needs a value.";
                    return null;
                }
                return args[++i];
            }

            switch (a)
            {
                case "-h" or "--help":            o.ShowHelp = true; return o;
                case "--list-languages":          o.ListLanguages = true; return o;
                case "--list-models":             o.ListModels = true; return o;
                case "-q" or "--quiet":           o.Quiet = true; break;
                case "-t" or "--translate":       o.Translate = true; break;
                case "-r" or "--recursive":       o.Recursive = true; break;
                case "--skip-existing":           o.SkipExisting = true; break;

                case "-m" or "--model":
                {
                    var v = Next(a); if (v is null) return o;
                    var match = WhisperModelCatalog.All.FirstOrDefault(m =>
                        string.Equals(m.Type.ToString(), v, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(m.DisplayName.Replace(" ", ""), v.Replace(" ", ""), StringComparison.OrdinalIgnoreCase));
                    if (match is null)
                    {
                        o.Error = $"Unknown model '{v}'. Try --list-models.";
                        return o;
                    }
                    o.Model = match.Type;
                    break;
                }

                case "-l" or "--language":
                {
                    var v = Next(a); if (v is null) return o;
                    if (WhisperLanguages.Find(v) is null)
                    {
                        o.Error = $"Unknown language '{v}'. Try --list-languages.";
                        return o;
                    }
                    o.Language = v.ToLowerInvariant();
                    break;
                }

                case "-f" or "--format":
                {
                    var v = Next(a); if (v is null) return o;
                    foreach (var part in v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        switch (part.ToLowerInvariant().TrimStart('.'))
                        {
                            case "srt":  o.Formats.Add(SubtitleFormat.Srt); break;
                            case "vtt":  o.Formats.Add(SubtitleFormat.Vtt); break;
                            case "both": o.Formats.Add(SubtitleFormat.Srt); o.Formats.Add(SubtitleFormat.Vtt); break;
                            default:
                                o.Error = $"Unknown format '{part}'. Use srt, vtt or both.";
                                return o;
                        }
                    }
                    break;
                }

                case "-o" or "--output":
                {
                    var v = Next(a); if (v is null) return o;
                    o.OutputDirectory = v;
                    break;
                }

                case "--burn":
                {
                    // The mode is optional: "--burn" alone means hard burn, which is what most
                    // people want when they ask for subtitles in the video.
                    if (i + 1 < args.Length && !args[i + 1].StartsWith('-'))
                    {
                        var v = args[i + 1].ToLowerInvariant();
                        if (v is "hard" or "soft")
                        {
                            i++;
                            o.Burn = v == "hard" ? BurnMode.HardBurn : BurnMode.SoftMux;
                            break;
                        }
                        // Not a mode - leave it for the next iteration to treat as an input path.
                    }
                    o.Burn = BurnMode.HardBurn;
                    break;
                }

                default:
                    if (a.StartsWith('-'))
                    {
                        o.Error = $"Unknown option '{a}'. Try --help.";
                        return o;
                    }
                    o.Inputs.Add(a);
                    break;
            }
        }

        if (o.Formats.Count == 0) o.Formats.Add(SubtitleFormat.Srt);
        // Duplicates would write the same file twice; "-f srt,both" is an easy way to ask for that.
        var distinct = o.Formats.Distinct().ToList();
        o.Formats.Clear();
        o.Formats.AddRange(distinct);

        if (o.Inputs.Count == 0) o.Error = "No input files. Try --help.";
        return o;
    }

    /// <summary>
    /// Turns the raw inputs into concrete files: a directory contributes the media inside it, and
    /// a plain path is taken as-is so a nonexistent file still produces a clear per-file error
    /// rather than silently vanishing from the batch.
    /// </summary>
    public IReadOnlyList<string> ResolveInputs()
    {
        var files = new List<string>();
        var search = Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

        foreach (var input in Inputs)
        {
            if (Directory.Exists(input))
            {
                files.AddRange(Directory.EnumerateFiles(input, "*", search).Where(IsMedia));
            }
            else
            {
                files.Add(input);
            }
        }

        return files.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public TranscriptionOptions ToTranscriptionOptions() => new()
    {
        Model = Model,
        Language = Language,
        TranslateToEnglish = Translate,
        Formats = Formats,
        OutputDirectory = OutputDirectory
    };
}
