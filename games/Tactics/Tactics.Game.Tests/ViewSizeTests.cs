using System.Text;
using System.Text.Json;
using ArcBox.Sdk;
using Tactics.Logic;

namespace Tactics.Game.Tests;

/// <summary>
/// The phone's turn choices stay well under the platform's 64 KB custom-view warning (GameSession.CustomViewWarnBytes), at
/// half of it, for every class at level 10 (the largest kits) on the biggest sample encounter, at the start and mid-fight.
/// </summary>
public sealed class ViewSizeTests(ITestOutputHelper output)
{
    private static readonly Lazy<RulesPack> Pack = new(() =>
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "ArcBox.slnx")))
                return RulesPack.Load([Path.Combine(dir.FullName, "content", "tactics", "base", "rules")], ArtManifests.Load(Path.Combine(dir.FullName, "content", "tactics", "base")));
        throw new DirectoryNotFoundException("No ArcBox.slnx above the test binaries.");
    });

    private const int Budget = 32 * 1024;

    [Theory]
    [InlineData("barbarian", "bard", "cleric", "druid", "fighter", "monk")]
    [InlineData("paladin", "ranger", "rogue", "sorcerer", "warlock", "wizard")]
    public void Turn_choices_for_level_10_kits_stay_under_half_the_warning(params string[] classes)
    {
        var rules = Pack.Value;
        PartyMember[] party = [.. classes.Select((c, i) => new PartyMember(new UnitId($"hero{i + 1}"),
            SheetBuilder.DefaultCharacter(c, "human", c, null, 10, rules), new PlayerControlled(new ProfileId($"pr_{c}"))))];
        var state = Battle.Start(Encounters.Setup("crystal_cave", [.. party], Difficulty.Normal, rules), rules, 5).State;
        Measure(state, party, "start");

        // Mid-fight, when foes are in range and area previews list several units: the AI plays everyone to round 3.
        for (var guard = 0; guard < 400 && state.Outcome is null && state.Round < 3; guard++)
            state = PlayTurn(state, rules);
        Measure(state, party, "round 3");
    }

    [Theory]
    [InlineData("wizard")]
    [InlineData("druid")]
    [InlineData("cleric")]
    public void Environmental_level_10_previews_stay_below_the_phone_warning(string cls)
    {
        var rules = Pack.Value;
        var id = new UnitId("hero1");
        PartyMember[] party = [.. Enumerable.Range(1, 4).Select(i => new PartyMember(new UnitId($"hero{i}"),
            SheetBuilder.DefaultCharacter(cls, "human", cls, null, 10, rules), new PlayerControlled(new ProfileId($"pr_test{i}"))))];
        var state = Battle.Start(Encounters.Setup("river_crossing", [.. party], Difficulty.Normal, rules), rules, 5).State;
        state = state with { Environment = true, TurnIndex = state.Order.IndexOf(id) };
        var choices = PhoneChoices.Of(Choices.For(state, id, rules), rules);
        var bytes = Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(choices, ArcJson.Options));
        output.WriteLine($"{cls}: {bytes} bytes with environmental previews");
        Assert.True(bytes < 64 * 1024, $"{cls} environmental choices use {bytes} bytes");
    }

    private void Measure(BattleState state, PartyMember[] party, string when)
    {
        var rules = Pack.Value;
        foreach (var hero in party.Where(h => state.Units.TryGetValue(h.Id, out var u) && u.Life == LifeState.Up && state.Order.Contains(h.Id)))
        {
            var turn = state with { TurnIndex = state.Order.IndexOf(hero.Id) };   // as if it were this hero's turn
            var choices = Choices.For(turn, hero.Id, rules);
            var bytes = Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(PhoneChoices.Of(choices), ArcJson.Options));
            output.WriteLine($"{when}, {hero.Character.Class}: {bytes / 1024.0:0.0} KB, {choices.Abilities.Length} abilities, " +
                             $"{choices.Abilities.Sum(a => a.Targets.Length)} targets, {choices.Moves.Length} moves");
            Assert.True(bytes < Budget, $"{when}: {hero.Character.Class}'s turn choices are {bytes / 1024} KB.");
            var units = turn.Units.Values.Where(DownedRules.InBattle).Select(u => new UnitView(u.Id, u.Sheet.Name, u.Sheet.Side.ToString(),
                u.Sheet.Sprite, u.Pos, u.Hp, u.Sheet.MaxHp, u.TempHp, u.Life.ToString(), u.Countdown, [.. u.Conditions.Select(c => c.Id.Value)], null)).ToArray();
            var packet = JsonSerializer.SerializeToUtf8Bytes(new { me = units.First(u => u.Id == hero.Id), units,
                choices = PhoneChoices.Of(choices, rules), terrain = PhoneTerrain.Of(turn, rules) }, ArcJson.Options);
            Assert.True(packet.Length < 60 * 1024, $"{when}: {hero.Character.Class}'s terrain and turn packet is {packet.Length} bytes (4 KiB reserved for headers/settings).");
        }
    }

    /// <summary>The AI plays the active unit's turn, answering reactions by its own rule.</summary>
    private static BattleState PlayTurn(BattleState state, RulesPack rules)
    {
        var active = state.Active!.Value;
        foreach (var command in Ai.Plan(state, active, rules, new AiOptions(ProfileOverride: "companion")).Commands)
        {
            var resolution = Battle.Apply(state, command, rules);
            if (resolution.Rejected is not null)
                break;
            state = resolution.State;
            while (state.Pending is ReactionOffer offer)
                state = Battle.Apply(state, new React(offer.Reactor, Ai.React(state, offer, rules)), rules).State;
            if (state.Outcome is not null || state.Active != active)
                return state;
        }
        return state.Outcome is null && state.Active == active ? Battle.Apply(state, new EndTurn(active), rules).State : state;
    }
}
