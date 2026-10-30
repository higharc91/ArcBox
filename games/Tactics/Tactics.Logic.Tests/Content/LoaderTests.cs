using System.Text;

namespace Tactics.Logic.Tests;

public sealed class LoaderTests
{
    private static string Base => ContentPaths.Fixture("minimal", "base");

    private static string Chapter(string fixture) => ContentPaths.Fixture(fixture, "chapter-01");

    [Fact]
    public void Loads_minimal_pack()
    {
        var art = ArtManifests.Load(ContentPaths.ContentBase);
        var pack = RulesPack.Load([Base], art);

        Assert.Equal(["test.bolt", "test.hold", "test.mend", "test.potion", "test.strike"], pack.Abilities.Keys.Select(k => k.Value));
        Assert.Equal(["test.sneak", "test.tough"], pack.Passives.Keys);
        Assert.Equal(["burning", "incapacitated", "lit", "prone", "stunned"], pack.Conditions.Keys.Select(k => k.Value));
        Assert.Equal(["fighter"], pack.Classes.Keys);
        Assert.Equal(["human"], pack.Races.Keys);
        Assert.Equal(["soldier"], pack.Backgrounds.Keys);
        Assert.Equal(["wolf"], pack.Monsters.Keys);
        Assert.Equal(["potion"], pack.Items.Keys.Select(k => k.Value));
        Assert.Equal(["field"], pack.Maps.Keys);
        Assert.Equal(["field_skirmish"], pack.Encounters.Keys);
        Assert.Equal(["brute"], pack.AiProfiles.Keys);
        Assert.Equal(["sprained_ankle"], pack.Injuries.Keys.Select(k => k.Value));
        Assert.Equal([0, 1000, 2000, 3000, 4000, 5000, 6000, 7000, 8000, 9000], pack.Xp.Thresholds);
        Assert.Matches("^[0-9a-f]{64}$", pack.Hash);

        // Typed content, deserialised in full.
        var bolt = pack.Abilities[new AbilityId("test.bolt")];
        var attack = Assert.IsType<AttackEffect>(Assert.Single(bolt.Effects));
        Assert.Equal(Dice.Parse("3d6"), Assert.IsType<DamageEffect>(attack.OnHit[0]).Dice!.At(7));
        Assert.Equal(new ApplyEffect(new ConditionId("lit"), 1), attack.OnHit[1]);

        var fighter = pack.Classes["fighter"];
        Assert.Equal(10, fighter.HitDie);
        Assert.Equal(3, fighter.Resources["grit"].At(5));
        Assert.Equal(Enumerable.Range(1, 10), fighter.Features.Keys);
        Assert.Equal(new AbilityId("test.strike"), fighter.Features[1][0].Ability);
        Assert.Equal(1, fighter.Features[2][0].Choose);
        Assert.Equal(["test.bolt", "test.mend"], fighter.Features[2][0].From);
        Assert.Equal(new ResourceRegenDef("grit", 1, 2), fighter.Features[3][0].ResourceRegen);
        Assert.Equal(2, fighter.Features[5][0].Attacks);
        Assert.Equal("wolf", Assert.Single(fighter.Subclasses).Companion);
        Assert.Equal([Stat.Str, Stat.Con, Stat.Dex, Stat.Wis, Stat.Cha, Stat.Int], fighter.AiTags.Priority);
        Assert.Null(fighter.CastingStat);

        var map = pack.MapFor("field")!;
        Assert.Equal((6, 5), (map.Width, map.Height));
        Assert.Equal(new TileInfo(0, 2, TileFlags.Water | TileFlags.Difficult), map[new Tile(2, 3)]);
        Assert.Equal(new PropPlacement("crate", new Tile(3, 2), true, Cover.ThreeQuarters), map.Props[0]);
        Assert.Equal(4, map.Zones["party_spawn"].Length);

        var encounter = pack.Encounters["field_skirmish"];
        Assert.Equal(ObjectiveExpr.Of(Objectives.PartyOut), encounter.Objectives!.Lose);
        Assert.Equal(2, encounter.Triggers.Length);
        Assert.True(encounter.Triggers[0].Once);    // "once" defaults to true
        Assert.False(encounter.Triggers[1].Once);

        // Every string-keyed collection keeps ordinal ordering.
        Assert.Same(StringComparer.Ordinal, pack.Passives.KeyComparer);
        Assert.Same(StringComparer.Ordinal, fighter.Resources.KeyComparer);
        Assert.Same(StringComparer.Ordinal, pack.Conditions[new ConditionId("lit")].Tags.KeyComparer);
    }

    [Fact]
    public void Base_rules_manifest_loads_with_no_content_yet()
    {
        var pack = RulesPack.Load([ContentPaths.BaseRules], ArtManifests.Load(ContentPaths.ContentBase));

        Assert.Empty(pack.Abilities);
        Assert.Empty(pack.Classes);
    }

    [Fact]
    public void Chapter_adds_ability()
    {
        var pack = RulesPack.Load([Base, Chapter("chapter_adds")]);

        Assert.Contains(new AbilityId("chapter.cleave"), pack.Abilities.Keys);
        Assert.Equal("Strike", pack.Abilities[new AbilityId("test.strike")].Name);
    }

    [Fact]
    public void Chapter_replaces_with_explicit_replaces()
    {
        var pack = RulesPack.Load([Base, Chapter("chapter_replaces")]);

        var strike = pack.Abilities[new AbilityId("test.strike")];
        Assert.Equal("Mighty Strike", strike.Name);
        Assert.Equal(1, Assert.IsType<AttackEffect>(Assert.Single(strike.Effects)).Bonus);
    }

