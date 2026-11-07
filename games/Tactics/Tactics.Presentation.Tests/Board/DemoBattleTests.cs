using Tactics.Logic;
using Tactics.Presentation.Board;

namespace Tactics.Presentation.Tests.Board;

/// <summary><see cref="DemoBattle"/>: the dev-only <c>--tactics-demo</c> views.</summary>
public sealed class DemoBattleTests
{
    [Fact]
    public void Stone_and_mire_review_places_both_giants_with_human_scale_references()
    {
        var battle = DemoBattle.View("river_crossing:stone-and-mire").Battle!;
        var terrain = new BoardTerrain(Pack.Map(battle.Map), battle.Board);
        Assert.Equal(5, battle.Units.Length);
        Assert.Contains(battle.Units, u => u.Sprite == "bosses.troll.troll");
        Assert.Contains(battle.Units, u => u.Sprite == "bosses.golem.granite");
        Assert.Contains(battle.Units, u => u.Sprite == "characters.class_fighter");
        Assert.Empty(battle.Units.Where(u => terrain.Map[u.At].Flags.HasFlag(TileFlags.Impassable)));
        Assert.Equal(5, UnitPlan.Build(battle, terrain, UnitArt.Load(Pack.Root), BoardFit.For(terrain, Pack.Art)).Length);
    }

    [Fact]
    public void Oath_and_shadow_review_places_greatsword_and_rapier_roles_with_refreshed_references()
    {
        var battle = DemoBattle.View("river_crossing:oath-and-shadow").Battle!;
        var terrain = new BoardTerrain(Pack.Map(battle.Map), battle.Board);
        Assert.Equal(5, battle.Units.Length);
        Assert.Contains(battle.Units, u => u.Sprite == "characters.black_knight");
        Assert.Contains(battle.Units, u => u.Sprite == "characters.dark_elf");
        Assert.Contains(battle.Units, u => u.Sprite == "characters.class_fighter");
        Assert.Empty(battle.Units.Where(u => terrain.Map[u.At].Flags.HasFlag(TileFlags.Impassable)));
        Assert.Equal(5, UnitPlan.Build(battle, terrain, UnitArt.Load(Pack.Root), BoardFit.For(terrain, Pack.Art)).Length);
    }

    [Fact]
    public void Rime_keeper_review_places_both_staff_users_with_party_and_refreshed_references()
    {
        var battle = DemoBattle.View("river_crossing:rime-keepers").Battle!;
        var terrain = new BoardTerrain(Pack.Map(battle.Map), battle.Board);
        Assert.Equal(5, battle.Units.Length);
        Assert.Contains(battle.Units, u => u.Sprite == "characters.necromancer");
        Assert.Contains(battle.Units, u => u.Sprite == "characters.lich");
        Assert.Contains(battle.Units, u => u.Sprite == "characters.class_fighter");
        Assert.Empty(battle.Units.Where(u => terrain.Map[u.At].Flags.HasFlag(TileFlags.Impassable)));
        Assert.Equal(5, UnitPlan.Build(battle, terrain, UnitArt.Load(Pack.Root), BoardFit.For(terrain, Pack.Art)).Length);
    }

    [Fact]
    public void Hollow_dead_review_places_risen_and_wraith_with_party_and_refreshed_references()
    {
        var battle = DemoBattle.View("river_crossing:hollow-dead").Battle!;
        var terrain = new BoardTerrain(Pack.Map(battle.Map), battle.Board);
        Assert.Equal(5, battle.Units.Length);
        Assert.Contains(battle.Units, u => u.Sprite == "characters.zombie");
        Assert.Contains(battle.Units, u => u.Sprite == "bosses.wraith.wraith");
        Assert.Contains(battle.Units, u => u.Sprite == "characters.class_fighter");
        Assert.Empty(battle.Units.Where(u => terrain.Map[u.At].Flags.HasFlag(TileFlags.Impassable)));
        Assert.Equal(5, UnitPlan.Build(battle, terrain, UnitArt.Load(Pack.Root), BoardFit.For(terrain, Pack.Art)).Length);
    }

    [Fact]
    public void Bone_warden_review_places_both_weapons_with_party_and_refreshed_human_references()
    {
        var battle = DemoBattle.View("river_crossing:bone-wardens").Battle!;
        var terrain = new BoardTerrain(Pack.Map(battle.Map), battle.Board);
        Assert.Equal(5, battle.Units.Length);
        Assert.Contains(battle.Units, u => u.Sprite == "characters.skeleton");
        Assert.Contains(battle.Units, u => u.Sprite == "characters.skeleton_archer");
        Assert.Contains(battle.Units, u => u.Sprite == "characters.class_fighter");
        Assert.Empty(battle.Units.Where(u => terrain.Map[u.At].Flags.HasFlag(TileFlags.Impassable)));
        Assert.Equal(5, UnitPlan.Build(battle, terrain, UnitArt.Load(Pack.Root), BoardFit.For(terrain, Pack.Art)).Length);
    }

    [Fact]
    public void Kindled_review_places_both_casters_with_party_and_refreshed_human_references()
    {
        var battle = DemoBattle.View("river_crossing:kindled").Battle!;
        var terrain = new BoardTerrain(Pack.Map(battle.Map), battle.Board);
        Assert.Equal(5, battle.Units.Length);
        Assert.Contains(battle.Units, u => u.Sprite == "characters.cultist");
        Assert.Contains(battle.Units, u => u.Sprite == "characters.cult_leader");
        Assert.Contains(battle.Units, u => u.Sprite == "characters.class_fighter");
        Assert.Empty(battle.Units.Where(u => terrain.Map[u.At].Flags.HasFlag(TileFlags.Impassable)));
        Assert.Equal(5, UnitPlan.Build(battle, terrain, UnitArt.Load(Pack.Root), BoardFit.For(terrain, Pack.Art)).Length);
    }

