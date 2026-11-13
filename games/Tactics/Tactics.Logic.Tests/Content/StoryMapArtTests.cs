using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using ArcBox.Sdk;

namespace Tactics.Logic.Tests;

public sealed class StoryMapArtTests
{
    private static RulesPack Rules => BasePackTests.Rules;

    [Theory]
    [InlineData("forest_ruins", "6F9368FEB2ADD8C4C66F7600761FB6660C7477B3027FE3F3783549F3149F22E3", "7,2:true:three_quarters|12,2:true:half|7,5:true:half|5,7:false:half|9,9:true:three_quarters")]
    [InlineData("warlord_bridge", "6D9750E667F77C50F1EDE43C6D0A59ED93012E4405971C1B8174503866FDB498", "3,3:true:three_quarters|2,8:true:three_quarters|10,8:true:three_quarters|12,7:true:half")]
    [InlineData("crystal_cave", "DCDEA82F0CA37DBA5DDE3F1AF4FE3EF06D13DB5B5613477199D800568CFF20E6", "4,3:true:three_quarters|7,3:true:half|8,6:true:three_quarters|11,2:true:three_quarters")]
    [InlineData("rime_tower", "DCDEA82F0CA37DBA5DDE3F1AF4FE3EF06D13DB5B5613477199D800568CFF20E6", "4,3:true:three_quarters|7,3:true:half|8,6:true:three_quarters|11,2:true:three_quarters")]
    public void Art_dressing_retains_tuned_heights_cover_and_flags_except_the_declared_shallow_pool(string id, string geometryHash, string cover)
    {
        var map = Rules.Maps[id];
        Assert.Equal((14, 12), (map.Width, map.Height));
        var bytes = new List<byte>();
        for (var y = 0; y < map.Height; y++)
        for (var x = 0; x < map.Width; x++)
        {
            var tile = map[new Tile(x, y)];
            var flags = tile.Flags;
            if (id == "forest_ruins" && x < 3 && y < 3)
            {
                Assert.True(flags.HasFlag(TileFlags.Water));
                flags &= ~TileFlags.Water;
            }
            bytes.Add((byte)tile.Height); bytes.Add((byte)flags);
        }
        Assert.Equal(geometryHash, Convert.ToHexString(SHA256.HashData(bytes.ToArray())));
        static string CoverName(Cover c) => c switch { Cover.None => "none", Cover.Half => "half", Cover.ThreeQuarters => "three_quarters", _ => "full" };
        Assert.Equal(cover, string.Join("|", map.Props.Select(p => $"{p.At.X},{p.At.Y}:{p.Blocks.ToString().ToLowerInvariant()}:{CoverName(p.Cover)}")));
    }

    [Fact]
    public void Dominant_ground_matches_the_desert_marsh_cave_and_watchtower()
    {
        static int Count(BattleMap map, params string[] families) => Enumerable.Range(0, map.Height)
            .SelectMany(y => Enumerable.Range(0, map.Width).Select(x => new Tile(x,y)))
            .Count(t => families.Any(f => map.Materials[map[t].Material].StartsWith(f + "/", StringComparison.Ordinal)));
        Assert.True(Count(Rules.Maps["warlord_bridge"], "sand", "dunes", "sandstone") >= 120);
        Assert.True(Count(Rules.Maps["forest_ruins"], "bog", "mud", "water") >= 120);
        Assert.Equal(9, Count(Rules.Maps["forest_ruins"], "water"));
        Assert.True(Count(Rules.Maps["crystal_cave"], "ice", "snow") >= 130);
        var tower = Rules.Maps["rime_tower"];
        Assert.Equal("rime_tower", Rules.Encounters["rime_tower"].Map);
        Assert.True(Count(tower, "flagstone") >= 120);
        Assert.NotEqual(Rules.Maps["crystal_cave"].Materials, tower.Materials);
        Assert.Contains(tower.Props, p => p.Id == "statue_knight");
        Assert.DoesNotContain(tower.Props, p => p.Id is "minecart" or "stalagmite" or "crystal_big");
        Assert.All(Rules.Encounters["crystal_cave"].Enemies.Where(e => e.Unit == "rime_spider"), e => Assert.Equal("phase", e.Variant));
    }

    [Fact]
    public void The_bone_warden_keeps_the_undead_casters_exact_mechanics_and_stable_unit_identity()
    {
        var before = Rules.Monsters["necromancer"];
        var after = Rules.Monsters["crypt_warden"];
        Assert.Equal("Bone Warden", after.Name);
        Assert.Equal("characters.lich", after.Sprite);
        Assert.Equal(JsonSerializer.Serialize(before with { Id = after.Id, Name = after.Name, Sprite = after.Sprite }, ArcJson.Options),
            JsonSerializer.Serialize(after, ArcJson.Options));
        var unit = Rules.Encounters["crypt_hall"].Enemies.Single(e => e.Unit == "crypt_warden");
        Assert.Equal("necromancer.1", unit.Id);
        Assert.Equal("necromancer", unit.UnitTag);
    }

    [Fact]
    public void All_nine_story_encounters_set_up_for_four_five_and_six_players()
    {
        foreach (var id in new[] { "ford_assault", "river_crossing", "forest_ruins", "warlord_bridge", "crypt_hall", "crystal_cave", "rime_tower", "keep_gate", "hall_of_the_flame" })
        foreach (var size in new[] {4, 5, 6})
        {
            string[] classes = ["fighter", "cleric", "rogue", "wizard", "ranger", "paladin"];
            var level = Rules.Encounters[id].Level ?? 1;
            var party = classes.Take(size).Select((c,i) => new PartyMember(new UnitId($"hero{i}"),
                SheetBuilder.DefaultCharacter(c, "human", c, null, level, Rules), new AiControlled("companion"))).ToImmutableArray();
            Assert.NotNull(Battle.Start(Encounters.Setup(id, party, Difficulty.Normal, Rules), Rules, 1).State);
        }
    }
}