    [Fact]
    public void Packs_load_in_dependency_order_whatever_order_they_are_given()
    {
        var pack = RulesPack.Load([Chapter("chapter_replaces"), Base]);

        Assert.Equal("Mighty Strike", pack.Abilities[new AbilityId("test.strike")].Name);
    }

    [Fact]
    public void Hash_is_stable_and_order_independent_of_file_listing()
    {
        var first = RulesPack.Load([Base, Chapter("chapter_adds")]).Hash;
        var again = RulesPack.Load([Base, Chapter("chapter_adds")]).Hash;
        var reversed = RulesPack.Load([Chapter("chapter_adds"), Base]).Hash;
        Assert.Equal(first, again);
        Assert.Equal(first, reversed);
        Assert.NotEqual(first, RulesPack.Load([Base]).Hash);

        (string, byte[])[] files = [("base/a.json", "{}"u8.ToArray()), ("base/b.json", "[]"u8.ToArray()), ("ch/a.json", "1"u8.ToArray())];
        Assert.Equal(ContentHash.Of(files), ContentHash.Of(files.Reverse()));
        Assert.Equal(ContentHash.Of(files), ContentHash.Of([files[1], files[2], files[0]]));
    }

    [Fact]
    public void Hash_changes_when_any_byte_changes()
    {
        (string Path, byte[] Bytes)[] files = [("base/a.json", "{\"id\":\"x\"}"u8.ToArray()), ("base/b.json", "[1,2]"u8.ToArray())];
        var original = ContentHash.Of(files);
        var seen = new HashSet<string> { original };

        for (var f = 0; f < files.Length; f++)
        for (var i = 0; i < files[f].Bytes.Length; i++)
        {
            var changed = files.Select(x => (x.Path, (byte[])x.Bytes.Clone())).ToArray();
            changed[f].Item2[i] ^= 1;
            Assert.True(seen.Add(ContentHash.Of(changed)), $"Flipping byte {i} of {files[f].Path} left the hash unchanged.");
        }

        // A rename, or moving bytes from one file to the next, changes it too.
        Assert.True(seen.Add(ContentHash.Of([("base/c.json", files[0].Bytes), files[1]])));
        Assert.True(seen.Add(ContentHash.Of([(files[0].Path, "{\"id\":\"x\"}[1"u8.ToArray()), (files[1].Path, ",2]"u8.ToArray())])));

        // And end to end: one changed byte in one file of a pack.
        var copy = CopyToTemp(Base);
        try
        {
            var before = RulesPack.Load([copy]).Hash;
            var monsters = Path.Combine(copy, "monsters", "beasts.json");
            File.WriteAllText(monsters, File.ReadAllText(monsters).Replace("\"Wolf\"", "\"Wolg\"", StringComparison.Ordinal),
                new UTF8Encoding(false));
            var afterByte = RulesPack.Load([copy]).Hash;
            Assert.NotEqual(before, afterByte);

            // The manifests are hashed too: reordering a manifest's file list is a change.
            var manifest = Path.Combine(copy, "manifest.json");
            File.WriteAllText(manifest, File.ReadAllText(manifest).Replace("\"races.json\",\n    \"backgrounds.json\"",
                "\"backgrounds.json\",\n    \"races.json\"", StringComparison.Ordinal), new UTF8Encoding(false));
            Assert.NotEqual(afterByte, RulesPack.Load([copy]).Hash);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(copy)!, recursive: true);
        }
    }

    [Fact]
    public void Art_manifest_lists_every_set_asset_and_variant()
    {
        var art = ArtManifests.Load(ContentPaths.ContentBase);

        Assert.Contains("signature.vanguard_shield_wall", art.Keys);
        Assert.Contains("fx.slash", art.Keys);
        Assert.Contains("projectiles.arrow", art.Keys);
        Assert.Contains("beasts.wolf", art.Keys);
        Assert.Contains("beasts.wolf.grey", art.Keys);
        Assert.Contains("characters.goblin_archer", art.Keys);
        Assert.Contains("characters.cast", art.Keys);
        Assert.Contains("props.crate", art.Keys);
        Assert.DoesNotContain(art.Keys, k => k.StartsWith("rules.", StringComparison.Ordinal));
        Assert.Same(StringComparer.Ordinal, art.Keys.KeyComparer);
    }

    [Fact]
    public void A_malformed_art_manifest_is_a_content_error()
    {
        var root = Path.Combine(Path.GetTempPath(), "tactics-art-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "fx"));
            Directory.CreateDirectory(Path.Combine(root, "props"));
            Directory.CreateDirectory(Path.Combine(root, "status"));
            File.WriteAllText(Path.Combine(root, "fx", "manifest.json"), "{ \"effects\": { \"slash\": {} }");
            File.WriteAllText(Path.Combine(root, "props", "manifest.json"), "[1, 2]");
            File.WriteAllText(Path.Combine(root, "status", "manifest.json"), "{ \"statuses\": { \"stunned\": {} } }");

            var errors = Assert.Throws<ContentException>(() => ArtManifests.Load(root)).Errors;

            Assert.Equal(["fx/manifest.json", "props/manifest.json"], errors.Select(e => e.File));
            Assert.All(errors, e => Assert.Equal("", e.Pointer));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>Copies a pack folder to a new temporary folder, returning the copy's path (its parent is the temp root).</summary>
    private static string CopyToTemp(string pack)
    {
        var root = Path.Combine(Path.GetTempPath(), "tactics-loader-" + Guid.NewGuid().ToString("N"));
        var target = Path.Combine(root, Path.GetFileName(pack));
        foreach (var file in Directory.GetFiles(pack, "*", SearchOption.AllDirectories))
        {
            var to = Path.Combine(target, Path.GetRelativePath(pack, file));
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            File.Copy(file, to);
        }
        return target;
    }
}
