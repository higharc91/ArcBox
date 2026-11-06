namespace Tactics.Logic.Tests;

/// <summary><see cref="Battle.Hand"/>: the adapter swaps a party member between its player and the companion AI.</summary>
public sealed class ControlTests
{
    private const string Field = """
        0. 0. 0. 0. 0. 0.
        0. 0. 0. 0. 0. 0.
        """;

    private static UnitId U(string id) => new(id);

    private static Tile T(int x, int y) => new(x, y);

    private static string Json(BattleState state) => System.Text.Json.JsonSerializer.Serialize(state, ArcBox.Sdk.ArcJson.Options);

    private static readonly UnitSheet Goblin = TestSheets.Goblin with { MaxHp = 30, InitiativeBonus = 20 };

    private static TestBattle Battle_() => TestBattle.On(Field)
        .Party("kara", TestSheets.Fighter, T(0, 1), new PlayerControlled(new ArcBox.Sdk.ProfileId("p1")))
        .Enemy("gob", Goblin, T(1, 1));

    [Fact]
    public void A_handed_over_member_reacts_by_ai_and_a_returned_one_is_asked_again()
    {
        var battle = Battle_();
        var (state, _) = battle.Start();
        var walk = new Move(U("gob"), [T(1, 1), T(2, 1), T(3, 1)]);

        // Played: the goblin's walk pauses for Kara's opportunity attack.
        Assert.IsType<ReactionOffer>(battle.Apply(state, walk).State.Pending);

        // Handed to the AI: no pause, the AI takes the attack at once.
        var handed = Battle.Hand(state, U("kara"), new AiControlled("companion"));
        var (after, events) = battle.Apply(handed, walk);
        Assert.Null(after.Pending);
        Assert.Contains(events, e => e is Reacted { Reactor.Value: "kara" });

        // Back with the player: asked again.
        var back = Battle.Hand(handed, U("kara"), new PlayerControlled(new ArcBox.Sdk.ProfileId("p1")));
        Assert.IsType<ReactionOffer>(battle.Apply(back, walk).State.Pending);
    }

    [Fact]
    public void A_party_members_summon_changes_hands_but_an_enemys_does_not()
    {
        var (state, _) = Battle_().Start();
        var kara = state.Units[U("kara")];
        var hound = kara with { Id = U("kara.hound.1"), SummonedBy = U("kara"), Pos = T(0, 0) };
        state = state with { Units = state.Units.Add(hound.Id, hound) };

        var ai = new AiControlled("companion");
        var handed = Battle.Hand(state, hound.Id, ai);
        Assert.Equal(ai, handed.Units[hound.Id].Controller);
        var player = new PlayerControlled(new ArcBox.Sdk.ProfileId("p1"));
        Assert.Equal(player, Battle.Hand(handed, hound.Id, player).Units[hound.Id].Controller);

        var imp = state.Units[U("gob")] with { Id = U("gob.imp.1"), SummonedBy = U("gob") };
        state = state with { Units = state.Units.Add(imp.Id, imp) };
        Assert.Throws<ArgumentException>(() => Battle.Hand(state, imp.Id, ai));
    }

    [Fact]
    public void Only_party_members_change_hands_and_nothing_else_changes()
    {
        var (state, _) = Battle_().Start();

        Assert.Throws<ArgumentException>(() => Battle.Hand(state, U("gob"), new PlayerControlled(new ArcBox.Sdk.ProfileId("p1"))));
        Assert.Throws<ArgumentException>(() => Battle.Hand(state, U("nobody"), new AiControlled("companion")));

        var handed = Battle.Hand(state, U("kara"), new AiControlled("companion"));
        var expected = state with { Units = state.Units.SetItem(U("kara"), state.Units[U("kara")] with { Controller = new AiControlled("companion") }) };
        Assert.Equal(Json(expected), Json(handed));
        Assert.Same(handed, Battle.Hand(handed, U("kara"), new AiControlled("companion")));
    }
}
