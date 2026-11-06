using Tactics.Logic;

namespace Tactics.Logic.Tests;

/// <summary>
/// The owner's decision 3 (2026-11-03, issue #83): a trap sprung out of battle gives whoever sprang it a minor injury that any camp or inn
/// night mends. The battle table is as it was.
/// </summary>
public sealed class TrapInjuriesTests
{
    private static readonly Lazy<RulesPack> Base = new(() =>
        RulesPack.Load([Path.Combine(ContentPaths.ContentBase, "rules")], ArtManifests.Load(ContentPaths.ContentBase)));

    [Fact]
    public void The_three_minor_injuries_are_rest_kind_mild_and_off_the_battle_table()
    {
        var rules = Base.Value;
        var scraped = rules.Injuries[new("scraped_hands")];
        var singed = rules.Injuries[new("singed")];
        var knee = rules.Injuries[new("twisted_knee")];
        Assert.All(new[] { scraped, singed, knee }, i => Assert.Equal(("rest", false), (i.Mends, i.Battle)));
        Assert.Equal((-1, 0), (scraped.Static.Attack, scraped.Static.Move));
        Assert.Equal((0, 0, 0, 0, 0), (singed.Static.Attack, singed.Static.Move, singed.Static.MaxHp, singed.Static.Ac, singed.Static.Initiative));
        Assert.Null(singed.Hook);
        Assert.Equal((0, -1), (knee.Static.Attack, knee.Static.Move));
        // The battle table is exactly the eight it was: what goes Out in a fight draws nothing new.
        Assert.Equal(["black_eye", "bruised_pride", "concussion", "cracked_ribs", "deep_cut", "shaken", "sprained_ankle", "wrenched_shoulder"],
            rules.Injuries.Values.Where(i => i.Battle).Select(i => i.Id.Value));
    }

    [Fact]
    public void A_trap_with_no_injury_of_its_own_picks_by_weight_among_the_rest_kind_ones_in_id_order()
    {
        var rules = Base.Value;
        var trap = new TrapDef(1);
        Assert.Equal(["black_eye", "scraped_hands", "singed", "twisted_knee"], TrapInjuries.Table(rules).Select(i => i.Id.Value));
        Assert.Equal(3 + 3 + 3 + 2, TrapInjuries.Total(rules));
        string Pick(int roll) => TrapInjuries.Pick(trap, rules, roll)!.Value.Value;
        Assert.Equal("black_eye", Pick(1));
        Assert.Equal("black_eye", Pick(3));
        Assert.Equal("scraped_hands", Pick(4));
        Assert.Equal("singed", Pick(9));
        Assert.Equal("twisted_knee", Pick(10));
        Assert.Equal("twisted_knee", Pick(11));
        Assert.Equal("twisted_knee", Pick(99));       // clamped, never past the table
    }

    [Fact]
    public void Traps_never_duplicate_held_injuries_and_exhausted_tables_give_none()
    {
        var rules = Base.Value;
        InjuryId[] held = [new("singed")];
        Assert.Null(TrapInjuries.Pick(new TrapDef(1, Injury: "singed"), rules, 1, held));
        Assert.DoesNotContain(TrapInjuries.Table(rules, held), i => i.Id == held[0]);
        Assert.Equal(8, TrapInjuries.Total(rules, held));
        var all = TrapInjuries.Table(rules).Select(i => i.Id).ToArray();
        Assert.Equal(0, TrapInjuries.Total(rules, all));
        Assert.Null(TrapInjuries.Pick(new TrapDef(1), rules, 1, all));
    }

    [Fact]
    public void A_trap_that_names_its_injury_gives_that_one_whatever_the_roll()
    {
        var rules = Base.Value;
        Assert.Equal("singed", TrapInjuries.Pick(new TrapDef(1, Injury: "singed"), rules, 1)!.Value.Value);
        Assert.Equal("cracked_ribs", TrapInjuries.Pick(new TrapDef(1, Injury: "cracked_ribs"), rules, 11)!.Value.Value);
    }
}
