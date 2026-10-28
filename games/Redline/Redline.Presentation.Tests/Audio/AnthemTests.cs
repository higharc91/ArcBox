using System.Collections.Concurrent;
using Redline.Presentation.Audio.Synth;

namespace Redline.Presentation.Tests.Audio;

public class AnthemTests
{
    private const int Rate = 44100;
    private static readonly double MaxSample = short.MaxValue * Math.Pow(10, -1 / 20.0); // −1 dBFS

    /// <summary>The 15 driver nationalities on the 2026 grid (ISO 3166-1 alpha-2, lower case as in <c>Podium.AnthemId</c>).</summary>
    private static readonly string[] GridNationalities =
        ["gb", "au", "mc", "nl", "fr", "it", "es", "ca", "ar", "nz", "de", "br", "fi", "th", "mx"];

    /// <summary>Spec §1: fallback countries play the REDLINE theme instead of an anthem.</summary>
    private static readonly string[] SpecFallbacks = ["th", "mx"];

    public static TheoryData<string> WithMelody => [.. Anthems.All.Where(a => a.HasMelody).Select(a => a.Id)];
    public static TheoryData<string> Everything => [.. Anthems.All.Select(a => a.Id)];

    // Rendering takes a moment; every test shares one render per anthem.
    private static readonly ConcurrentDictionary<string, Pcm> Rendered = new();
    private static Pcm Render(string id) => Rendered.GetOrAdd(id, k => AnthemSynth.Render(Anthems.Find(k)!));
    private static readonly Lazy<Pcm> Theme = new(VictoryTheme.Render);

    private static double Rms(ReadOnlySpan<short> s)
    {
        double sum = 0;
        foreach (var v in s) sum += (double)v * v;
        return Math.Sqrt(sum / s.Length) / short.MaxValue;
    }

    // ---- Catalogue ---------------------------------------------------------------------------------------------

    [Fact]
    public void Every_grid_nationality_resolves_to_an_anthem_entry()
    {
        foreach (var id in GridNationalities)
        {
            var info = Anthems.Find(id);
            Assert.NotNull(info);
            Assert.Equal(id, info.Id);
        }
        Assert.Equal(GridNationalities.Order(), Anthems.All.Select(a => a.Id).Order());
    }

    [Fact]
    public void Find_ignores_case_and_returns_null_for_unknown_countries()
    {
        Assert.Same(Anthems.Find("gb"), Anthems.Find("GB"));
        Assert.Null(Anthems.Find("zz"));
        Assert.Null(Anthems.Find(""));
    }

    [Theory]
    [MemberData(nameof(Everything))]
    public void Every_entry_records_country_title_composer_year_and_status(string id)
    {
        var a = Anthems.Find(id)!;

        Assert.False(string.IsNullOrWhiteSpace(a.Country));
        Assert.False(string.IsNullOrWhiteSpace(a.Title));
        Assert.False(string.IsNullOrWhiteSpace(a.Composer));
        Assert.InRange(a.Year, 1500, 1950);
        Assert.False(string.IsNullOrWhiteSpace(a.Status));
        Assert.Matches(@"^\d+/(2|4|8)$", a.Meter);
    }

    [Fact]
    public void Spec_fallback_countries_have_no_melody_and_say_why()
    {
        foreach (var id in SpecFallbacks)
        {
            var a = Anthems.Find(id)!;
            Assert.Null(a.Melody);
            Assert.False(a.HasMelody);
            Assert.StartsWith("Fallback", a.Status);
        }
        Assert.Contains("law", Anthems.Find("mx")!.Status);
    }

    [Theory]
    [MemberData(nameof(Everything))]
    public void An_entry_without_a_melody_is_a_fallback_and_one_with_a_melody_is_public_domain(string id)
    {
        var a = Anthems.Find(id)!;
        if (a.HasMelody)
        {
            Assert.StartsWith("Public domain", a.Status);
            Assert.NotNull(a.Chords);
        }
        else
        {
            Assert.StartsWith("Fallback", a.Status);
            Assert.Null(a.Chords);
        }
    }

    [Fact]
    public void Most_of_the_grid_has_a_melody()
    {
        Assert.True(Anthems.All.Count(a => a.HasMelody) >= 9);
        foreach (var id in new[] { "gb", "fr", "de", "it", "es", "nl", "au", "ca", "fi" }) Assert.True(Anthems.Find(id)!.HasMelody, id);
    }

