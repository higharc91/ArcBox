using System.Globalization;

namespace Redline.Presentation.Audio.Synth;

/// <summary>One melody note: a MIDI pitch (0 = rest) lasting <paramref name="Beats"/> quarter notes.</summary>
public sealed record Note(int Midi, double Beats);

/// <summary>
/// One chord of the accompaniment, lasting <paramref name="Beats"/> quarter notes. <paramref name="Symbol"/> is a root
/// (<c>C</c>, <c>F#</c>, <c>Bb</c>…), an optional quality (<c>m</c>, <c>dim</c>, <c>7</c>, <c>m7</c>, <c>maj7</c>) and
/// an optional bass note after a slash (<c>G/D</c>); <c>r</c> is a rest (the band is tacet).
/// </summary>
public sealed record Chord(string Symbol, double Beats);

/// <summary>
/// A national anthem the podium can play. <see cref="Melody"/> is the opening phrase (≈ 10–14 s at <see cref="Bpm"/>
/// quarter notes per minute) and <see cref="Chords"/> our own simple harmonisation of it, beat for beat. A fallback
/// entry (<see cref="HasMelody"/> false) has no melody: the TV plays the REDLINE victory theme instead, and
/// <see cref="Status"/> says why. <see cref="Status"/> starts with "Public domain" or "Fallback".
/// </summary>
public sealed record AnthemInfo(
    string Id,
    string Country,
    string Title,
    string Composer,
    int Year,
    string Status,
    IReadOnlyList<Note>? Melody,
    int Bpm,
    string Meter,
    IReadOnlyList<Chord>? Chords = null)
{
    public bool HasMelody => Melody is { Count: > 0 };
}

