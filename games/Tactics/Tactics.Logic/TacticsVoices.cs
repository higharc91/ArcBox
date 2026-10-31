using ArcBox.Sdk.Speech;

namespace Tactics.Logic;

/// <summary>
/// Wardenflame's voice themes: one per speaker, so a line with no pre-rendered clip is spoken live (<c>/api/tts</c>) in
/// the same voice its clips use. The casting's source of truth is <c>tools/tactics-lore/voice-casting.json</c>; a test
/// keeps this list in step with it, and lore characters name their theme in <c>voice.theme</c>.
/// </summary>
/// <remarks>
/// Presets are Chirp 3: HD voices, which share names with the Gemini TTS voices the clips are rendered with. Chirp 3: HD
/// rejects <c>pitch</c>, so the pitch shifts and filters some speakers get in their clips don't reach live speech.
/// </remarks>
public static class TacticsVoices
{
    /// <summary>The storyteller who narrates scenes and battles.</summary>
    public const string Narrator = "tactics.narrator";

    /// <summary>A named character's theme: <c>tactics.npc.&lt;id&gt;</c>, with underscores as hyphens (theme ids allow no underscores).</summary>
    public static string Npc(string characterId) => $"tactics.npc.{characterId.Replace('_', '-')}";

    /// <summary>A player voice set's cast: <c>tactics.voice.&lt;set&gt;.&lt;low|high&gt;</c>.</summary>
    public static string PlayerVoice(string set, string cast) => $"tactics.voice.{set}.{cast}";

    private static string Voice(string preset) => $"en-US-Chirp3-HD-{preset}";

    public static IReadOnlyList<VoiceTheme> All { get; } =
    [
        new(Narrator, "Wardenflame narrator: a warm storyteller", Voice("Algieba"), Fallback: new FallbackVoice()),
        new(Npc("bree_tallow"), "Bree Tallow: alto, brisk and kind", Voice("Zephyr"), Fallback: new FallbackVoice()),
        new(Npc("celandine_marrowby"), "Lady Celandine Marrowby: smooth, cultured alto", Voice("Autonoe"), Fallback: new FallbackVoice()),
        new(Npc("corvin_ashby"), "Corvin Ashby: light, quick, anxious", Voice("Sadachbia"), Fallback: new FallbackVoice()),
        new(Npc("dagna_holt"), "Dagna Holt: low, flat, few words", Voice("Callirrhoe"), Fallback: new FallbackVoice()),
        new(Npc("halden_varrow"), "Halden Varrow: warm baritone, hoarse", Voice("Charon"), Fallback: new FallbackVoice()),
        new(Npc("halvard"), "Ser Halvard: deep and hollow inside the helm", Voice("Schedar"), Fallback: new FallbackVoice()),
        new(Npc("hooded_stranger"), "The Hooded Stranger: shares scholarly.low; pitch and echo keep them apart", Voice("Sadaltager"), Fallback: new FallbackVoice()),
        new(Npc("ivo_rime"), "Ivo Rime: soft-spoken, exhausted", Voice("Enceladus"), Fallback: new FallbackVoice()),
        new(Npc("lark"), "Lark: androgynous, musical", Voice("Aoede"), Fallback: new FallbackVoice()),
        new(Npc("maren_sorrel"), "Maren Sorrel: warm, persuasive alto", Voice("Sulafat"), Fallback: new FallbackVoice()),
        new(Npc("mother_aubrine"), "Mother Aubrine: clear mezzo, measured", Voice("Vindemiatrix"), Fallback: new FallbackVoice()),
        new(Npc("mother_skarrow"), "Mother Skarrow: cracked, rasping", Voice("Laomedeia"), Fallback: new FallbackVoice()),
        new(Npc("old_maud"), "Old Maud: ancient, papery", Voice("Achernar"), Fallback: new FallbackVoice()),
        new(Npc("oswin_hale"), "Oswin Hale: dry, cold, very old", Voice("Rasalgethi"), Fallback: new FallbackVoice()),
        new(Npc("pip_ashdown"), "Pip Ashdown: a child; shares earnest.high, pitched up", Voice("Leda"), Fallback: new FallbackVoice()),
        new(Npc("rook"), "Rook: dry, low, unhurried", Voice("Umbriel"), Fallback: new FallbackVoice()),
        new(Npc("tobin_fell"), "Tobin Fell: deep, rough bass", Voice("Orus"), Fallback: new FallbackVoice()),
        new(Npc("vorga"), "Vorga: young swagger; clear and loud", Voice("Iapetus"), Fallback: new FallbackVoice()),
        new(Npc("ysolde"), "Ysolde: shares wry.high; fey shimmer and pitch keep them apart", Voice("Despina"), Fallback: new FallbackVoice()),
        new(PlayerVoice("earnest", "high"), "Player voice: earnest, high", Voice("Leda"), Fallback: new FallbackVoice()),
        new(PlayerVoice("earnest", "low"), "Player voice: earnest, low", Voice("Achird"), Fallback: new FallbackVoice()),
        new(PlayerVoice("fierce", "high"), "Player voice: fierce, high", Voice("Pulcherrima"), Fallback: new FallbackVoice()),
        new(PlayerVoice("fierce", "low"), "Player voice: fierce, low", Voice("Fenrir"), Fallback: new FallbackVoice()),
        new(PlayerVoice("grim", "high"), "Player voice: grim, high", Voice("Gacrux"), Fallback: new FallbackVoice()),
        new(PlayerVoice("grim", "low"), "Player voice: grim, low", Voice("Algenib"), Fallback: new FallbackVoice()),
        new(PlayerVoice("scholarly", "high"), "Player voice: scholarly, high", Voice("Erinome"), Fallback: new FallbackVoice()),
        new(PlayerVoice("scholarly", "low"), "Player voice: scholarly, low", Voice("Sadaltager"), Fallback: new FallbackVoice()),
        new(PlayerVoice("steady", "high"), "Player voice: steady, high", Voice("Kore"), Fallback: new FallbackVoice()),
        new(PlayerVoice("steady", "low"), "Player voice: steady, low", Voice("Alnilam"), Fallback: new FallbackVoice()),
        new(PlayerVoice("wry", "high"), "Player voice: wry, high", Voice("Despina"), Fallback: new FallbackVoice()),
        new(PlayerVoice("wry", "low"), "Player voice: wry, low", Voice("Zubenelgenubi"), Fallback: new FallbackVoice()),
    ];
}