    [Theory]
    [MemberData(nameof(WithMelody))]
    public void Melodies_sit_in_a_brass_range_and_the_chords_cover_them_exactly(string id)
    {
        var a = Anthems.Find(id)!;
        var notes = a.Melody!.Where(n => n.Midi > 0).ToList();

        Assert.True(notes.Count >= 10, "an opening phrase, not a fragment");
        Assert.All(notes, n => Assert.InRange(n.Midi, 55, 86));
        Assert.All(a.Melody!, n => Assert.True(n.Beats > 0));
        Assert.True(a.Melody![^1].Midi > 0, "the excerpt ends on a note, not a rest");
        Assert.Equal(a.Melody!.Sum(n => n.Beats), a.Chords!.Sum(c => c.Beats), 9);
        Assert.InRange(a.Bpm, 56, 132);
    }

    // ---- Score notation ----------------------------------------------------------------------------------------

    [Fact]
    public void Score_notation_reads_pitches_and_dotted_lengths()
    {
        var notes = Anthems.Melody("3/4", "G4:q G4:q A4:q | F#4:q. G4:e Bb3:q | r:h C5:1");

        Assert.Equal(
            [new Note(67, 1), new Note(67, 1), new Note(69, 1), new Note(66, 1.5), new Note(67, 0.5), new Note(58, 1), new Note(0, 2), new Note(72, 1)],
            notes);
    }

    [Fact]
    public void Score_notation_allows_a_pickup_and_a_short_last_bar_but_checks_every_bar_between()
    {
        Assert.Equal(5, Anthems.Melody("4/4", "D4:q | G4:h G4:h | A4:h G4:q").Count);
        Assert.Throws<FormatException>(() => Anthems.Melody("4/4", "D4:q | G4:h G4:q | A4:h G4:q"));
        Assert.Throws<FormatException>(() => Anthems.Melody("3/4", "G4:h G4:h | A4:h."));
        Assert.Throws<FormatException>(() => Anthems.Melody("4/4", "H4:q"));
        Assert.Throws<FormatException>(() => Anthems.Melody("4/4", "G4:x"));
    }

    [Fact]
    public void Chord_notation_reads_symbols_and_lengths()
    {
        var chords = Anthems.Chords("3/4", "G:q Em:q D:q | D:h D7:q | Am:q G/D:q r:q");

        Assert.Equal(8, chords.Count);
        Assert.Equal(new Chord("G/D", 1), chords[6]);
        Assert.Throws<FormatException>(() => Anthems.Chords("3/4", "H:h."));
        Assert.Throws<FormatException>(() => Anthems.Chords("3/4", "G:h | G:h | G:h"));
    }

    // ---- Rendering ---------------------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(WithMelody))]
    public void Every_anthem_renders_10_to_16_seconds_of_44k_mono(string id)
    {
        var pcm = Render(id);

        Assert.Equal(Rate, pcm.SampleRate);
        Assert.InRange(pcm.Seconds, 10, 16);
        Assert.True(Rms(pcm.Samples) > 0.02, $"{id} is near-silent");
    }

    [Theory]
    [MemberData(nameof(WithMelody))]
    public void Every_anthem_peaks_at_or_below_minus_1_dBFS(string id)
    {
        var pcm = Render(id);

        Assert.True(pcm.Samples.Max(s => Math.Abs((int)s)) <= MaxSample, $"{id} peak {pcm.PeakDbfs:F2} dBFS");
        Assert.True(pcm.PeakDbfs > -6, $"{id} peak {pcm.PeakDbfs:F2} dBFS is too quiet");
    }

    [Theory]
    [MemberData(nameof(WithMelody))]
    public void Every_anthem_starts_and_ends_near_silence(string id)
    {
        var s = Render(id).Samples;

        Assert.True(Math.Abs((int)s[0]) < 0.01 * short.MaxValue, $"{id} starts with a click");
        Assert.True(Math.Abs((int)s[^1]) < 0.01 * short.MaxValue, $"{id} ends with a click");
        var w = Rate / 10;
        Assert.True(Rms(s.AsSpan(s.Length - w)) < 0.1 * Rms(s), $"{id} is cut off rather than faded");
    }

    [Theory]
    [MemberData(nameof(WithMelody))]
    public void Every_anthem_has_no_NaN_dropouts(string id)
    {
        // Non-finite samples become exact zeros in Pcm.FromFloats, so a NaN shows up as a run of digital silence.
        var s = Render(id).Samples;
        var run = 0;
        for (var i = Rate / 5; i < s.Length - Rate / 5; i++)
        {
            run = s[i] == 0 ? run + 1 : 0;
            Assert.True(run < 64, $"{id} has a dropout at {i / (double)Rate:F2} s");
        }
    }

    [Fact]
    public void Anthems_are_deterministic()
    {
        var gb = Anthems.Find("gb")!;
        Assert.Equal(AnthemSynth.Render(gb).Samples, AnthemSynth.Render(gb).Samples);
    }

