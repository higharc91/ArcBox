using System.Collections.Immutable;
using Tactics.Game;
using Tactics.Logic;
using Tactics.Presentation.Beats;
using Tactics.Presentation.Audio;
using Tactics.Presentation.Board;
using Tactics.Presentation.Tests.Board;

namespace Tactics.Presentation.Tests.Beats;

/// <summary><see cref="BeatScript"/>: what a beat's events become on screen, and how long that takes.</summary>
public sealed class BeatScriptTests
{
    private static readonly Lazy<FxArt> Fx = new(() => FxArt.Load(Pack.Root));
    private static readonly UnitId Hero = new("hero1");
    private static readonly UnitId Foe = new("goblin.1");
    private static readonly AbilityId Attack = new("common.attack");

    private static UnitView Unit(UnitId id, string side, int x, int y, int hp = 10)
        => new(id, id.Value, side, "characters.class_fighter", new Tile(x, y), hp, 10, 0, "Up", 0, [], null);

    private static BattleView View(params UnitView[] units) => new("e", "m", 1, [], null, [.. units], [], null, BoardChanges.None);

    private static readonly BattleView Near = View(Unit(Hero, "Party", 3, 3), Unit(Foe, "Enemy", 4, 3));
    private static readonly BattleView Far = View(Unit(Hero, "Party", 3, 3), Unit(Foe, "Enemy", 9, 3));

    private static readonly ImmutableArray<UnitId> Targets = [.. Enumerable.Range(1, 5).Select(i => new UnitId($"goblin.{i}"))];
    private static readonly BattleView Crowd = View([Unit(Hero, "Party", 0, 3), .. Targets.Select((t, i) => Unit(t, "Enemy", 11 + i % 2, 2 + i))]);
    private static readonly AbilityUsed Fireball = new(Hero, Attack, new Target(Tile: new Tile(11, 3)), new PresentationDef("characters.cast", "fx.explosion.fire", "projectiles.fireball"));

    private static double LastHit(BeatScript script) => script.Cues.OfType<ApplyCue>().Where(a => a.Event is Damaged).Max(a => a.At);

    private static BeatScript Script(BattleView view, params BattleEvent[] events) => BeatScript.Of(new Beat(1, null, [.. events]), view, view, Fx.Value);

    private static AbilityUsed Used(string? anim, string? fx, string? projectile, UnitId target)
        => new(Hero, Attack, new Target(Unit: target), new PresentationDef(anim, fx, projectile));

    [Theory]
    [InlineData("bosses.air_elemental.gale", "air_elemental.whirlwind", "bosses.special", .4, .8, 16)]
    [InlineData("bosses.air_elemental.gale", "air_elemental.gale_blast", "bosses.cast", .4, .8, 20)]
    [InlineData("bosses.earth_elemental.stone", "earth_elemental.tremor", "bosses.special", .5, 1, 16)]
    [InlineData("bosses.earth_elemental.stone", "golem.stone_aegis", "bosses.buff", .4, .8, 20)]
    [InlineData("bosses.fire_elemental.ember", "fire_elemental.flame_surge", "bosses.special", .4, .8, 16)]
    [InlineData("bosses.fire_elemental.ember", "drake.flame_breath", "bosses.cast", .4, 1, 20)]
    [InlineData("bosses.water_elemental.sea", "water_elemental.tidal_crush", "bosses.special", .4, .8, 16)]
    [InlineData("bosses.dragon.red", "dragon.fire_breath", "bosses.special", .5, 1.2, 16)]
    [InlineData("bosses.dragon.red", "dragon.frightful_roar", "bosses.cast", .5, 1.2, 20)]
    [InlineData("bosses.dragon.red", "dragon.cataclysm", "bosses.buff", .7, 1.4, 24)]
    [InlineData("bosses.air_elemental.storm", "air_elemental.whirlwind", "bosses.special", .4, .8, 16)]
    [InlineData("bosses.air_elemental.storm", "air_elemental.gale_blast", "bosses.cast", .4, .8, 20)]
    [InlineData("bosses.earth_elemental.magma", "earth_elemental.tremor", "bosses.special", .5, 1, 16)]
    [InlineData("bosses.earth_elemental.magma", "golem.stone_aegis", "bosses.buff", .4, .8, 20)]
    [InlineData("bosses.water_elemental.swamp", "water_elemental.tidal_crush", "bosses.special", .4, .8, 16)]
    [InlineData("bosses.golem.basalt", "golem.seismic_slam", "bosses.special", .5, 1, 16)]
    [InlineData("bosses.golem.basalt", "golem.stone_aegis", "bosses.buff", .4, .8, 20)]
    [InlineData("bosses.golem.crystal", "golem.seismic_slam", "bosses.special", .5, 1, 16)]
    [InlineData("bosses.golem.crystal", "golem.stone_aegis", "bosses.buff", .4, .8, 20)]
    [InlineData("bosses.golem.temple", "golem.seismic_slam", "bosses.special", .5, 1, 16)]
    [InlineData("bosses.golem.temple", "golem.stone_aegis", "bosses.buff", .4, .8, 20)]
    [InlineData("bosses.fire_elemental.azure", "fire_elemental.flame_surge", "bosses.special", .4, .8, 16)]
    [InlineData("bosses.fire_elemental.azure", "drake.flame_breath", "bosses.cast", .4, 1, 20)]
    [InlineData("bosses.fire_elemental.radiant", "fire_elemental.flame_surge", "bosses.special", .4, .8, 16)]
    [InlineData("bosses.fire_elemental.radiant", "drake.flame_breath", "bosses.cast", .4, 1, 20)]
    [InlineData("bosses.fire_elemental.shadow", "fire_elemental.flame_surge", "bosses.special", .4, .8, 16)]
    [InlineData("bosses.fire_elemental.shadow", "drake.flame_breath", "bosses.cast", .4, 1, 20)]
    public void Elemental_effects_and_damage_wait_for_their_distinct_authored_contact_frame(string key, string ability, string binding, double impact, double duration, int contact)
    {
        var rules = RulesPack.Load([Path.Combine(Pack.Root, "rules")]);
        var def = rules.Abilities[new(ability)];
        var view = Near with { Units = [.. Near.Units.Select(u => u.Id == Hero ? u with { Sprite = key } : u)] };
        var used = new AbilityUsed(Hero, new(ability), new Target(Unit: Hero), def.Presentation);
        var script = Script(view, used, new Damaged(Foe, 4, "fire", 6));
        var pose = Assert.Single(script.Cues.OfType<PoseCue>(), p => p.Unit == Hero);
        var burst = Assert.Single(script.Cues.OfType<BurstCue>(), b => b.Tile == view.Units[0].At);
        var anim = UnitArt.Load(Pack.Root).Sprite(key)!.Anim(pose.Anim);
        Assert.Equal(binding, pose.Anim);
        Assert.Equal(duration, pose.Length, 3);
        Assert.True(pose.Length >= anim.Frames.Length / anim.Fps);
        Assert.Equal(impact, burst.At - pose.At, 3);
        Assert.Equal(impact, LastHit(script) - pose.At, 3);
        Assert.Equal(contact, anim.FrameAt(impact));
        if (key is "bosses.water_elemental.sea" or "bosses.dragon.red" or "bosses.air_elemental.storm" or "bosses.earth_elemental.magma" or "bosses.water_elemental.swamp" or "bosses.golem.basalt" or "bosses.golem.crystal" or "bosses.golem.temple" or "bosses.fire_elemental.azure" or "bosses.fire_elemental.radiant" or "bosses.fire_elemental.shadow")
            Assert.Equal(pose.At + impact, Assert.Single(script.Cues.OfType<MomentCue>(), c => c.Label == "special-contact").At, 3);
        Assert.Equal(Fx.Value.Clip(def.Presentation!.Fx!)!.Path, burst.Clip.Path);
        Assert.Equal(ability == "dragon.fire_breath" ? 42 : ability == "drake.flame_breath" ? 30 : Fx.Value.Clip(def.Presentation.Fx!)!.Lift, burst.Clip.Lift);
        Assert.Equal(view.Units[0].At, pose.From);
        Assert.Equal(view.Units[1].At, pose.Toward);
        if (burst.Clip.Aimed) Assert.Equal(view.Units[1].At, burst.Toward);
        else Assert.Null(burst.Toward);
        Assert.True(script.Length >= pose.At + pose.Length);
    }

    [Theory]
    [InlineData("bosses.dragon.blue", "dragon.fire_breath")]
    public void Legacy_elemental_variants_keep_their_existing_visual_bindings_and_effect_timing(string key, string ability)
    {
        var rules = RulesPack.Load([Path.Combine(Pack.Root, "rules")]);
        var def = rules.Abilities[new(ability)];
        var view = Near with { Units = [.. Near.Units.Select(u => u.Id == Hero ? u with { Sprite = key } : u)] };
        var script = Script(view, new AbilityUsed(Hero, new(ability), new Target(Unit: Hero), def.Presentation));
        Assert.Equal(def.Presentation!.Anim, Assert.Single(script.Cues.OfType<PoseCue>()).Anim);
        Assert.Equal(0, Assert.Single(script.Cues.OfType<BurstCue>()).At);
    }

    [Theory]
    [InlineData("bosses.air_elemental.gale")]
    [InlineData("bosses.earth_elemental.stone")]
    [InlineData("bosses.fire_elemental.ember")]
    [InlineData("bosses.water_elemental.sea")]
    [InlineData("bosses.dragon.red")]
    [InlineData("bosses.air_elemental.storm")]
    [InlineData("bosses.earth_elemental.magma")]
    [InlineData("bosses.water_elemental.swamp")]
    [InlineData("bosses.golem.basalt")]
    [InlineData("bosses.golem.crystal")]
    [InlineData("bosses.golem.temple")]
    [InlineData("bosses.fire_elemental.azure")]
    [InlineData("bosses.fire_elemental.radiant")]
    [InlineData("bosses.fire_elemental.shadow")]
    public void Elemental_natural_attacks_contact_with_connected_fists_and_finish_their_recovery(string key)
    {
        var view = Near with { Units = [.. Near.Units.Select(u => u.Id == Hero ? u with { Sprite = key } : u)] };
        var script = Script(view, new AbilityUsed(Hero, Attack, new Target(Unit: Foe), new("characters.attack", null, null)),
            new AttackResolved(Hero, Foe, true, false), new Damaged(Foe, 4, "bludgeoning", 6));
        var lunge = Assert.Single(script.Cues.OfType<LungeCue>());
        var anim = UnitArt.Load(Pack.Root).Sprite(key)!.Anim(lunge.Anim);
        Assert.Equal(10, anim.FrameAt(LastHit(script) - lunge.At));
        Assert.True(lunge.Length >= anim.Frames.Length / anim.Fps);
        Assert.True(script.Length >= lunge.At + lunge.Length);
        Assert.Empty(script.Cues.OfType<FlightCue>());
    }

