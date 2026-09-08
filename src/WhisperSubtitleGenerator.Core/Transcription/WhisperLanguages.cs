namespace WhisperSubtitleGenerator.Core.Transcription;

/// <summary>A language Whisper can transcribe, as an ISO-639-1 code plus its English name.</summary>
public sealed record WhisperLanguage(string Code, string Name)
{
    public bool IsAuto => Code == "auto";

    /// <summary>"English (en)", or just "Auto-detect" for the sentinel.</summary>
    public override string ToString() => IsAuto ? "Auto-detect" : $"{Name} ({Code})";
}

/// <summary>
/// Every language the Whisper models are trained on - all 99, not a hand-picked subset.
///
/// <para>Coverage varies enormously across them. English dominates the training data; many of the
/// languages near the end of this list were seen far less often and transcribe poorly on the
/// smaller models. Offering the full set is still right: a user who needs Kannada or Sinhala should
/// be able to select it and judge the result, rather than find it simply absent.</para>
///
/// <para>Codes are ISO-639-1 as whisper.cpp expects them. Two are deliberately not ISO-639-1:
/// <c>haw</c> (Hawaiian) and <c>yue</c> (Cantonese) are the codes Whisper itself uses.</para>
/// </summary>
public static class WhisperLanguages
{
    public static readonly WhisperLanguage Auto = new("auto", "Auto-detect");

    private static readonly WhisperLanguage[] Languages =
    {
        new("af", "Afrikaans"),      new("am", "Amharic"),        new("ar", "Arabic"),
        new("as", "Assamese"),       new("az", "Azerbaijani"),    new("ba", "Bashkir"),
        new("be", "Belarusian"),     new("bg", "Bulgarian"),      new("bn", "Bengali"),
        new("bo", "Tibetan"),        new("br", "Breton"),         new("bs", "Bosnian"),
        new("ca", "Catalan"),        new("cs", "Czech"),          new("cy", "Welsh"),
        new("da", "Danish"),         new("de", "German"),         new("el", "Greek"),
        new("en", "English"),        new("es", "Spanish"),        new("et", "Estonian"),
        new("eu", "Basque"),         new("fa", "Persian"),        new("fi", "Finnish"),
        new("fo", "Faroese"),        new("fr", "French"),         new("gl", "Galician"),
        new("gu", "Gujarati"),       new("ha", "Hausa"),          new("haw","Hawaiian"),
        new("he", "Hebrew"),         new("hi", "Hindi"),          new("hr", "Croatian"),
        new("ht", "Haitian Creole"), new("hu", "Hungarian"),      new("hy", "Armenian"),
        new("id", "Indonesian"),     new("is", "Icelandic"),      new("it", "Italian"),
        new("ja", "Japanese"),       new("jw", "Javanese"),       new("ka", "Georgian"),
        new("kk", "Kazakh"),         new("km", "Khmer"),          new("kn", "Kannada"),
        new("ko", "Korean"),         new("la", "Latin"),          new("lb", "Luxembourgish"),
        new("ln", "Lingala"),        new("lo", "Lao"),            new("lt", "Lithuanian"),
        new("lv", "Latvian"),        new("mg", "Malagasy"),       new("mi", "Maori"),
        new("mk", "Macedonian"),     new("ml", "Malayalam"),      new("mn", "Mongolian"),
        new("mr", "Marathi"),        new("ms", "Malay"),          new("mt", "Maltese"),
        new("my", "Myanmar"),        new("ne", "Nepali"),         new("nl", "Dutch"),
        new("nn", "Nynorsk"),        new("no", "Norwegian"),      new("oc", "Occitan"),
        new("pa", "Punjabi"),        new("pl", "Polish"),         new("ps", "Pashto"),
        new("pt", "Portuguese"),     new("ro", "Romanian"),       new("ru", "Russian"),
        new("sa", "Sanskrit"),       new("sd", "Sindhi"),         new("si", "Sinhala"),
        new("sk", "Slovak"),         new("sl", "Slovenian"),      new("sn", "Shona"),
        new("so", "Somali"),         new("sq", "Albanian"),       new("sr", "Serbian"),
        new("su", "Sundanese"),      new("sv", "Swedish"),        new("sw", "Swahili"),
        new("ta", "Tamil"),          new("te", "Telugu"),         new("tg", "Tajik"),
        new("th", "Thai"),           new("tk", "Turkmen"),        new("tl", "Tagalog"),
        new("tr", "Turkish"),        new("tt", "Tatar"),          new("uk", "Ukrainian"),
        new("ur", "Urdu"),           new("uz", "Uzbek"),          new("vi", "Vietnamese"),
        new("yi", "Yiddish"),        new("yo", "Yoruba"),         new("yue","Cantonese"),
        new("zh", "Chinese"),
    };

    /// <summary>Auto-detect first, then every language by English name.</summary>
    public static IReadOnlyList<WhisperLanguage> All { get; } =
        new[] { Auto }.Concat(Languages.OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase)).ToArray();

    /// <summary>Languages worth putting at the top of a picker for this project's users.</summary>
    public static IReadOnlyList<WhisperLanguage> Common { get; } =
        new[] { "en", "hi", "kn", "ta", "te", "mr", "bn", "gu", "ml", "pa", "ur", "ar" }
            .Select(Find)
            .Where(l => l is not null)
            .Select(l => l!)
            .ToArray();

    public static WhisperLanguage? Find(string code) =>
        code == "auto" ? Auto
        : Languages.FirstOrDefault(l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase));

    /// <summary>Turns a Whisper-reported code back into a display name, falling back to the code.</summary>
    public static string NameFor(string code) => Find(code)?.Name ?? code;

    public static int Count => Languages.Length;
}
