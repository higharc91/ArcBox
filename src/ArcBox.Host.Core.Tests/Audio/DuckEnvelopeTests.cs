using ArcBox.Host.Core.Audio;

namespace ArcBox.Host.Core.Tests.Audio;

public class DuckEnvelopeTests
{
    [Fact]
    public void Ducks_music_twelve_and_sfx_six_db_over_the_200_ms_attack()
    {
        var duck = new DuckEnvelope();
        Assert.Equal(0, duck.MusicDb);

        duck.Step(voiceActive: true, 0.1);
        Assert.Equal(-6, duck.MusicDb, 3);
        Assert.Equal(-3, duck.SfxDb, 3);

        duck.Step(true, 0.1);
        Assert.Equal(-12, duck.MusicDb, 3);
        Assert.Equal(-6, duck.SfxDb, 3);

        duck.Step(true, 5);
        Assert.Equal(DuckEnvelope.MusicDuckDb, duck.MusicDb, 3);
    }

    [Fact]
    public void Releases_over_600_ms()
    {
        var duck = new DuckEnvelope();
        duck.Step(true, 1);

        duck.Step(false, 0.3);
        Assert.Equal(-6, duck.MusicDb, 3);
        duck.Step(false, 0.3);
        Assert.Equal(0, duck.MusicDb, 3);
        duck.Step(false, 1);
        Assert.Equal(0, duck.Amount);
    }

    [Fact]
    public void Levels_convert_to_decibels()
    {
        Assert.Equal(0, AudioLevels.Db(1), 6);
        Assert.Equal(-6.0206, AudioLevels.Db(0.5), 3);
        Assert.Equal(AudioLevels.SilentDb, AudioLevels.Db(0));
        Assert.Equal(AudioLevels.SilentDb, AudioLevels.Db(-1));
        Assert.Equal(0, AudioLevels.Db(3), 6);   // clamped to 1
    }
}
