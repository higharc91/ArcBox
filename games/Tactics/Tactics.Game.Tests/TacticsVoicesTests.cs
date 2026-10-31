using ArcBox.Sdk.Speech;

namespace Tactics.Game.Tests;

/// <summary>The voices the TV asks for.</summary>
public sealed class TacticsVoicesTests
{
    [Fact]
    public void Each_theme_has_a_valid_id_in_the_games_namespace()
        => Assert.All(TacticsVoices.All, t =>
        {
            Assert.True(VoiceThemes.IsValidId(t.Id));
            Assert.StartsWith($"{TacticsGame.Id}.", t.Id);
        });

    [Fact]
    public void The_two_voices_the_tv_uses_are_both_there()
        => Assert.Equal([TacticsVoices.Narrator, TacticsVoices.Character], TacticsVoices.All.Select(t => t.Id));
}