    [Theory]
    [InlineData("crypt-kin")]
    [InlineData("web-and-hunt")]
    public void Library_variant_replay_keeps_targets_stable_and_all_natural_attacks_adjacent(string batch)
    {
        var steps = LibraryVariantDemo.Steps(Pack.Root, batch);
        Assert.Equal(11, steps.Count);
        var battle = steps[0].View.Battle!;
        var terrain = new BoardTerrain(Pack.Map(battle.Map), battle.Board);
        var rules = RulesPack.Load([Path.Combine(Pack.Root, "rules")]);
        var moves = 0;
        var attacks = 0;
        BattleView? previous = null;
        foreach (var step in steps)
        {
            var current = step.View.Battle!;
            Assert.All(current.Units, u => Assert.False(terrain.Map[u.At].Flags.HasFlag(TileFlags.Impassable)));
            foreach (var walk in current.Beats.SelectMany(b => b.Events).OfType<Moved>())
            {
                moves++;
                Assert.Equal(walk.Path[0], walk.Path[^1]);
                Assert.All(walk.Path, t => Assert.False(terrain.Map[t].Flags.HasFlag(TileFlags.Impassable)));
            }
            foreach (var used in current.Beats.SelectMany(b => b.Events).OfType<AbilityUsed>())
            {
                attacks++;
                Assert.NotNull(previous);
                Assert.Equal(previous.Units.Select(u => u.At), current.Units.Select(u => u.At));
                Assert.Equal("common.attack", used.Ability.Value);
                Assert.Equal(rules.Abilities[used.Ability].Presentation, used.Presentation);
                var from = Assert.Single(current.Units, u => u.Id == used.Source).At;
                var to = Assert.Single(current.Units, u => u.Id == used.Target.Unit).At;
                Assert.Equal(1, Math.Max(Math.Abs(from.X-to.X), Math.Abs(from.Y-to.Y)));
            }
            previous = current;
        }
        Assert.Equal(3, moves);
        Assert.Equal(3, attacks);
    }

    [Fact]
    public void Small_library_companions_replay_round_trips_bites_and_real_ranged_hellfire()
    {
        var steps = NightCompanionsDemo.Steps(Pack.Root);
        Assert.Equal(11, steps.Count);
        var battle = steps[0].View.Battle!;
        var terrain = new BoardTerrain(Pack.Map(battle.Map), battle.Board);
        var rules = RulesPack.Load([Path.Combine(Pack.Root, "rules")]);
        var moves = 0;
        var attacks = 0;
        BattleView? previous = null;
        foreach (var step in steps)
        {
            var current = step.View.Battle!;
            Assert.All(current.Units, u => Assert.False(terrain.Map[u.At].Flags.HasFlag(TileFlags.Impassable)));
            foreach (var walk in current.Beats.SelectMany(b => b.Events).OfType<Moved>())
            {
                moves++;
                Assert.Equal(walk.Path[0], walk.Path[^1]);
                Assert.All(walk.Path, t => Assert.False(terrain.Map[t].Flags.HasFlag(TileFlags.Impassable)));
            }
            foreach (var used in current.Beats.SelectMany(b => b.Events).OfType<AbilityUsed>())
            {
                attacks++;
                Assert.NotNull(previous);
                Assert.Equal(previous.Units.Select(u => u.At), current.Units.Select(u => u.At));
                Assert.Equal("common.attack", used.Ability.Value);
                Assert.Equal(rules.Abilities[used.Ability].Presentation, used.Presentation);
                var from = Assert.Single(current.Units, u => u.Id == used.Source).At;
                var to = Assert.Single(current.Units, u => u.Id == used.Target.Unit).At;
                Assert.Equal(used.Source.Value == "Violet" ? 4 : 1, Math.Max(Math.Abs(from.X-to.X), Math.Abs(from.Y-to.Y)));
            }
            previous = current;
        }
        Assert.Equal(3, moves);
        Assert.Equal(3, attacks);
    }

    [Fact]
    public void Library_elemental_replay_uses_all_six_real_specials_and_walkable_adjacent_physical_targets()
    {
        var steps = WildElementsDemo.Steps(Pack.Root);
        Assert.Equal(20, steps.Count);
        var rules = RulesPack.Load([Path.Combine(Pack.Root, "rules")]);
        var battle = steps[0].View.Battle!;
        var terrain = new BoardTerrain(Pack.Map(battle.Map), battle.Board);
        var used = new List<string>();
        BattleView? previous = null;
        foreach (var step in steps)
        {
            var current = step.View.Battle!;
            Assert.All(current.Units, u => Assert.False(terrain.Map[u.At].Flags.HasFlag(TileFlags.Impassable)));
            foreach (var moved in current.Beats.SelectMany(b => b.Events).OfType<Moved>())
            {
                Assert.Equal(moved.Path[0], moved.Path[^1]);
                Assert.All(moved.Path, tile => Assert.False(terrain.Map[tile].Flags.HasFlag(TileFlags.Impassable)));
            }
            foreach (var move in current.Beats.SelectMany(b => b.Events).OfType<AbilityUsed>())
            {
                Assert.Equal(rules.Abilities[move.Ability].Presentation, move.Presentation);
                Assert.NotNull(previous);
                Assert.Equal(previous.Units.Select(u => u.At), current.Units.Select(u => u.At));
                if (move.Ability.Value == "common.attack")
                {
                    var from = Assert.Single(current.Units, u => u.Id == move.Source).At;
                    var to = Assert.Single(current.Units, u => u.Id == move.Target.Unit).At;
                    Assert.Equal(1, Math.Max(Math.Abs(from.X-to.X),Math.Abs(from.Y-to.Y)));
                }
                else used.Add(move.Ability.Value);
            }
            previous = current;
        }
        Assert.Equal(6, used.Distinct().Count());
    }

    [Fact]
    public void Library_golem_replay_uses_all_six_real_specials_and_walkable_adjacent_physical_targets()
    {
        var steps = StoneSentinelsDemo.Steps(Pack.Root);
        Assert.Equal(20, steps.Count);
        var rules = RulesPack.Load([Path.Combine(Pack.Root, "rules")]);
        var battle = steps[0].View.Battle!;
        var terrain = new BoardTerrain(Pack.Map(battle.Map), battle.Board);
        var used = new List<string>();
        BattleView? previous = null;
        foreach (var step in steps)
        {
            var current = step.View.Battle!;
            Assert.All(current.Units, u => Assert.False(terrain.Map[u.At].Flags.HasFlag(TileFlags.Impassable)));
            foreach (var moved in current.Beats.SelectMany(b => b.Events).OfType<Moved>())
            {
                Assert.Equal(moved.Path[0], moved.Path[^1]);
                Assert.All(moved.Path, tile => Assert.False(terrain.Map[tile].Flags.HasFlag(TileFlags.Impassable)));
            }
            foreach (var move in current.Beats.SelectMany(b => b.Events).OfType<AbilityUsed>())
            {
                Assert.Equal(rules.Abilities[move.Ability].Presentation, move.Presentation);
                Assert.NotNull(previous);
                Assert.Equal(previous.Units.Select(u => u.At), current.Units.Select(u => u.At));
                if (move.Ability.Value == "common.attack")
                {
                    var from = Assert.Single(current.Units, u => u.Id == move.Source).At;
                    var to = Assert.Single(current.Units, u => u.Id == move.Target.Unit).At;
                    Assert.Equal(1, Math.Max(Math.Abs(from.X-to.X),Math.Abs(from.Y-to.Y)));
                }
                else used.Add(move.Ability.Value);
            }
            previous = current;
        }
        Assert.Equal(6, used.Count);
        Assert.Equal(3, used.Count(a => a == "golem.seismic_slam"));
        Assert.Equal(3, used.Count(a => a == "golem.stone_aegis"));
    }

    [Fact]
    public void Library_fire_replay_uses_all_six_real_specials_and_walkable_adjacent_physical_targets()
    {
        var steps = PrismaticFlamesDemo.Steps(Pack.Root);
        Assert.Equal(20, steps.Count);
        var rules = RulesPack.Load([Path.Combine(Pack.Root, "rules")]);
        var battle = steps[0].View.Battle!;
        var terrain = new BoardTerrain(Pack.Map(battle.Map), battle.Board);
        var used = new List<string>();
        BattleView? previous = null;
        foreach (var step in steps)
        {
            var current = step.View.Battle!;
            Assert.All(current.Units, u => Assert.False(terrain.Map[u.At].Flags.HasFlag(TileFlags.Impassable)));
            foreach (var moved in current.Beats.SelectMany(b => b.Events).OfType<Moved>())
            {
                Assert.Equal(moved.Path[0], moved.Path[^1]);
                Assert.All(moved.Path, tile => Assert.False(terrain.Map[tile].Flags.HasFlag(TileFlags.Impassable)));
            }
            foreach (var move in current.Beats.SelectMany(b => b.Events).OfType<AbilityUsed>())
            {
                Assert.Equal(rules.Abilities[move.Ability].Presentation, move.Presentation);
                Assert.NotNull(previous);
                Assert.Equal(previous.Units.Select(u => u.At), current.Units.Select(u => u.At));
                if (move.Ability.Value == "common.attack")
                {
                    var from = Assert.Single(current.Units, u => u.Id == move.Source).At;
                    var to = Assert.Single(current.Units, u => u.Id == move.Target.Unit).At;
                    Assert.Equal(1, Math.Max(Math.Abs(from.X-to.X),Math.Abs(from.Y-to.Y)));
                }
                else used.Add(move.Ability.Value);
            }
            previous = current;
        }
        Assert.Equal(6, used.Count);
        Assert.Equal(3, used.Count(a => a == "fire_elemental.flame_surge"));
        Assert.Equal(3, used.Count(a => a == "drake.flame_breath"));
    }