    [Fact]
    public void Road_and_host_review_places_both_new_roles_with_party_and_goblin_references()
    {
        var battle = DemoBattle.View("river_crossing:road-and-host").Battle!;
        var terrain = new BoardTerrain(Pack.Map(battle.Map), battle.Board);
        Assert.Equal(5, battle.Units.Length);
        Assert.Contains(battle.Units, u => u.Sprite == "characters.bandit");
        Assert.Contains(battle.Units, u => u.Sprite == "characters.hobgoblin");
        Assert.Contains(battle.Units, u => u.Sprite == "characters.class_fighter");
        Assert.Empty(battle.Units.Where(u => terrain.Map[u.At].Flags.HasFlag(TileFlags.Impassable)));
        Assert.Equal(5, UnitPlan.Build(battle, terrain, UnitArt.Load(Pack.Root), BoardFit.For(terrain, Pack.Art)).Length);
    }

    [Fact]
    public void Goblin_role_review_places_each_role_with_a_party_and_cutthroat_reference()
    {
        var battle = DemoBattle.View("river_crossing:goblin-roles").Battle!;
        var terrain = new BoardTerrain(Pack.Map(battle.Map), battle.Board);
        Assert.Equal(5, battle.Units.Length);
        Assert.Contains(battle.Units, u => u.Sprite == "characters.goblin_archer");
        Assert.Contains(battle.Units, u => u.Sprite == "characters.goblin_shaman");
        Assert.Contains(battle.Units, u => u.Sprite == "characters.goblin_spear");
        Assert.Empty(battle.Units.Where(u => terrain.Map[u.At].Flags.HasFlag(TileFlags.Impassable)));
        Assert.Equal(5, UnitPlan.Build(battle, terrain, UnitArt.Load(Pack.Root), BoardFit.For(terrain, Pack.Art)).Length);
    }

    [Fact]
    public void Mob_refresh_review_places_the_new_mobs_beside_the_party_and_kobold_on_walkable_ground()
    {
        var battle = DemoBattle.View("river_crossing:mob-refresh").Battle!;
        var terrain = new BoardTerrain(Pack.Map(battle.Map), battle.Board);
        Assert.Equal(5, battle.Units.Length);
        Assert.Contains(battle.Units, u => u.Sprite == "characters.orc_brute");
        Assert.Contains(battle.Units, u => u.Sprite == "beasts.drake.red");
        Assert.Empty(battle.Units.Where(u => terrain.Map[u.At].Flags.HasFlag(TileFlags.Impassable)));
        Assert.Equal(5, UnitPlan.Build(battle, terrain, UnitArt.Load(Pack.Root), BoardFit.For(terrain, Pack.Art)).Length);
    }

    [Fact]
    public void A_plain_demo_is_the_untouched_map()
    {
        var battle = DemoBattle.View("crypt_hall").Battle!;

        Assert.Equal("crypt_hall", battle.Map);
        Assert.Empty(battle.Board!.Zones);
        Assert.Empty(battle.Board.Heights);
        Assert.Empty(battle.Board.Sunk);
    }

    [Fact]
    public void The_changed_demo_has_zones_a_raised_tile_and_the_collapsed_bridge()
    {
        var battle = DemoBattle.View("warlord_bridge:changed").Battle!;

        Assert.Equal("warlord_bridge", battle.Map);
        Assert.NotEmpty(battle.Board!.Zones);
        Assert.Contains(battle.Board.Heights, h => h.Delta > 0);
        Assert.Equal(8, battle.Board.Sunk.Length);
    }

    [Fact]
    public void The_fight_demo_has_every_kind_of_unit_and_they_all_go_on_the_board()
    {
        var battle = DemoBattle.View("river_crossing:fight").Battle!;
        var terrain = new BoardTerrain(Pack.Map("river_crossing"), battle.Board);

        var units = UnitPlan.Build(battle, terrain, UnitArt.Load(Pack.Root), BoardFit.For(terrain, Pack.Art));

        Assert.Equal(battle.Units.Count(u => u.Life is "Up" or "Downed"), units.Length);
        Assert.Contains(battle.Units, u => u.Life == "Downed" && u.Countdown > 0);
        Assert.Contains(battle.Units, u => u.Life == "Out");
        Assert.Contains(battle.Units, u => u.Life == "Dead");
        Assert.Contains(units, u => u.Active);
        Assert.Contains(units, u => !u.Icons.IsEmpty);
        Assert.Contains(units, u => u.Sprite.Width > 64);
        Assert.Empty(battle.Units.Where(u => terrain.Map[u.At].Flags.HasFlag(TileFlags.Impassable)));
    }

    [Fact]
    public void The_view_goes_to_the_board()
    {
        var view = DemoBattle.View("river_crossing");

        Assert.Equal("Battle", view.Phase);
        Assert.NotNull(PhaseScreen.Of(view).Board);
    }
    [Fact]
    public void The_creature_art_review_uses_the_real_battle_view_without_adding_live_encounters()
    {
        var battle = DemoBattle.View("forest_ruins:creatures").Battle!;
        Assert.Equal("forest_ruins", battle.Map);
        Assert.Equal(15, battle.Units.Length);
        Assert.Single(battle.Units, u => u.Side == "Party");
        var art = UnitArt.Load(Pack.Root);
        Assert.All(battle.Units, u => Assert.NotNull(art.Sprite(u.Sprite)));
    }
}
