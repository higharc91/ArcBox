using Tactics.Logic;
using static Tactics.Game.Tests.TacticsGameTests;

namespace Tactics.Game.Tests;

public sealed class ChapterHarnessTests
{
    [Fact]
    public void Development_battle_settings_reach_the_engine_and_persist_the_seed()
    {
        var game = new TacticsGame(Rules, battleList: true) { BattleDifficulty = Difficulty.Story, BattleSeed = 76 };
        Difficulty? difficulty = null;
        game.StartEngine = (setup, rules, seed) => { difficulty = setup.Difficulty; return Battle.Start(setup, rules, seed); };
        var session = new Session(game: game);
        session.ToBattle();
        Assert.Equal(Difficulty.Story, difficulty);
        Assert.Equal(76UL, game.Document.Battle!.Seed);
    }
}