    [Theory]
    [InlineData("bosses.water_elemental.sea")]
    [InlineData("bosses.water_elemental.swamp")]
    public void Undertow_pulls_with_a_directed_gesture_and_target_effect_at_contact(string key)
    {
        var def = RulesPack.Load([Path.Combine(Pack.Root, "rules")]).Abilities[new("water_elemental.undertow")];
        var view = Near with { Units = [.. Near.Units.Select(u => u.Id == Hero ? u with { Sprite = key } : u)] };
        var script = Script(view, new AbilityUsed(Hero, new("water_elemental.undertow"), new Target(Unit: Foe), def.Presentation));
        var pose = Assert.Single(script.Cues.OfType<PoseCue>());
        var burst = Assert.Single(script.Cues.OfType<BurstCue>());
        Assert.Equal("bosses.cast", pose.Anim);
        Assert.Equal(view.Units[1].At, pose.Toward);
        Assert.Equal(view.Units[1].At, burst.Tile);
        Assert.Equal(.4, burst.At, 3);
        Assert.Equal(.4, Assert.Single(script.Cues.OfType<MomentCue>(), c => c.Label == "special-contact").At, 3);
        Assert.True(script.Length >= .8);
        Assert.Empty(script.Cues.OfType<LungeCue>());
        Assert.Empty(script.Cues.OfType<FlightCue>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Dragon_jaws_remain_a_two_tile_physical_attack_but_explicit_projectiles_win(bool explicitProjectile)
    {
        var view = Near with { Units = [.. Near.Units.Select(u => u.Id == Hero ? u with { Sprite = "bosses.dragon.red" } : u with { At = new Tile(5,3) })] };
        var script = Script(view, new AbilityUsed(Hero, Attack, new Target(Unit: Foe), new("characters.attack", null, explicitProjectile ? "projectiles.arrow" : null)),
            new AttackResolved(Hero,Foe,true,false), new Damaged(Foe,4,"piercing",6));
        if (explicitProjectile)
        {
            Assert.Single(script.Cues.OfType<FlightCue>());
            Assert.Empty(script.Cues.OfType<LungeCue>());
        }
        else
        {
            var bite = Assert.Single(script.Cues.OfType<LungeCue>());
            Assert.Equal(1, bite.Length, 3);
            Assert.Equal(.5, LastHit(script) - bite.At, 3);
            Assert.True(script.Length >= bite.At + bite.Length);
            Assert.Empty(script.Cues.OfType<FlightCue>());
        }
    }

    [Fact]
    public void Elemental_replay_uses_all_six_real_specials_and_walkable_adjacent_physical_targets()
    {
        var steps = ElementalTriadDemo.Steps(Pack.Root);
        Assert.Equal(20, steps.Count);
        var rules = RulesPack.Load([Path.Combine(Pack.Root, "rules")]);
        var battle = steps[0].View.Battle!;
        var terrain = new BoardTerrain(Pack.Map(battle.Map), battle.Board);
        var used = new List<string>();
        BattleView? previous = null;
        foreach (var step in steps)
        {
            var current = step.View.Battle!;
            Assert.All(current.Units, u => Assert.False(terrain.Map[u.At].Flags.HasFlag(TileFlags.Impassable)));
            foreach (var moved in current.Beats.SelectMany(b => b.Events).OfType<Moved>())
            {
                Assert.Equal(moved.Path[0], moved.Path[^1]);
                Assert.All(moved.Path, tile => Assert.False(terrain.Map[tile].Flags.HasFlag(TileFlags.Impassable)));
            }
            foreach (var move in current.Beats.SelectMany(b => b.Events).OfType<AbilityUsed>())
            {
                Assert.Equal(rules.Abilities[move.Ability].Presentation, move.Presentation);
                Assert.NotNull(previous);
                Assert.Equal(previous.Units.Select(u => u.At), current.Units.Select(u => u.At));
                if (move.Ability.Value == "common.attack")
                {
                    var from = Assert.Single(current.Units, u => u.Id == move.Source).At;
                    var to = Assert.Single(current.Units, u => u.Id == move.Target.Unit).At;
                    Assert.Equal(1, Math.Max(Math.Abs(from.X-to.X),Math.Abs(from.Y-to.Y)));
                }
                else used.Add(move.Ability.Value);
            }
            previous = current;
        }
        Assert.Equal(6, used.Distinct().Count());
    }

    [Fact]
    public void Tide_and_wyrm_replay_uses_five_real_specials_and_stable_walkable_targets()
    {
        var steps = TideAndWyrmDemo.Steps(Pack.Root);
        Assert.Equal(15, steps.Count);
        var rules = RulesPack.Load([Path.Combine(Pack.Root,"rules")]);
        var board = steps[0].View.Battle!;
        var terrain = new BoardTerrain(Pack.Map(board.Map),board.Board);
        BattleView? previous = null;
        var used = new List<string>();
        foreach (var step in steps)
        {
            var current = step.View.Battle!;
            Assert.All(current.Units,u=>Assert.False(terrain.Map[u.At].Flags.HasFlag(TileFlags.Impassable)));
            foreach(var moved in current.Beats.SelectMany(b=>b.Events).OfType<Moved>())
            {
                Assert.Equal(moved.Path[0],moved.Path[^1]);
                Assert.All(moved.Path,t=>Assert.False(terrain.Map[t].Flags.HasFlag(TileFlags.Impassable)));
            }
            foreach(var move in current.Beats.SelectMany(b=>b.Events).OfType<AbilityUsed>())
            {
                Assert.NotNull(previous);
                Assert.Equal(previous.Units.Select(u=>u.At),current.Units.Select(u=>u.At));
                Assert.Equal(rules.Abilities[move.Ability].Presentation,move.Presentation);
                if(move.Ability.Value=="common.attack")
                {
                    var from=Assert.Single(current.Units,u=>u.Id==move.Source).At;
                    var to=Assert.Single(current.Units,u=>u.Id==move.Target.Unit).At;
                    Assert.Equal(move.Source.Value=="Dragon"?2:1,Math.Max(Math.Abs(from.X-to.X),Math.Abs(from.Y-to.Y)));
                }
                else used.Add(move.Ability.Value);
            }
            previous=current;
        }
        Assert.Equal(5,used.Distinct().Count());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Sapling_lashes_are_complete_physical_strikes_at_close_and_reach_distance(int range)
    {
        var view = Near with { Units = [.. Near.Units.Select(u => u.Id == Hero ? u with { Sprite = "bosses.sapling.spring", At = new Tile(3, 3) } : u with { At = new Tile(3 + range, 3) })] };
        var used = new AbilityUsed(Hero, Attack, new Target(Unit: Foe), new("characters.attack", null, null));
        var script = Script(view, used, new AttackResolved(Hero, Foe, true, false), new Damaged(Foe, 4, "bludgeoning", 6));
        var lunge = Assert.Single(script.Cues.OfType<LungeCue>());
        var anim = UnitArt.Load(Pack.Root).Sprite("bosses.sapling.spring")!.Anim(lunge.Anim);
        Assert.Equal(10, anim.FrameAt(LastHit(script) - lunge.At));
        Assert.True(lunge.Length >= anim.Frames.Length / anim.Fps);
        Assert.True(script.Length >= lunge.At + lunge.Length);
        Assert.Empty(script.Cues.OfType<FlightCue>());
    }

    [Fact]
    public void A_saplings_explicit_projectile_override_is_preserved()
    {
        var view = Far with { Units = [.. Far.Units.Select(u => u.Id == Hero ? u with { Sprite = "bosses.sapling.spring" } : u)] };
        var used = new AbilityUsed(Hero, Attack, new Target(Unit: Foe), new("characters.shoot", null, "projectiles.arrow"));
        Assert.Equal("projectiles/arrow.png", Assert.Single(Script(view, used).Cues.OfType<FlightCue>()).Art.Path);
    }

    [Fact]
    public void Grove_ward_replay_uses_a_walkable_round_trip_and_unmodified_common_attack()
    {
        var steps = GroveWardDemo.Steps(Pack.Root);
        Assert.Equal(4, steps.Count);
        var battle = steps[0].View.Battle!;
        var terrain = new BoardTerrain(Pack.Map(battle.Map), battle.Board);
        var walk = Assert.Single(steps[1].View.Battle!.Beats.SelectMany(b => b.Events).OfType<Moved>());
        Assert.Equal(walk.Path[0], walk.Path[^1]);
        Assert.All(walk.Path, tile => Assert.False(terrain.Map[tile].Flags.HasFlag(TileFlags.Impassable)));
        var used = Assert.Single(steps[2].View.Battle!.Beats.SelectMany(b => b.Events).OfType<AbilityUsed>());
        Assert.Equal("characters.attack", used.Presentation!.Anim);
        Assert.Null(used.Presentation.Projectile);
        Assert.Null(used.Presentation.Fx);
    }

    [Theory]
    [InlineData("bosses.imp.crimson", 1)]
    [InlineData("bosses.imp.crimson", 4)]
    [InlineData("bosses.imp.violet", 1)]
    [InlineData("bosses.imp.violet", 4)]
    public void Bound_imp_natural_attacks_launch_hellfire_from_the_hand_release_instead_of_arrows(string key, int range)
    {
        var view = Near with { Units = [.. Near.Units.Select(u => u.Id == Hero ? u with { Sprite = key, At = new Tile(3, 3) } : u with { At = new Tile(3 + range, 3) })] };
        var used = new AbilityUsed(Hero, Attack, new Target(Unit: Foe), new("characters.attack", null, null));
        var script = Script(view, used, new AttackResolved(Hero, Foe, true, false), new Damaged(Foe, 4, "fire", 6));
        var pose = Assert.Single(script.Cues.OfType<PoseCue>(), p => p.Unit == Hero);
        var flight = Assert.Single(script.Cues.OfType<FlightCue>());
        Assert.Equal("projectiles/fireball.png", flight.Art.Path);
        Assert.Equal("bosses.attack", pose.Anim);
        Assert.Equal(.4, flight.At - pose.At, 3);
        var anim = UnitArt.Load(Pack.Root).Sprite(key)!.Anim(pose.Anim);
        Assert.Equal(10, anim.FrameAt(flight.At - pose.At));
        Assert.True(pose.Length >= anim.Frames.Length / anim.Fps);
        Assert.True(script.Length >= pose.At + pose.Length);
        Assert.Empty(script.Cues.OfType<LungeCue>());
    }

    [Theory]
    [InlineData("bosses.imp.crimson")]
    [InlineData("bosses.imp.violet")]
    public void An_explicit_imp_projectile_is_not_replaced_by_the_basic_hellfire_inference(string key)
    {
        var view = Far with { Units = [.. Far.Units.Select(u => u.Id == Hero ? u with { Sprite = key } : u)] };
        var used = new AbilityUsed(Hero, Attack, new Target(Unit: Foe), new("characters.shoot", null, "projectiles.arrow"));
        var script = Script(view, used);
        Assert.Equal("projectiles/arrow.png", Assert.Single(script.Cues.OfType<FlightCue>()).Art.Path);
    }

    [Fact]
    public void Ember_familiar_replay_uses_a_walkable_round_trip_and_the_real_common_attack_binding()
    {
        var steps = EmberFamiliarDemo.Steps(Pack.Root);
        Assert.Equal(4, steps.Count);
        var battle = steps[0].View.Battle!;
        var terrain = new BoardTerrain(Pack.Map(battle.Map), battle.Board);
        var walk = Assert.Single(steps[1].View.Battle!.Beats.SelectMany(b => b.Events).OfType<Moved>());
        Assert.Equal(walk.Path[0], walk.Path[^1]);
        Assert.All(walk.Path, tile => Assert.False(terrain.Map[tile].Flags.HasFlag(TileFlags.Impassable)));
        var used = Assert.Single(steps[2].View.Battle!.Beats.SelectMany(b => b.Events).OfType<AbilityUsed>());
        Assert.Equal("common.attack", used.Ability.Value);
        Assert.Equal("characters.attack", used.Presentation!.Anim);
        Assert.Null(used.Presentation.Projectile);
    }

    [Theory]
    [InlineData("bosses.hound.tan")]
    [InlineData("bosses.hound.black")]
    [InlineData("bosses.hound.grey")]
    [InlineData("beasts.bat.vampire")]
    [InlineData("beasts.bear.undead")]
    [InlineData("beasts.slime.crystal")]
    [InlineData("beasts.spider.phase")]
    [InlineData("beasts.wolf.alpha")]
    [InlineData("bestiary.crab.deep")]
    public void Companion_bite_finishes_its_complete_clip_and_contacts_on_the_forward_jaw_pose(string key)
    {
        var view = Near with { Units = [.. Near.Units.Select(u => u.Id == Hero ? u with { Sprite = key } : u)] };
        var used = new AbilityUsed(Hero, Attack, new Target(Unit: Foe), null);
        var script = Script(view, used, new AttackResolved(Hero, Foe, true, false), new Damaged(Foe, 4, "piercing", 6));
        var lunge = Assert.Single(script.Cues.OfType<LungeCue>());
        var anim = UnitArt.Load(Pack.Root).Sprite(key)!.Anim(lunge.Anim);
        Assert.True(lunge.Length >= anim.Frames.Length / anim.Fps);
        Assert.Equal(10, anim.FrameAt(LastHit(script) - lunge.At));
        Assert.True(script.Length >= lunge.At + lunge.Length);
    }

    [Fact]
    public void Companion_replay_uses_a_walkable_round_trip_and_the_unmodified_natural_attack()
    {
        var steps = TrailCompanionDemo.Steps();
        Assert.Equal(4, steps.Count);
        var battle = steps[0].View.Battle!;
        var terrain = new BoardTerrain(Pack.Map(battle.Map), battle.Board);
        var walk = Assert.Single(steps[1].View.Battle!.Beats.SelectMany(b => b.Events).OfType<Moved>());
        Assert.Equal(walk.Path[0], walk.Path[^1]);
        Assert.All(walk.Path, tile => Assert.False(terrain.Map[tile].Flags.HasFlag(TileFlags.Impassable)));
        var used = Assert.Single(steps[2].View.Battle!.Beats.SelectMany(b => b.Events).OfType<AbilityUsed>());
        Assert.Equal("common.attack", used.Ability.Value);
        Assert.Null(used.Presentation);
    }

    [Fact]
    public void Oak_ambush_replay_uses_a_walkable_skitter_and_the_real_adhesive_binding()
    {
        var steps = OakAmbushDemo.Steps(Pack.Root);
        Assert.Equal(5, steps.Count);
        var walk = Assert.Single(steps[1].View.Battle!.Beats.SelectMany(b => b.Events).OfType<Moved>());
        var battle = steps[0].View.Battle!;
        var terrain = new BoardTerrain(Pack.Map(battle.Map), battle.Board);
        Assert.All(walk.Path, tile => Assert.False(terrain.Map[tile].Flags.HasFlag(TileFlags.Impassable)));
        var used = Assert.Single(steps[3].View.Battle!.Beats.SelectMany(b => b.Events).OfType<AbilityUsed>());
        Assert.Equal("mimic.adhesive_bite", used.Ability.Value);
        Assert.Equal("bosses.attack", used.Presentation!.Anim);
        Assert.Equal("fx.hit", used.Presentation.Fx);
    }

    [Theory]
    [InlineData("common.attack", null)]
    [InlineData("mimic.adhesive_bite", "fx.hit")]
    public void Mimic_bites_keep_the_hinged_wind_up_tongue_contact_and_recovery(string ability, string? fx)
    {
        var view = Near with { Units = [.. Near.Units.Select(u => u.Id == Hero ? u with { Sprite = "bosses.mimic.oak" } : u)] };
        var used = new AbilityUsed(Hero, new(ability), new Target(Unit: Foe), new("bosses.attack", fx, null));
        var script = Script(view, used, new AttackResolved(Hero, Foe, true, false), new Damaged(Foe, 4, "piercing", 6));
        var lunge = Assert.Single(script.Cues.OfType<LungeCue>());
        var anim = UnitArt.Load(Pack.Root).Sprite("bosses.mimic.oak")!.Anim(lunge.Anim);
        Assert.True(lunge.Length >= anim.Frames.Length / anim.Fps);
        Assert.Equal(10, anim.FrameAt(LastHit(script) - lunge.At));
        Assert.True(script.Length >= lunge.At + lunge.Length);
    }

    [Fact]
    public void Giant_review_replays_the_four_real_ability_presentations_through_the_beat_path()
    {
        var steps = StoneAndMireDemo.Steps(Pack.Root);
        Assert.Equal(5, steps.Count);
        var used = steps.Skip(1).SelectMany(s => s.View.Battle!.Beats).SelectMany(b => b.Events).OfType<AbilityUsed>().ToArray();
        Assert.Equal(new[] { "troll.boulder_toss", "troll.frenzy_roar", "golem.seismic_slam", "golem.stone_aegis" }, used.Select(u => u.Ability.Value));
        Assert.Equal(new[] { "bosses.special", "bosses.buff", "bosses.special", "bosses.buff" }, used.Select(u => u.Presentation!.Anim));
        foreach (var step in steps)
        {
            var battle = step.View.Battle!;
            var terrain = new BoardTerrain(Pack.Map(battle.Map), battle.Board);
            Assert.Empty(battle.Units.Where(u => terrain.Map[u.At].Flags.HasFlag(TileFlags.Impassable)));
        }
    }

    [Theory]
    [InlineData("bosses.troll.troll")]
    [InlineData("bosses.golem.granite")]
    public void Giant_melee_keeps_its_wind_up_impact_and_recovery(string sprite)
    {
        var view = Near with { Units = [.. Near.Units.Select(u => u.Id == Hero ? u with { Sprite = sprite } : u)] };
        var used = Used("bosses.attack", null, null, Foe);
        var script = Script(view, used, new AttackResolved(Hero, Foe, true, false), new Damaged(Foe, 4, "bludgeoning", 6));
        var lunge = Assert.Single(script.Cues.OfType<LungeCue>());
        var anim = UnitArt.Load(Pack.Root).Sprite(sprite)!.Anim(lunge.Anim);
        Assert.True(lunge.Length >= anim.Frames.Length / anim.Fps);
        Assert.Equal(10, anim.FrameAt(LastHit(script) - lunge.At));
        Assert.True(script.Length >= lunge.At + lunge.Length);
    }

    [Fact]
    public void Troll_releases_the_rendered_boulder_when_its_authored_hands_open()
    {
        var view = Far with { Units = [.. Far.Units.Select(u => u.Id == Hero ? u with { Sprite = "bosses.troll.troll" } : u)] };
        var used = new AbilityUsed(Hero, new("troll.boulder_toss"), new Target(Unit: Foe), new("bosses.special", "fx.dust", "projectiles.rock"));
        var script = Script(view, used, new Damaged(Foe, 5, "bludgeoning", 5));
        var pose = Assert.Single(script.Cues.OfType<PoseCue>(), c => c.Unit == Hero);
        var flight = Assert.Single(script.Cues.OfType<FlightCue>());
        var anim = UnitArt.Load(Pack.Root).Sprite("bosses.troll.troll")!.Anim(pose.Anim);
        Assert.Equal(15, anim.FrameAt(flight.At - pose.At - .001)); // held overhead
        Assert.Equal(16, anim.FrameAt(flight.At - pose.At)); // empty release hands
        Assert.Equal(flight.At + flight.Length, LastHit(script), 6);
        Assert.True(pose.Length >= anim.Frames.Length / anim.Fps);
        Assert.True(script.Length >= pose.At + pose.Length);
    }

    [Theory]
    [InlineData("troll.frenzy_roar", "bosses.troll.troll", "bosses.buff")]
    [InlineData("golem.seismic_slam", "bosses.golem.granite", "bosses.special")]
    [InlineData("golem.stone_aegis", "bosses.golem.granite", "bosses.buff")]
    public void Giant_self_moves_keep_their_entire_authored_clip(string ability, string sprite, string binding)
    {
        var used = new AbilityUsed(Hero, new(ability), new Target(Unit: Hero), new(binding, null, null));
        var script = Script(Near, used);
        var pose = Assert.Single(script.Cues.OfType<PoseCue>(), c => c.Unit == Hero);
        var anim = UnitArt.Load(Pack.Root).Sprite(sprite)!.Anim(binding);
        Assert.True(pose.Length >= anim.Frames.Length / anim.Fps);
        Assert.True(script.Length >= pose.At + pose.Length);
    }

    [Fact]
    public void Golem_seismic_dust_and_damage_begin_when_the_fists_land()
    {
        var used = new AbilityUsed(Hero, new("golem.seismic_slam"), new Target(Unit: Hero), new("bosses.special", "fx.dust", null));
        var script = Script(Near, used, new Damaged(Foe, 5, "bludgeoning", 5));
        var pose = Assert.Single(script.Cues.OfType<PoseCue>(), c => c.Unit == Hero);
        var dust = Assert.Single(script.Cues.OfType<BurstCue>(), c => c.Clip.Path == Fx.Value.Clip("fx.dust")!.Path);
        var anim = UnitArt.Load(Pack.Root).Sprite("bosses.golem.granite")!.Anim(pose.Anim);
        Assert.Equal(16, anim.FrameAt(dust.At - pose.At));
        Assert.Equal(dust.At, LastHit(script), 6);
        Assert.True(dust.At > pose.At);
        Assert.True(script.Length >= pose.At + pose.Length);
    }

    [Theory]
    [InlineData("ashmaw.cinder_roar", "monsters.roar", 1.2, "ashmaw_roar")]
    [InlineData("ashmaw.ash_shake", "monsters.shake", 1.0, "ashmaw_shake")]
    [InlineData("grisk.dig_faster", "monsters.shout", 0.7, null)]
    public void Creature_self_actions_show_the_whole_clip_and_play_their_own_sound(string ability, string anim, double length, string? sound)
    {
        var used = new AbilityUsed(Hero, new(ability), new Target(Unit: Hero), new PresentationDef(anim, null, null));
        var script = BeatScript.Of(new Beat(1, Hero, [used]), Near, Near, Fx.Value, SoundBank.Load(Pack.Root));
        var pose = Assert.Single(script.Cues.OfType<PoseCue>());
        Assert.Equal(anim, pose.Anim);
        Assert.Equal(length, pose.Length);
        Assert.True(script.Length >= pose.At + pose.Length);
        if (sound is not null) Assert.Contains(script.Cues.OfType<SoundCue>(), c => c.Id == sound);
        Assert.DoesNotContain(script.Cues.OfType<SoundCue>(), c => c.Id == "spell_cast");
    }

    private static RollResult Roll(int die, bool success, bool crit = false) => new([die], die, 3, die + 3, 12, success, crit, false);

    [Theory]
    [InlineData("ashmaw.maul")]
    [InlineData("ashmaw.party_maul")]
    public void Both_bear_profiles_keep_the_whole_attack_clip(string ability)
    {
        var used = new AbilityUsed(Hero, new(ability), new Target(Unit: Foe), new("beasts.attack", null, null));
        var script = Script(Near, used, new AttackResolved(Hero, Foe, true, false), new Damaged(Foe, 4, "slashing", 6));
        var lunge = Assert.Single(script.Cues.OfType<LungeCue>());
        Assert.Equal(1.0, lunge.Length);
        Assert.True(script.Length >= lunge.At + lunge.Length);
    }

    private static BattleEvent[] Hit(int amount, int after, string? anim = "characters.attack", string? fx = "fx.slash.steel", string? projectile = null,
        bool crit = false) =>
    [
        Used(anim, fx, projectile, Foe),
        new Rolled(Hero, RollKind.Attack, Roll(14, true, crit)),
        new AttackResolved(Hero, Foe, true, crit),
        new Damaged(Foe, amount, "slashing", after),
    ];

    [Theory]
    [InlineData("ignite", "fx/flames_fire.png")]
    [InlineData("spread", "fx/flames_fire.png")]
    [InlineData("burn_web", "fx/flames_fire.png")]
    [InlineData("freeze", "fx/ice_frost.png")]
    [InlineData("thaw", "fx/misty_mist.png")]
    [InlineData("douse", "fx/poof_white.png")]
    public void Surface_reactions_use_pack_clips_and_keep_the_action_under_a_second(string rule, string path)
    {
        var tiles = Enumerable.Range(0, 60).Select(x => new Tile(x, 3)).ToImmutableArray();
        var script = Script(Near, new TileReacted(rule, Hero, tiles),
            new ZoneCreated(new Zone(1, null, "burning", tiles, 2, false, false, [])));
        var bursts = script.Cues.OfType<BurstCue>().Where(c => c.Clip.Path == path).ToArray();
        Assert.Equal(tiles, bursts.Select(c => c.Tile));
        Assert.All(bursts, c => Assert.InRange(c.At + c.Length, 0.01, rule is "thaw" or "douse" ? 2.5 : 0.999));
        Assert.InRange(script.Length, 0.5, 0.999);
        Assert.Contains(script.Cues.OfType<MomentCue>(), c => c.Label == "reaction-" + rule);
    }

    [Fact]
    public void Conduct_ripples_in_event_order_and_its_saves_and_damage_land_with_each_tile()
    {
        var tiles = new[] { new Tile(3, 3), new Tile(4, 3), new Tile(5, 3) }.ToImmutableArray();
        var script = Script(Near, new TileReacted("conduct", Hero, tiles),
            new Rolled(Foe, RollKind.Save, Roll(8, false)), new Damaged(Foe, 4, "lightning", 6));
        var ripple = script.Cues.OfType<HazardCue>().ToArray();
        Assert.Equal(tiles, ripple.Select(c => c.Tile));
        Assert.Equal(new[] { 0.0, 0.04, 0.08 }, ripple.Select(c => c.At));
        Assert.Equal(0.04, Assert.Single(script.Cues.OfType<BadgeCue>()).At, 3);
        Assert.Equal(0.04, Assert.Single(script.Cues.OfType<PopCue>()).At, 3);
        Assert.Contains(script.Cues.OfType<BurstCue>(), c => c.Clip.Path == "fx/shock_storm.png" && c.Tile == tiles[1]);
        Assert.InRange(script.Length, 0.5, 0.999);
    }

    [Fact]
    public void A_large_conducted_front_keeps_all_victims_concurrent_including_lethal_hits()
    {
        var units = Enumerable.Range(0, 60).Select(i => Unit(new UnitId("foe" + i), "Enemy", i, 3)).ToArray();
        var tiles = units.Select(u => u.At).ToImmutableArray();
        BattleEvent[] events = [new TileReacted("conduct", Hero, tiles), .. units.SelectMany(u => new BattleEvent[]
            { new Rolled(u.Id, RollKind.Save, Roll(8, false)), new Damaged(u.Id, 10, "lightning", 0), new Died(u.Id) })];
        var script = Script(View(units), events);
        Assert.Equal(60, script.Cues.OfType<PopCue>().Count());
        Assert.Equal(60, script.Cues.OfType<BadgeCue>().Count());
        Assert.All(script.Cues.Where(c => c is not (PopCue or BadgeCue)), c => Assert.InRange(c.At + c.Length, 0, 0.999));
        Assert.InRange(script.Length, 0.6, 0.999);
        Assert.All(script.Cues.OfType<PopCue>(), c => Assert.InRange(c.At, 0, 0.24));
    }

    [Fact]
    public void Reaction_sound_is_once_per_front_and_the_next_turn_waits_for_it()
    {
        var sounds = Tactics.Presentation.Audio.SoundBank.Load(Pack.Root);
        var beat = new Beat(1, null, [new RoundStarted(2, []), new TileReacted("spread", null, [new Tile(3, 3), new Tile(4, 3)]),
            new ZoneCreated(new Zone(1, null, "burning", [new Tile(3, 3)], 2, false, false, [])), new TurnStarted(Hero, 2)]);
        var script = BeatScript.Of(beat, Near, Near, Fx.Value, sounds);
        Assert.Single(script.Cues.OfType<SoundCue>(), c => c.Id == "spell_fire_aura");
        Assert.DoesNotContain(script.Cues.OfType<SoundCue>(), c => c.Id == "zone_created");
        var turn = Assert.Single(script.Cues.OfType<ApplyCue>(), c => c.Event is TurnStarted);
        Assert.InRange(turn.At, 0.85, 0.999);
    }

    [Theory]
    [InlineData("characters.cast")]
    [InlineData("characters.pray")]
    [InlineData("characters.channel")]
    [InlineData("characters.summon")]
    [InlineData("characters.point")]
    [InlineData("characters.buff")]
    public void A_spell_at_arms_length_is_cast_from_where_the_caster_stands(string anim)
    {
        var cues = Script(Near, Used(anim, null, null, Foe)).Cues;

        Assert.Empty(cues.OfType<LungeCue>());
        Assert.Equal(anim, Assert.Single(cues.OfType<PoseCue>()).Anim);
    }

    [Theory]
    [InlineData("characters.attack")]
    [InlineData("characters.thrust")]
    [InlineData("characters.slam")]
    [InlineData("characters.bash")]
    [InlineData("characters.flurry")]
    [InlineData("characters.spin")]
    [InlineData("beasts.special")]
    public void A_blow_at_arms_length_still_lunges(string anim)
        => Assert.Equal(anim, Assert.Single(Script(Near, Used(anim, null, null, Foe)).Cues.OfType<LungeCue>()).Anim);

    [Fact]
    public void A_one_shot_effect_plays_to_its_end_however_long_it_is()
    {
        var clip = Fx.Value.Clip("signature.spell_fireball.fire")!;

        var burst = Assert.Single(Script(Near, Used("characters.attack", "signature.spell_fireball.fire", null, Foe)).Cues.OfType<BurstCue>());

        Assert.True(clip.Seconds > BeatTiming.BurstMost);
        Assert.Equal(clip.Seconds, burst.Length, 3);
    }

    [Fact]
    public void A_looping_effect_has_no_end_of_its_own_so_it_stops_at_the_most()
        => Assert.Equal(BeatTiming.BurstMost, Assert.Single(Script(Near, Used("characters.block", "fx.shield.arcane", null, Foe)).Cues.OfType<BurstCue>()).Length, 3);

    [Fact]
    public void A_long_effect_does_not_hold_the_beat_up()
        => Assert.Equal(Script(Near, Used("characters.attack", "fx.slash.steel", null, Foe)).Length,
            Script(Near, Used("characters.attack", "signature.spell_fireball.fire", null, Foe)).Length, 3);

    [Fact]
    public void An_arrow_named_as_an_effect_flies_and_is_not_left_in_the_target()
    {
        var cues = Script(Far, Used("characters.shoot", "fx.arrow.oak", null, Foe)).Cues;

        Assert.Equal("fx/arrow_oak.png", Assert.Single(cues.OfType<FlightCue>()).Art.Path);
        Assert.Empty(cues.OfType<BurstCue>());
    }

    [Fact]
    public void An_arrow_named_twice_flies_once()
    {
        var cues = Script(Far, Used("characters.shoot", "fx.arrow.oak", "projectiles.arrow", Foe)).Cues;

        Assert.Equal("projectiles/arrow.png", Assert.Single(cues.OfType<FlightCue>()).Art.Path);
        Assert.Empty(cues.OfType<BurstCue>());
    }

    [Fact]
    public void A_line_spell_leaves_the_caster_toward_the_target()
    {
        var burst = Assert.Single(Script(Far, Used("characters.summon", "signature.spell_lightning_bolt.storm", null, Foe)).Cues.OfType<BurstCue>());

        Assert.Equal(new Tile(3, 3), burst.Tile);
        Assert.Equal(new Tile(9, 3), burst.Toward);
    }

    [Fact]
    public void A_burst_on_the_target_is_on_the_target()
    {
        var burst = Assert.Single(Script(Far, Used("characters.cast", "signature.spell_fireball.fire", null, Foe)).Cues.OfType<BurstCue>());

        Assert.Equal(new Tile(9, 3), burst.Tile);
        Assert.Null(burst.Toward);
    }

    [Fact]
    public void A_walk_is_one_step_after_another()
    {
        var script = Script(Near, new Moved(Hero, [new Tile(3, 3), new Tile(3, 4)]), new Moved(Hero, [new Tile(3, 4), new Tile(3, 5)]));

        var steps = script.Cues.OfType<WalkCue>().ToArray();

        Assert.Equal(2, steps.Length);
        Assert.Equal(new Tile(3, 3), steps[0].From);
        Assert.Equal(steps[0].At + steps[0].Length, steps[1].At);
        Assert.Equal(BeatTiming.Step * 2, script.Length, 3);
    }

    [Fact]
    public void A_multi_tile_path_in_one_event_walks_each_leg()
        => Assert.Equal(3, Script(Near, new Moved(Hero, [new Tile(3, 3), new Tile(3, 4), new Tile(3, 5), new Tile(3, 6)])).Cues.OfType<WalkCue>().Count());

    [Fact]
    public void A_walks_position_lands_with_each_step_not_before_it()
    {
        var script = Script(Near, new Moved(Hero, [new Tile(3, 3), new Tile(3, 4)]));

        var landed = Assert.Single(script.Cues.OfType<ApplyCue>());

        Assert.Equal(BeatTiming.Step, landed.At, 3);
    }

    [Fact]
    public void A_melee_attack_lunges_at_its_target_and_is_under_a_second()
    {
        var script = Script(Near, Hit(6, 4));

        var lunge = Assert.Single(script.Cues.OfType<LungeCue>());
        Assert.Equal(new Tile(4, 3), lunge.Toward);
        Assert.Equal("characters.attack", lunge.Anim);
        Assert.InRange(script.Length, 0.3, 1.0);
    }

    [Fact]
    public void A_ranged_attack_flies_and_is_under_a_second_even_across_the_map()
    {
        var script = Script(Far, Hit(6, 4, "characters.shoot", null, "projectiles.arrow"));

        var flight = Assert.Single(script.Cues.OfType<FlightCue>());
        Assert.Equal((new Tile(3, 3), new Tile(9, 3)), (flight.From, flight.To));
        Assert.Empty(script.Cues.OfType<LungeCue>());
        Assert.InRange(script.Length, 0.3, 1.0);
    }

    [Fact]
    public void A_plain_attack_from_range_is_a_bow_shot_though_the_ability_names_no_projectile()
    {
        var script = Script(Far, Hit(6, 4, fx: null));

        Assert.Equal("projectiles/arrow.png", Assert.Single(script.Cues.OfType<FlightCue>()).Art.Path);
        Assert.Contains(script.Cues.OfType<PoseCue>(), p => p.Anim == "shoot" && p.Unit == Hero);
    }

    [Fact]
    public void A_spell_from_range_with_no_projectile_is_not_an_arrow()
        => Assert.Empty(Script(Far, Hit(6, 4, "characters.cast", "fx.lightning.storm")).Cues.OfType<FlightCue>());

    [Fact]
    public void A_spell_with_a_projectile_flies_even_at_arms_length()
        => Assert.Single(Script(Near, Hit(6, 4, "characters.cast", "fx.explosion.fire", "projectiles.fireball")).Cues.OfType<FlightCue>());

    [Fact]
    public void The_damage_lands_after_the_blow_arrives()
    {
        var script = Script(Far, Hit(6, 4, "characters.shoot", null, "projectiles.arrow"));

        var flight = script.Cues.OfType<FlightCue>().Single();
        var pop = Assert.Single(script.Cues.OfType<PopCue>());
        var applied = script.Cues.OfType<ApplyCue>().Single(a => a.Event is Damaged);

        Assert.True(pop.At >= flight.At + flight.Length - 0.001);
        Assert.Equal(pop.At, applied.At);
        Assert.Equal("-6", pop.Text);
        Assert.Equal(PopStyle.Damage, pop.Style);
    }

    [Fact]
    public void Each_attack_of_a_double_attack_stays_under_a_second()
    {
        var script = Script(Near, [.. Hit(3, 7), .. Hit(3, 4)]);

        var lunges = script.Cues.OfType<LungeCue>().ToArray();

        Assert.Equal(2, lunges.Length);
        Assert.InRange(lunges[1].At - lunges[0].At, 0.3, 1.0);
        Assert.True(lunges[1].At >= lunges[0].At + lunges[0].Length - 0.001);   // the second doesn't start over the first
    }

    [Fact]
    public void Several_attacks_in_one_action_each_swing()
    {
        BattleEvent[] second = [new Rolled(Hero, RollKind.Attack, Roll(14, true)), new AttackResolved(Hero, Foe, true, false), new Damaged(Foe, 3, "slashing", 4)];

        var script = Script(Near, [.. Hit(3, 7), .. second]);

        var lunges = script.Cues.OfType<LungeCue>().ToArray();
        Assert.Equal(2, lunges.Length);
        Assert.True(lunges[1].At >= lunges[0].At + lunges[0].Length - 0.001);
    }

    [Fact]
    public void An_area_spell_lands_on_all_its_targets_within_a_second()
    {
        var script = Script(Crowd, [Fireball, .. Targets.SelectMany(t => new BattleEvent[]
        {
            new Rolled(t, RollKind.Save, Roll(4, false)), new Damaged(t, 3, "fire", 7),
        })]);

        Assert.InRange(LastHit(script), 0.3, 1.0);
    }

    [Theory]
    [InlineData(2, 0.78, 1.02)]    // the longest flight's 0.52 to arrive, the first save's 0.2, and 0.06 for each target after
    [InlineData(3, 0.84, 1.08)]
    [InlineData(5, 0.96, 1.20)]    // with the impact's 0.24 held once, after the last hit
    public void An_areas_timing_at_the_longest_range(int targets, double lastHit, double length)
    {
        var script = Script(Crowd, [Fireball, .. Targets.Take(targets).SelectMany(t => new BattleEvent[]
        {
            new Rolled(t, RollKind.Save, Roll(4, false)), new Damaged(t, 3, "fire", 7),
        })]);

        Assert.Equal((lastHit, length), (Math.Round(LastHit(script), 3), Math.Round(script.Length, 3)));
    }

    [Fact]
    public void An_area_spell_that_also_lays_conditions_and_kills_lands_within_a_second_too()
    {
        var script = Script(Crowd, [Fireball, .. Targets.SelectMany(t => new BattleEvent[]
        {
            new Rolled(t, RollKind.Save, Roll(4, false)), new Damaged(t, 10, "fire", 0), new ConditionApplied(t, new ConditionId("burning")), new Died(t),
        })]);

        Assert.InRange(LastHit(script), 0.3, 1.0);
    }

    [Fact]
    public void An_areas_countdown_transform_and_zone_cleanup_do_not_delay_its_other_targets()
    {
        var countdown = new CountdownTicked(Targets[1], 2);
        var transformed = new Transformed(Targets[2], "druid");
        var ended = new ZoneEnded(1);
        var events = new List<BattleEvent> { Fireball };
        for (var i = 0; i < Targets.Length; i++)
        {
            events.Add(new Rolled(Targets[i], RollKind.Save, Roll(4, false)));
            if (i == 1) events.Add(countdown);
            else events.Add(new Damaged(Targets[i], 3, "fire", 7));
            if (i == 2)
            {
                events.Add(transformed);
                events.Add(new Rolled(Targets[i], RollKind.Save, Roll(4, false)));
                events.Add(new ConcentrationBroken(Targets[i], new("druid.entangle")));
                events.Add(ended);
            }
        }
        var script = Script(Crowd, [.. events]);
        Assert.InRange(LastHit(script), 0, 1.1);
        foreach (var effect in new BattleEvent[] { countdown, transformed, ended })
            Assert.Contains(script.Cues.OfType<ApplyCue>(), cue => cue.Event == effect);
    }

    [Fact]
    public void An_area_spell_with_no_saves_lands_within_a_second()
    {
        var script = Script(Crowd, [Fireball, .. Targets.Select(t => new Damaged(t, 3, "fire", 7))]);

        Assert.InRange(LastHit(script), 0.3, 1.0);
    }

    [Fact]
    public void A_sweep_that_rolls_to_hit_each_target_is_one_swing()
    {
        var sweep = new AbilityUsed(Hero, Attack, new Target(Tile: new Tile(5, 3)), new PresentationDef("characters.spin", "fx.slash.steel", null));
        var script = Script(Crowd, [sweep, .. Targets.SelectMany(t => new BattleEvent[]
        {
            new Rolled(Hero, RollKind.Attack, Roll(14, true)), new AttackResolved(Hero, t, true, false), new Damaged(t, 3, "slashing", 7),
        })]);

        Assert.True(script.Cues.OfType<LungeCue>().Count() + script.Cues.OfType<PoseCue>().Count(p => p.Unit == Hero) <= 1);
        Assert.InRange(LastHit(script), 0.2, 1.0);
    }

    [Fact]
    public void An_area_still_shows_every_targets_roll_and_number()
    {
        var script = Script(Crowd, [Fireball, .. Targets.SelectMany(t => new BattleEvent[]
        {
            new Rolled(t, RollKind.Save, Roll(4, false)), new Damaged(t, 3, "fire", 7),
        })]);

        Assert.Equal(Targets, script.Cues.OfType<BadgeCue>().Select(b => b.Unit));
        Assert.Equal(Targets, script.Cues.OfType<PopCue>().Select(p => p.Unit));
    }

    [Fact]
    public void A_target_killed_in_an_area_stays_until_its_fall_has_played()
    {
        var script = Script(Crowd, [Fireball, .. Targets.SelectMany(t => new BattleEvent[] { new Damaged(t, 10, "fire", 0), new Died(t) })]);

        foreach (var target in Targets)
        {
            var pose = script.Cues.OfType<PoseCue>().Single(p => p.Unit == target && p.Anim == "down");
            var gone = script.Cues.OfType<ApplyCue>().Single(a => a.Event is Died d && d.Unit == target);
            Assert.True(gone.At >= pose.At + pose.Length - 0.001);
            Assert.True(gone.At <= script.Length);
        }
    }

    [Fact]
    public void The_stage_is_still_held_for_a_moment_after_an_areas_last_hit()
        => Assert.True(Script(Crowd, [Fireball, .. Targets.Select(t => new Damaged(t, 3, "fire", 7))]) is var script
            && script.Length >= LastHit(script) + BeatTiming.Impact - 0.001);

    [Theory]
    [InlineData(false, 0.46)]    // a swing's 0.22, then the impact's 0.24
    [InlineData(true, 0.74)]     // the same, with 0.28 of flight over six tiles
    public void A_single_target_attack_times_out_as_it_did(bool shot, double seconds)
        => Assert.Equal(seconds, (shot ? Script(Far, Hit(6, 4, "characters.shoot", null, "projectiles.arrow")) : Script(Near, Hit(6, 4))).Length, 3);

    [Fact]
    public void A_single_target_spell_with_a_save_and_a_condition_times_out_as_it_did()
    {
        var script = Script(Far, Used("characters.cast", "fx.hex.shadow", "projectiles.eldritch", Foe), new Rolled(Foe, RollKind.Save, Roll(4, false)),
            new Damaged(Foe, 3, "necrotic", 7), new ConditionApplied(Foe, new ConditionId("cursed")));

        Assert.Equal(0.22 + 0.28 + BeatTiming.BadgeHold + BeatTiming.Impact + BeatTiming.Condition, script.Length, 3);
    }

    [Fact]
    public void Two_attacks_on_one_foe_with_thorns_biting_the_attacker_after_each_hit_are_still_two_swings()
    {
        BattleEvent[] thorns = [new PassiveTriggered(Foe, "thorns"), new Damaged(Hero, 2, "piercing", 8)];

        var script = Script(Near, [.. Hit(3, 7), .. thorns, new Rolled(Hero, RollKind.Attack, Roll(14, true)), new AttackResolved(Hero, Foe, true, false),
            new Damaged(Foe, 3, "slashing", 4), .. thorns]);

        Assert.Equal(2, script.Cues.OfType<LungeCue>().Count());
    }

    [Fact]
    public void A_drain_is_single_target_timing()
        => Assert.Equal(BeatTiming.Windup + BeatTiming.Impact + BeatTiming.Stagger, Script(Near, [.. Hit(6, 4), new Healed(Hero, 3, 10)]).Length, 3);

    [Fact]
    public void A_condition_landing_on_the_caster_does_not_make_an_area()
        => Assert.Equal(BeatTiming.Windup + BeatTiming.Impact + BeatTiming.Condition,
            Script(Near, [.. Hit(6, 4), new ConditionApplied(Hero, new ConditionId("blessed"))]).Length, 3);

    [Fact]
    public void A_three_beam_blast_whose_repelling_blast_shoves_the_foe_into_a_concentrating_ally_is_still_three_swings()
    {
        var ally = new UnitId("druid");
        var other = new UnitId("bandit.1");
        var view = View(Unit(Hero, "Party", 3, 3), Unit(Foe, "Enemy", 7, 3), Unit(ally, "Party", 8, 3), Unit(other, "Enemy", 9, 3));
        var script = Script(view,
            Used("characters.point", "fx.force.eldritch", "projectiles.eldritch", Foe),
            new Rolled(Hero, RollKind.Attack, Roll(7, false)), new AttackResolved(Hero, Foe, false, false),
            new Rolled(Hero, RollKind.Attack, Roll(3, false)), new AttackResolved(Hero, Foe, false, false),
            new Rolled(Hero, RollKind.Attack, Roll(14, true)), new AttackResolved(Hero, Foe, true, false),
            new Damaged(Foe, 4, "force", 12),
            new PassiveTriggered(Foe, "lit.fades"), new ConditionRemoved(Foe, new ConditionId("lit")),
            new PassiveTriggered(Hero, "warlock.agonizing_blast"), new Damaged(Foe, 3, "force", 9),
            new PassiveTriggered(Hero, "warlock.repelling_blast"),
            new Collided(Foe, ally, 2), new Damaged(Foe, 2, "bludgeoning", 7),
            new Collided(ally, Foe, 3), new Damaged(ally, 3, "bludgeoning", 66),
            new Rolled(ally, RollKind.Save, Roll(1, false)), new ConcentrationBroken(ally, new AbilityId("druid.entangle")),
            new ConditionRemoved(other, new ConditionId("restrained")),
            new PassiveTriggered(Hero, "warlock.lifedrinker"), new Damaged(Foe, 5, "necrotic", 2));

        Assert.Equal(3, script.Cues.OfType<PoseCue>().Count(c => c.Toward is not null));
    }

    [Fact]
    public void A_guarded_unit_bracing_at_the_edge_pops_a_word_over_its_tile()
    {
        var view = View(Unit(Hero, "Party", 3, 3), Unit(Foe, "Enemy", 4, 3));

        var script = Script(view, new PushResisted(Foe, new Tile(4, 3)));

        var pop = Assert.Single(script.Cues.OfType<PopCue>());
        Assert.Equal("BRACED", pop.Text);
        Assert.Equal(new Tile(4, 3), pop.Tile);
    }

    [Fact]
    public void A_foe_shoved_into_another_is_not_an_area()
    {
        var other = new UnitId("goblin.2");
        var view = View(Unit(Hero, "Party", 3, 3), Unit(Foe, "Enemy", 4, 3), Unit(other, "Enemy", 6, 3));

        var script = Script(view, [.. Hit(3, 7), new Pushed(Foe, new Tile(4, 3), new Tile(5, 3)), new Collided(Foe, other, 2),
            new Damaged(Foe, 2, null, 5), new Damaged(other, 2, null, 8)]);

        Assert.True(script.Length >= BeatTiming.Windup + BeatTiming.Impact + BeatTiming.Slide * 2);
        Assert.Equal(BeatTiming.Windup, script.Cues.OfType<PopCue>().First().At, 3);
        Assert.True(script.Cues.OfType<ApplyCue>().Single(a => a.Event is Pushed).At >= BeatTiming.Windup + BeatTiming.Impact - 0.001);
    }

    [Fact]
    public void A_real_three_target_area_is_still_an_area()
    {
        var script = Script(Crowd, [Fireball, .. Targets.Take(3).SelectMany(t => new BattleEvent[]
        {
            new Rolled(t, RollKind.Save, Roll(4, false)), new Damaged(t, 3, "fire", 7),
        })]);

        Assert.Equal(0.84, LastHit(script), 3);
    }

    [Fact]
    public void An_area_is_a_moment_worth_a_shot_once_its_last_target_is_hit()
    {
        var script = Script(Crowd, [Fireball, .. Targets.Take(3).SelectMany(t => new BattleEvent[]
        {
            new Rolled(t, RollKind.Save, Roll(4, false)), new Damaged(t, 3, "fire", 7),
        })]);

        Assert.Equal(LastHit(script), script.Cues.OfType<MomentCue>().Single(m => m.Label == "area").At, 3);
    }

    // The next few are the engine's own event order, from real battles (see Real_battles below).

    [Fact]
    public void A_hobgoblins_martial_advantage_then_a_second_attack_is_two_swings()
    {
        var script = Script(Near, new AbilityUsed(Hero, Attack, new Target(Unit: Foe), new PresentationDef("characters.attack", null, null)),
            new Rolled(Hero, RollKind.Attack, Roll(14, true)), new AttackResolved(Hero, Foe, true, false), new Damaged(Foe, 3, "slashing", 7),
            new PassiveTriggered(Hero, "hobgoblin.martial_advantage"), new Damaged(Foe, 7, "slashing", 0),
            new Rolled(Hero, RollKind.Attack, Roll(2, false)), new AttackResolved(Hero, Foe, false, false));

        Assert.Equal(2, script.Cues.OfType<LungeCue>().Count());
    }

    [Fact]
    public void A_wolfs_knockdown_save_on_the_same_target_is_not_an_area()
    {
        var script = Script(Near, [.. Hit(3, 7), new PassiveTriggered(Hero, "wolf.knockdown"), new Rolled(Foe, RollKind.Save, Roll(4, false)),
            new ConditionApplied(Foe, new ConditionId("prone"))]);

        Assert.Equal(BeatTiming.Windup + BeatTiming.Impact + BeatTiming.BadgeHold + BeatTiming.Condition, script.Length, 3);
    }

    [Fact]
    public void A_repelling_blast_knocking_a_foe_into_another_is_not_an_area()
    {
        var other = new UnitId("goblin.2");
        var view = View(Unit(Hero, "Party", 3, 3), Unit(Foe, "Enemy", 4, 3), Unit(other, "Enemy", 6, 3));

        var script = Script(view, [.. Hit(3, 7), new PassiveTriggered(Hero, "warlock.repelling_blast"), new Damaged(Foe, 2, null, 5), new Damaged(other, 2, null, 8),
            new Rolled(Hero, RollKind.Attack, Roll(14, true)), new AttackResolved(Hero, Foe, true, false), new Damaged(Foe, 2, "slashing", 3)]);

        Assert.Equal(2, script.Cues.OfType<LungeCue>().Count());
    }

    [Fact]
    public void A_passive_on_the_first_target_of_a_fireball_leaves_it_an_area()
    {
        var script = Script(Crowd, [Fireball, new Rolled(Targets[0], RollKind.Save, Roll(4, false)), new Damaged(Targets[0], 3, "fire", 7),
            new PassiveTriggered(Targets[0], "fighter.indomitable"), .. Targets.Skip(1).SelectMany(t => new BattleEvent[]
            {
                new Rolled(t, RollKind.Save, Roll(4, false)), new Damaged(t, 3, "fire", 7),
            })]);

        Assert.InRange(LastHit(script), 0.3, 1.0);
    }

    [Fact]
    public void A_mass_heal_aimed_at_one_ally_is_an_area()
    {
        var ally = new UnitId("hero2");
        var view = View([Unit(Hero, "Party", 0, 3), Unit(ally, "Party", 1, 3), .. Targets.Select((t, i) => Unit(t, "Party", 2, 2 + i))]);
        var heal = new AbilityUsed(Hero, Attack, new Target(Unit: ally), new PresentationDef("characters.cast", "fx.heal.holy", null));

        var script = Script(view, [heal, .. Targets.Select(t => new Healed(t, 3, 10))]);

        Assert.InRange(script.Cues.OfType<ApplyCue>().Where(a => a.Event is Healed).Max(a => a.At), 0.2, 1.0);
    }

    [Fact]
    public void Attacks_that_turn_to_another_foe_after_a_kill_are_swings_at_each_not_an_area()
    {
        // #107: the first attack drops the goblin; the engine sends the other two at the second one.
        var other = new UnitId("goblin.2");
        var view = View(Unit(Hero, "Party", 3, 3), Unit(Foe, "Enemy", 4, 3), Unit(other, "Enemy", 3, 4));
        var script = Script(view, [.. Hit(3, 0), new Died(Foe),
            new Retargeted(Hero, other),
            new Rolled(Hero, RollKind.Attack, Roll(14, true)), new AttackResolved(Hero, other, true, false), new Damaged(other, 3, "slashing", 7),
            new Rolled(Hero, RollKind.Attack, Roll(14, true)), new AttackResolved(Hero, other, true, false), new Damaged(other, 3, "slashing", 4)]);

        var lunges = script.Cues.OfType<LungeCue>().ToArray();
        Assert.Equal(3, lunges.Length);
        Assert.Equal([new Tile(4, 3), new Tile(3, 4), new Tile(3, 4)], lunges.Select(l => l.Toward));
        Assert.True(lunges[1].At >= script.Cues.OfType<ApplyCue>().First(a => a.Event is Died).At, "the next swing waits for the fall");
    }

    [Fact]
    public void A_double_attack_on_one_target_is_still_two_swings_after_all_this()
        => Assert.Equal(2, Script(Near, [.. Hit(3, 7), new Rolled(Hero, RollKind.Attack, Roll(14, true)), new AttackResolved(Hero, Foe, true, false),
            new Damaged(Foe, 3, "slashing", 4)]).Cues.OfType<LungeCue>().Count());

    [Fact]
    public void A_miss_says_so()
    {
        var script = Script(Near, Used("characters.attack", "fx.slash.steel", null, Foe), new Rolled(Hero, RollKind.Attack, Roll(2, false)),
            new AttackResolved(Hero, Foe, false, false));

        Assert.Equal(("MISS", Foe), Assert.Single(script.Cues.OfType<PopCue>()) is { } pop ? (pop.Text, pop.Unit) : default);
    }

    [Fact]
    public void A_crit_says_so_with_the_damage()
    {
        var script = Script(Near, Hit(12, 0, crit: true));

        Assert.Equal(["CRIT", "-12"], script.Cues.OfType<PopCue>().Select(p => p.Text));
    }

    [Fact]
    public void Healing_pops_green()
    {
        var pop = Assert.Single(Script(Near, new Healed(Hero, 5, 9)).Cues.OfType<PopCue>());

        Assert.Equal(("+5", PopStyle.Heal), (pop.Text, pop.Style));
    }

    [Fact]
    public void A_save_shows_its_roll_on_the_roller()
    {
        var badge = Assert.Single(Script(Near, new Rolled(Foe, RollKind.Save, Roll(15, true))).Cues.OfType<BadgeCue>());

        Assert.Equal((Foe, 15, true), (badge.Unit, badge.Die, badge.Success));
    }

    [Fact]
    public void An_attack_roll_gets_no_badge()
        => Assert.Empty(Script(Near, Hit(1, 9)).Cues.OfType<BadgeCue>());

    [Fact]
    public void A_condition_saved_shows_its_roll_too()
        => Assert.Single(Script(Near, new ConditionSaved(Hero, new ConditionId("poisoned"), Roll(16, true))).Cues.OfType<BadgeCue>());

    [Fact]
    public void A_push_slides_rather_than_walks()
    {
        var script = Script(Near, new Pushed(Foe, new Tile(4, 3), new Tile(5, 3)));

        Assert.Equal((new Tile(4, 3), new Tile(5, 3)), Assert.Single(script.Cues.OfType<SlideCue>()) is { } s ? (s.From, s.To) : default);
    }

    [Fact]
    public void Someone_who_dies_stays_until_their_fall_has_played()
    {
        var script = Script(Near, new Damaged(Foe, 10, null, 0), new Died(Foe));

        var pose = script.Cues.OfType<PoseCue>().Single(p => p.Anim == "down");
        var gone = script.Cues.OfType<ApplyCue>().Single(a => a.Event is Died);

        Assert.True(gone.At >= pose.At + pose.Length - 0.001);
        Assert.True(pose.Length >= 0.2);
    }

    [Fact]
    public void A_new_unit_arrives_in_a_puff()
    {
        var script = Script(Near, new Spawned(new UnitId("x"), new Tile(6, 6), null));

        Assert.Equal(new Tile(6, 6), Assert.Single(script.Cues.OfType<BurstCue>()).Tile);
    }

    [Fact]
    public void A_line_is_spoken_and_held_long_enough_to_catch_its_start()
    {
        var script = Script(Near, new Said("narrator", "The ropes snap."));

        var line = Assert.Single(script.Cues.OfType<LineCue>());
        Assert.Equal(("narrator", "The ropes snap."), (line.Speaker, line.Line));
        Assert.True(script.Length >= 0.5);
    }

    [Fact]
    public void The_bridge_goes_after_the_walk_and_the_line_that_trigger_it()
    {
        Tile[] bridge = [new(5, 5), new(6, 5)];
        var script = Script(Near, new Moved(Hero, [new Tile(4, 5), new Tile(5, 5)]), new TriggerFired("bridge_collapse"),
            new Said("narrator", "The ropes snap!"), new TerrainAltered([.. bridge], -9), new Fell(Hero, 1, 0, true), new Downed(Hero));

        var walked = script.Cues.OfType<WalkCue>().Single();
        var said = script.Cues.OfType<LineCue>().Single();
        var sunk = script.Cues.OfType<ApplyCue>().Single(a => a.Event is TerrainAltered);
        var fall = script.Cues.OfType<FallCue>().Single();

        Assert.True(walked.At + walked.Length <= said.At + 0.001);
        Assert.True(said.At < sunk.At);
        Assert.Equal(sunk.At, fall.At);            // the ground goes, and they go with it
        Assert.Equal(1, fall.Levels);
        Assert.Contains(script.Cues.OfType<ShakeCue>(), s => s.At <= sunk.At && s.At + s.Length >= sunk.At);
        Assert.Equal(2, script.Cues.OfType<BurstCue>().Count(b => b.At == sunk.At && bridge.Contains(b.Tile)));
    }

    [Fact]
    public void A_bridge_that_goes_a_tile_at_a_time_is_still_one_collapse()
    {
        BattleEvent[] tiles = [.. Enumerable.Range(5, 4).SelectMany(x => new[] { new Tile(x, 5), new Tile(x, 6) }).Select(t => new TerrainAltered([t], -9))];

        var script = Script(Near, tiles);

        Assert.Single(script.Cues.OfType<ShakeCue>());
        Assert.Equal(8, script.Cues.OfType<ApplyCue>().Count(a => a.Event is TerrainAltered));
        Assert.InRange(script.Length, 1, 2);
    }

    [Fact]
    public void Someone_on_a_bridge_goes_down_with_it_and_the_rest_of_it_keeps_going()
    {
        var script = Script(Near, new TerrainAltered([new Tile(3, 3)], -9), new Fell(Hero, 2, 0, true), new Downed(Hero),
            new TerrainAltered([new Tile(4, 3)], -9), new TerrainAltered([new Tile(5, 3)], -9), new Said("warlord", "Ha!"));

        var altered = script.Cues.OfType<ApplyCue>().Where(a => a.Event is TerrainAltered).Select(a => a.At).ToArray();
        var line = script.Cues.OfType<LineCue>().Single();

        Assert.True(altered[2] - altered[0] < 0.6);        // the fall doesn't stop the collapse
        Assert.True(line.At >= altered[2] + BeatTiming.Collapse - 0.001);    // and the jeer waits for the dust
    }

    [Fact]
    public void A_boss_phase_is_marked()
    {
        var script = Script(Near, new PhaseEntered(Foe, 1));

        Assert.NotEmpty(script.Cues.OfType<ShakeCue>());
        Assert.Single(script.Cues.OfType<BurstCue>());
    }

    [Fact]
    public void A_beat_with_nothing_to_show_still_takes_a_moment()
    {
        var script = Script(Near, new TurnEnded(Hero), new TurnStarted(Foe, 1));

        Assert.InRange(script.Length, 0.1, 0.5);
    }

    [Fact]
    public void An_ability_with_no_presentation_still_works()
    {
        var script = Script(Near, new AbilityUsed(Hero, Attack, new Target(Unit: Foe), null), new Damaged(Foe, 1, null, 9));

        Assert.Single(script.Cues.OfType<PopCue>());
    }

    [Fact]
    public void An_ability_on_a_unit_the_view_has_lost_is_played_where_it_can_be()
    {
        var script = Script(Near, new AbilityUsed(new UnitId("gone"), Attack, new Target(Unit: new UnitId("also-gone")), null));

        Assert.True(script.Length >= 0);
    }

    [Fact]
    public void A_self_buff_is_a_pose_and_no_lunge()
    {
        var script = Script(Near, new AbilityUsed(Hero, Attack, new Target(Tile: new Tile(3, 3)), new PresentationDef("characters.cast", "fx.aura.holy", null)));

        Assert.Empty(script.Cues.OfType<LungeCue>());
        Assert.Single(script.Cues.OfType<PoseCue>(), p => p.Anim == "characters.cast");
        Assert.Single(script.Cues.OfType<BurstCue>());
    }

    [Fact]
    public void A_key_the_art_does_not_have_is_left_out()
        => Assert.Empty(Script(Near, Used("characters.attack", "fx.nonsense", "projectiles.nonsense", Foe)).Cues.OfType<BurstCue>());

    [Fact]
    public void Events_the_tv_has_no_use_for_are_skipped()
    {
        var script = Script(Near, new CameraHint(new Tile(1, 1)), new PassiveTriggered(Hero, "x"), new Marked(Foe, Hero));

        Assert.Empty(script.Cues.OfType<PopCue>());
    }

    [Fact]
    public void Everything_is_in_time_order_and_within_the_beat_or_fades_beyond_it_only_if_it_does_not_block()
    {
        var script = Script(Near, [.. Hit(3, 7), new ConditionApplied(Foe, new ConditionId("prone")), new TurnEnded(Hero)]);

        Assert.True(script.Cues.Select(c => c.At).SequenceEqual(script.Cues.Select(c => c.At).Order()));
        Assert.All(script.Cues, c => Assert.True(c.At <= script.Length));
    }

    [Fact]
    public void A_zone_appears_with_a_moment_for_the_camera()
    {
        var zone = new Zone(1, null, "fire", [new Tile(2, 2)], 2, false, false, []);

        Assert.Contains(Script(Near, new ZoneCreated(zone)).Cues.OfType<MomentCue>(), m => m.Label == "zone");
    }

    [Fact]
    public void An_offer_is_a_moment_worth_a_shot_once_it_has_been_applied()
    {
        var offer = new ReactionOffered(new ReactionOffer(new UnitId("hero1"), [], "leavesReach"));

        var cues = BeatScript.Of(new Beat(1, null, [offer]), new BattleView("e", "m", 1, [], null, [], [], null), new BattleView("e", "m", 1, [], null, [], [], null), FxArt.Load(Tactics.Presentation.Tests.Board.Pack.Root)).Cues;

        var applied = cues.Select((c, i) => (c, i)).Single(x => x.c is ApplyCue { Event: ReactionOffered }).i;
        var moment = cues.Select((c, i) => (c, i)).Single(x => x.c is MomentCue { Label: BeatPlayer.ReactionMoment }).i;
        Assert.True(applied < moment);
    }
}
