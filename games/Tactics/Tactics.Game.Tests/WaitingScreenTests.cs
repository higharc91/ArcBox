using System.Text.Json;
using ArcBox.Sdk;
using Tactics.Logic;
using static Tactics.Game.Tests.TacticsGameTests;

namespace Tactics.Game.Tests;

public sealed class WaitingScreenTests
{
    private static (Session Session, int Player, JsonElement Data) Waiting()
    {
        var s = new Session();
        s.ToBattle();
        for (var player = 0; player < 4; player++)
            if (s.Screen(player) is { Screen: TacticsGame.Screens.Status } status)
                return (s, player, status.Data);
        throw new Xunit.Sdk.XunitException("Nobody is waiting.");
    }

    [Fact]
    public void First_waiting_screen_has_current_party_objective_class_and_read_only_abilities()
    {
        var (s, player, data) = Waiting();
        Assert.Equal(s.Game.Document.Battle!.State.Round, data.GetProperty("round").GetInt32());
        Assert.NotEmpty(data.GetProperty("objective").GetString()!);
        Assert.NotEmpty(data.GetProperty("cls").GetString()!);
        Assert.Equal(4, data.GetProperty("party").GetArrayLength());
        Assert.Contains(data.GetProperty("abilities").EnumerateArray(), a => a.GetProperty("ability").GetString() == "common.attack");
        Assert.All(data.GetProperty("abilities").EnumerateArray(), a =>
        {
            Assert.Empty(a.GetProperty("targets").EnumerateArray());
            Assert.Empty(a.GetProperty("tiles").EnumerateArray());
        });
        var hero = s.Game.Document.Battle.State.Units[TacticsGame.UnitFor(player)];
        foreach (var ability in hero.Sheet.Abilities.Where(a => !Rules.Items.Values.Any(i => i.Use == a)))
            Assert.Contains(data.GetProperty("abilities").EnumerateArray(), a => a.GetProperty("ability").GetString() == ability.Value);
        Assert.True(data.GetProperty("recent").GetArrayLength() <= 3);
        Assert.True(JsonSerializer.SerializeToUtf8Bytes(s.Screen(player), ArcJson.Options).Length < 64 * 1024);
    }

    [Fact]
    public void Waiting_updates_party_hp_and_recent_events_from_completed_commands()
    {
        var (s, player, _) = Waiting();
        for (var step = 0; step < 500; step++)
        {
            for (var p = 0; p < 4; p++)
                if (s.Screen(p) is { } screen)
                {
                    if (screen.Screen == TacticsGame.Screens.Turn) s.Send(p, "endTurn");
                    if (screen.Screen == TacticsGame.Screens.React) s.Send(p, "react", new { choice = "pass" });
                }
            s.Harness.Advance(TimeSpan.FromSeconds(1));
            if (s.Game.Document.Battle is not { } battle) break;
            if (!battle.Log.SelectMany(b => b.Events).OfType<Damaged>().Any()) continue;
            var waiting = Enumerable.Range(0, 4).FirstOrDefault(p => s.Screen(p)?.Screen == TacticsGame.Screens.Status, -1);
            if (waiting < 0) continue;
            var data = s.Screen(waiting)!.Data;
            foreach (var ally in data.GetProperty("party").EnumerateArray())
                Assert.Equal(battle.State.Units[new UnitId(ally.GetProperty("id").GetString()!)].Hp, ally.GetProperty("hp").GetInt32());
            var recent = data.GetProperty("recent").EnumerateArray().Select(e => e.GetString()!).ToArray();
            Assert.InRange(recent.Length, 1, 3);
            Assert.Contains(recent, e => e.Contains("damage", StringComparison.Ordinal));
            return;
        }
        Assert.Fail("No completed damage event reached a waiting phone.");
    }
}