    [Fact]
    public void A_shorter_limit_ends_the_anthem_early_and_still_fades()
    {
        var pcm = AnthemSynth.Render(Anthems.Find("fr")!, maxSeconds: 8);

        Assert.InRange(pcm.Seconds, 5, 8);
        Assert.True(Math.Abs((int)pcm.Samples[^1]) < 0.01 * short.MaxValue);
        Assert.True(pcm.PeakDbfs <= -1.0);
    }

    [Fact]
    public void A_fallback_anthem_cannot_be_rendered()
    {
        Assert.Throws<ArgumentException>(() => AnthemSynth.Render(Anthems.Find("mx")!));
    }

    [Fact]
    public void The_melody_is_audible_in_the_render()
    {
        // God Save the King opens on G4 (392 Hz) for two beats at 80 BPM after the one-second roll: the strongest
        // spectral line in that window should be G4 or one of its octaves.
        var s = Render("gb").Samples;
        var start = (int)(1.15 * Rate);
        var n = (int)(0.5 * Rate);
        double Power(double hz)
        {
            double re = 0, im = 0;
            for (var i = 0; i < n; i++)
            {
                var w = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / n);
                re += s[start + i] * w * Math.Cos(2 * Math.PI * hz * i / Rate);
                im += s[start + i] * w * Math.Sin(2 * Math.PI * hz * i / Rate);
            }
            return re * re + im * im;
        }

        var g = Power(392);
        foreach (var other in new[] { 370.0, 415.3, 440, 349.2, 329.6 }) Assert.True(g > 4 * Power(other), $"G4 not dominant against {other} Hz");
    }

    // ---- Victory theme -----------------------------------------------------------------------------------------

    [Fact]
    public void Victory_theme_is_about_16_seconds_of_loud_44k_mono()
    {
        var pcm = Theme.Value;

        Assert.Equal(Rate, pcm.SampleRate);
        Assert.InRange(pcm.Seconds, 15, 16.5);
        Assert.True(pcm.PeakDbfs <= -1.0, $"peak {pcm.PeakDbfs:F2} dBFS");
        Assert.True(pcm.PeakDbfs > -4, $"peak {pcm.PeakDbfs:F2} dBFS");
        Assert.True(Rms(pcm.Samples) > 0.06, "the theme should be big");
    }

    [Fact]
    public void Victory_theme_ends_on_a_clean_tail_so_it_can_loop()
    {
        var s = Theme.Value.Samples;
        var whole = Rms(s);

        Assert.True(Math.Abs((int)s[0]) < 0.01 * short.MaxValue, "starts with a click");
        Assert.True(Math.Abs((int)s[^1]) < 0.01 * short.MaxValue, "ends with a click");
        Assert.True(Math.Abs(s[0] - s[^1]) < 0.01 * short.MaxValue, "loop seam clicks");
        // The last 100 ms has died away; the first 100 ms already hits (the theme starts on the downbeat).
        var w = Rate / 10;
        Assert.True(Rms(s.AsSpan(s.Length - w)) < 0.05 * whole, "the tail has not died away");
        Assert.True(Rms(s.AsSpan(0, w)) > 0.5 * whole, "the theme should open on a hit");
    }

    [Fact]
    public void Victory_theme_drives_throughout()
    {
        // No bar of the 15 s body drops out: every 1.5 s window carries at least a third of the average level.
        var s = Theme.Value.Samples;
        var whole = Rms(s);
        var w = (int)(1.5 * Rate);
        for (var start = 0; start + w <= 13 * Rate; start += w / 2)
            Assert.True(Rms(s.AsSpan(start, w)) > 0.33 * whole, $"quiet window at {start / (double)Rate:F1} s");
    }

    [Fact]
    public void Victory_theme_and_sting_are_deterministic()
    {
        Assert.Equal(Theme.Value.Samples, VictoryTheme.Render().Samples);
        Assert.Equal(VictoryTheme.Sting().Samples, VictoryTheme.Sting().Samples);
    }

    [Fact]
    public void Sting_is_a_short_loud_hit_that_starts_and_ends_on_silence()
    {
        var pcm = VictoryTheme.Sting();
        var s = pcm.Samples;

        Assert.Equal(Rate, pcm.SampleRate);
        Assert.InRange(pcm.Seconds, 1.0, 2.5);
        Assert.True(pcm.PeakDbfs <= -1.0 && pcm.PeakDbfs > -6, $"peak {pcm.PeakDbfs:F2} dBFS");
        Assert.True(Math.Abs((int)s[0]) < 0.01 * short.MaxValue);
        Assert.True(Math.Abs((int)s[^1]) < 0.01 * short.MaxValue);
    }
}