/// <summary>
/// The anthems of the 2026 grid's driver nationalities (spec §1), keyed by lower-case ISO 3166-1 alpha-2 code.
/// <para>
/// Only the <b>compositions</b> are used, and only where they are in the public domain; the notes below are our own
/// transcriptions of the melodies' opening phrases from public-domain editions, and the harmony is our own plain
/// I–IV–V setting, not any modern published arrangement. Nothing here comes from a recording.
/// </para>
/// <para>
/// Score notation (one string per anthem, bars separated by <c>|</c>): <c>pitch:length</c>, where pitch is a note
/// name with octave (<c>C4</c> = MIDI 60, <c>F#4</c>, <c>Bb3</c>) or <c>r</c> for a rest, and length is <c>w h q e s
/// t</c> (whole … thirty-second) with optional dots, or a number of quarter-note beats. Chords use the same
/// <c>symbol:length</c> form. Every bar between the first (which may be a pickup) and the last (which may stop early,
/// where the excerpt ends) must be exactly one bar of the meter, so a slip in the rhythm fails to load.
/// </para>
/// </summary>
public static class Anthems
{
    private static readonly IReadOnlyList<AnthemInfo> Entries = Build();
    private static readonly Dictionary<string, AnthemInfo> ById = Entries.ToDictionary(a => a.Id, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<AnthemInfo> All => Entries;

    /// <summary>The entry for an ISO country code (any case), or null if the country is not on the grid.</summary>
    public static AnthemInfo? Find(string id) => ById.GetValueOrDefault(id ?? "");

    private static AnthemInfo[] Build() =>
    [
        // source: "God Save the King", Thesaurus Musicus (London, 1744), the tune as printed in every standard hymnal;
        // G major as in the usual band and hymnal editions. Checked against the LilyPond score in it.wikipedia
        // "Oben am jungen Rhein" (rev. 152501636).
        Pd("gb", "Great Britain", "God Save the King", "Traditional", 1744,
            "Public domain: a traditional melody, first printed in 1744.",
            bpm: 80, meter: "3/4",
            melody: "G4:q G4:q A4:q | F#4:q. G4:e A4:q | B4:q B4:q C5:q | B4:q. A4:e G4:q | A4:q G4:q F#4:q | G4:h.",
            chords: "G:q Em:q D:q | D:h D7:q | G:h C:q | G:h Em:q | Am:q G/D:q D7:q | G:h."),

        // source: P. D. McCormick, "Advance Australia Fair" (Sydney: W. H. Paling, 1879), first couplet ("Australians
        // all let us rejoice, for we are one and free"); C major. Checked against Commons "Advance_Australia_Fair.mid"
        // (public domain).
        Pd("au", "Australia", "Advance Australia Fair", "Peter Dodds McCormick", 1878,
            "Public domain: McCormick died in 1916.",
            bpm: 84, meter: "4/4",
            melody: "G4:q | C5:q G4:q E4:q G4:q | C5:q. C5:e C5:q E5:q | D5:q C5:q B4:q C5:q | D5:h.",
            chords: "G:q | C:w | F:h C:h | G:q Am:q G/B:q C:q | G:h."),

        Fallback("mc", "Monaco", "Hymne Monégasque", "Théophile Bellando de Castro; arr. Léon Jehin", 1841,
            "Fallback for now: the composition is public domain (Bellando de Castro 1841; Jehin's 1896 setting, Jehin " +
            "died in 1928), but no verified score could be consulted when this was built, and a national anthem is not " +
            "transcribed from memory. Add the melody once it has been checked against a score."),

        // source: Adrianus Valerius, "Nederlandtsche Gedenck-clanck" (1626), the modern standard form in G major
        // (4/4 barring); second line of the phrase ending on the tonic ("Wilhelmus van Nassouwe / ben ik van Duitsen
        // bloed"). Checked against the LilyPond score in it.wikipedia "Het Wilhelmus" (rev. 152507650).
        Pd("nl", "Netherlands", "Het Wilhelmus", "Traditional (French tune; set by Adrianus Valerius, 1626)", 1568,
            "Public domain: a 16th-century melody.",
            bpm: 66, meter: "4/4",
            melody: "D4:q | G4:q G4:q A4:e B4:e C5:e A4:e | B4:q A4:e B4:e C5:q B4:q | A4:e G4:e A4:q G4:h",
            chords: "D:q | G:h D:q Am:q | G:h C:q G:q | Am:q D:q G:h"),

        // source: Rouget de Lisle, "Chant de guerre pour l'Armée du Rhin" (Strasbourg, 1792), opening to "le jour de
        // gloire est arrivé"; B-flat major as in the French military band editions. Checked against the LilyPond
        // score in it.wikipedia "La Marsigliese" (rev. 151946643).
        Pd("fr", "France", "La Marseillaise", "Claude Joseph Rouget de Lisle", 1792,
            "Public domain: Rouget de Lisle died in 1836.",
            bpm: 84, meter: "4/4",
            melody: "F4:s F4:e. F4:s | Bb4:q Bb4:q C5:q C5:q | F5:q. D5:e Bb4:e. Bb4:s D5:e. Bb4:s | G4:q Eb5:h C5:e. A4:s | Bb4:w",
            chords: "F:1.25 | Bb:h F:h | Bb:w | Eb:h. F7:q | Bb:w"),

        // source: Michele Novaro, "Il Canto degli Italiani" (Turin, 1847), verse opening to "s'è cinta la testa",
        // E-flat major; the last note is held where the excerpt stops. Checked against the LilyPond score in
        // en.wikipedia "Il Canto degli Italiani" (rev. 1368805845).
        Pd("it", "Italy", "Il Canto degli Italiani", "Michele Novaro", 1847,
            "Public domain: Novaro died in 1885.",
            bpm: 100, meter: "4/4",
            melody: "G4:s G4:q G4:e. F4:s | Ab4:q G4:e r:s Bb4:s Bb4:q Bb4:e. A4:s | C5:q Bb4:e r:s Bb4:s Bb4:q C5:e. D5:s | " +
                    "Eb5:q G4:e. Ab4:s C5:q Bb4:e. G4:s | Ab4:q F4:h.",
            chords: "Eb:2.25 | Bb7:q Eb:h. | F7:q Bb:h. | Eb:h Ab:q Eb:q | Bb7:w"),

        // source: "Marcha Granadera", Libro de la Ordenanza de los toques militares de la Infantería Española (1761),
        // first phrase; C major. Checked against the LilyPond score in it.wikipedia "Marcha Real" (rev. 151372762).
        Pd("es", "Spain", "Marcha Real", "Manuel de Espinosa (attributed)", 1761,
            "Public domain: an 18th-century military march, first printed in 1761. The harmony here is our own, not " +
            "the official 1908 Pérez Casas arrangement.",
            bpm: 76, meter: "4/4",
            melody: "C5:q G4:q E5:q C5:e G5:e | F5:e E5:e D5:e C5:e C5:e B4:e A4:e G4:e | C5:q D5:q E5:q r:e G5:e | F5:e E5:e D5:e C5:e G5:h",
            chords: "C:w | F:h G:h | C:w | G7:h C:h"),

        // source: Calixa Lavallée, "Chant national" (Québec, 1880), "O Canada! Our home and native land!"; F major.
        // Checked against Commons "O_Canada.mid" (public domain).
        Pd("ca", "Canada", "O Canada", "Calixa Lavallée", 1880,
            "Public domain: Lavallée died in 1891.",
            bpm: 76, meter: "4/4",
            melody: "A4:h C5:q.. C5:s | F4:h. G4:q | A4:q Bb4:q C5:q D5:q | G4:w",
            chords: "F:h C/E:h | Dm:h Bb:q C:q | F:q Gm:q F/A:q Bb:q | C:w"),

        Fallback("ar", "Argentina", "Himno Nacional Argentino", "Blas Parera", 1813,
            "Fallback for now: the composition is public domain (Parera died in 1840; Esnaola's 1860 official version, " +
            "Esnaola died in 1878), but no verified score could be consulted when this was built, and a national anthem " +
            "is not transcribed from memory. Add the melody once it has been checked against a score."),

        Fallback("nz", "New Zealand", "God Defend New Zealand", "John Joseph Woods", 1876,
            "Fallback for now: the composition is public domain (Woods died in 1934), but no verified score could be " +
            "consulted when this was built, and a national anthem is not transcribed from memory. Add the melody once " +
            "it has been checked against a score."),

        // source: Haydn, "Gott erhalte Franz den Kaiser" (Vienna, 1797), first line ("Einigkeit und Recht und Freiheit
        // für das deutsche Vaterland"); E-flat major. Checked against the LilyPond score in en.wikipedia
        // "Deutschlandlied" (rev. 1375806761).
        Pd("de", "Germany", "Das Lied der Deutschen", "Joseph Haydn", 1797,
            "Public domain: Haydn died in 1809.",
            bpm: 84, meter: "4/4",
            melody: "Eb4:q. F4:e | G4:q F4:q Ab4:q G4:q | F4:e D4:e Eb4:q C5:q Bb4:q | Ab4:q G4:q F4:q G4:e Eb4:e | Bb4:w",
            chords: "Eb:h | Eb:q Bb7:h Eb:q | Bb:q Eb:q Ab:q Eb:q | Ab:q Eb:q Bb7:q Eb:q | Eb:w"),

        Fallback("br", "Brazil", "Hino Nacional Brasileiro", "Francisco Manuel da Silva", 1831,
            "Fallback for now: the composition is public domain (da Silva died in 1865), but no verified score could be " +
            "consulted when this was built, and a national anthem is not transcribed from memory. Note also that " +
            "Brazilian law (Lei 5.700/1971, art. 34) restricts unauthorised instrumental arrangements of the anthem."),

        // source: Fredrik Pacius, "Vårt land" (Helsinki, 1848), first two lines ("Oi maamme, Suomi, synnyinmaa, soi,
        // sana kultainen!"); B-flat major. Checked against the Wikisource transcription of "The Songs of Ensign
        // Stål" (1925), page 44 (rev. 16314830).
        Pd("fi", "Finland", "Maamme", "Fredrik Pacius", 1848,
            "Public domain: Pacius died in 1891.",
            bpm: 90, meter: "3/4",
            melody: "F4:e D4:e Eb4:e | F4:q. Bb4:e C5:e. F4:s | D5:h Bb4:q | G4:e. C5:s Bb4:q A4:q | Bb4:h F4:q | " +
                    "C5:e. Bb4:s A4:e G4:e F4:e Eb4:e | D4:e G4:e F4:h",
            chords: "Bb:1.5 | Bb:h F:q | Bb:h. | Eb:q Bb/F:q F7:q | Bb:h. | F:h F7:q | Gm:q Bb:h"),

        Fallback("th", "Thailand", "Phleng Chat Thai", "Phra Chenduriyang (Peter Feit)", 1932,
            "Fallback: the public-domain status of the composition outside Thailand is unclear (the composer died in 1968)."),

        Fallback("mx", "Mexico", "Himno Nacional Mexicano", "Jaime Nunó", 1854,
            "Fallback: the composition is public domain (Nunó died in 1908), but Mexican law (Ley sobre el Escudo, la " +
            "Bandera y el Himno Nacionales) forbids altering or arranging the anthem."),
    ];

    private static AnthemInfo Pd(string id, string country, string title, string composer, int year, string status,
        int bpm, string meter, string melody, string chords) =>
        new(id, country, title, composer, year, status, Melody(meter, melody), bpm, meter, Chords(meter, chords));

    private static AnthemInfo Fallback(string id, string country, string title, string composer, int year, string status) =>
        new(id, country, title, composer, year, status, null, 0, "4/4");

    // ---- Score notation ----------------------------------------------------------------------------------------

    /// <summary>Reads a melody in the score notation described on <see cref="Anthems"/>.</summary>
    /// <exception cref="FormatException">A token is malformed or a bar does not add up to the meter.</exception>
    public static IReadOnlyList<Note> Melody(string meter, string score) => Read(meter, score, (pitch, beats) => new Note(Pitch(pitch), beats));

    /// <summary>Reads a chord list in the score notation described on <see cref="Anthems"/>.</summary>
    /// <exception cref="FormatException">A symbol or length is malformed or a bar does not add up to the meter.</exception>
    public static IReadOnlyList<Chord> Chords(string meter, string score) => Read(meter, score, (symbol, beats) =>
    {
        _ = ChordTones(symbol);
        return new Chord(symbol, beats);
    });

    private static List<T> Read<T>(string meter, string score, Func<string, double, T> make)
    {
        var bar = BarBeats(meter);
        var bars = score.Split('|');
        var items = new List<T>();
        for (var b = 0; b < bars.Length; b++)
        {
            double sum = 0;
            foreach (var token in bars[b].Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var colon = token.LastIndexOf(':');
                if (colon <= 0) throw new FormatException($"'{token}' is not symbol:length");
                var beats = Length(token[(colon + 1)..]);
                items.Add(make(token[..colon], beats));
                sum += beats;
            }
            var first = b == 0;
            var last = b == bars.Length - 1;
            var ok = (first || last) ? sum > 0 && sum <= bar + 1e-9 : Math.Abs(sum - bar) < 1e-9;
            if (!ok) throw new FormatException($"bar {b + 1} lasts {sum} beats, not {bar} ({meter}): '{bars[b].Trim()}'");
        }
        return items;
    }

    private static double BarBeats(string meter)
    {
        var parts = meter.Split('/');
        if (parts.Length != 2 || !int.TryParse(parts[0], out var num) || !int.TryParse(parts[1], out var den) || num <= 0 || den <= 0)
            throw new FormatException($"'{meter}' is not a meter");
        return num * 4.0 / den;
    }

    private static double Length(string text)
    {
        if (text.Length > 0 && char.IsDigit(text[0]))
        {
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && n > 0
                ? n
                : throw new FormatException($"'{text}' is not a length");
        }
        var basic = text.Length == 0 ? -1 : text[0] switch
        {
            'w' => 4, 'h' => 2, 'q' => 1, 'e' => 0.5, 's' => 0.25, 't' => 0.125, _ => -1,
        };
        if (basic < 0 || text[1..].Any(c => c != '.')) throw new FormatException($"'{text}' is not a length");
        var dots = text.Length - 1;
        return basic * (2 - Math.Pow(0.5, dots));
    }

    /// <summary>MIDI number of a note name with octave (C4 = 60); <c>r</c> is a rest (0).</summary>
    private static int Pitch(string name)
    {
        if (name == "r") return 0;
        var (pc, rest) = PitchClass(name);
        return int.TryParse(rest, NumberStyles.None, CultureInfo.InvariantCulture, out var octave) && octave is >= 0 and <= 8
            ? 12 * (octave + 1) + pc
            : throw new FormatException($"'{name}' is not a note");
    }

    private static (int Pc, string Tail) PitchClass(string name)
    {
        int[] naturals = [9, 11, 0, 2, 4, 5, 7]; // A B C D E F G
        if (name.Length == 0 || name[0] < 'A' || name[0] > 'G') throw new FormatException($"'{name}' is not a note");
        var pc = naturals[name[0] - 'A'];
        var i = 1;
        if (i < name.Length && name[i] == '#') { pc++; i++; }
        else if (i < name.Length && name[i] == 'b') { pc--; i++; }
        return ((pc + 12) % 12, name[i..]);
    }

    /// <summary>
    /// The pitch classes of a chord symbol (root first) and its bass pitch class; null for a rest (<c>r</c>).
    /// </summary>
    internal static (int Bass, int[] Tones)? ChordTones(string symbol)
    {
        if (symbol == "r") return null;
        var slash = symbol.IndexOf('/');
        var head = slash < 0 ? symbol : symbol[..slash];
        var (root, quality) = PitchClass(head);
        int[] intervals = quality switch
        {
            "" => [0, 4, 7],
            "m" => [0, 3, 7],
            "dim" => [0, 3, 6],
            "7" => [0, 4, 7, 10],
            "m7" => [0, 3, 7, 10],
            "maj7" => [0, 4, 7, 11],
            _ => throw new FormatException($"'{symbol}' is not a chord"),
        };
        var bass = root;
        if (slash >= 0)
        {
            var (b, extra) = PitchClass(symbol[(slash + 1)..]);
            if (extra.Length > 0) throw new FormatException($"'{symbol}' is not a chord");
            bass = b;
        }
        return (bass, [.. intervals.Select(i => (root + i) % 12)]);
    }
}
