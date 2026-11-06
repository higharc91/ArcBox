using System.Collections.Immutable;
using System.Text.Json;
using Tactics.Game;
using Tactics.Logic;
using Tactics.Presentation.Tests.Board;
using Tactics.Presentation.World;

namespace Tactics.Presentation.Tests.World;

/// <summary>
/// <see cref="ThingArt"/>: a thing's own art by key once the set exists, else its stand-in, else the plain marker (interactables design §4.3),
/// so the user's art AI can drop the set in with no code change.
/// </summary>
public sealed class ThingArtTests
{
    private static readonly Lazy<ThingArt> Now = new(() => ThingArt.Load(Pack.Root));

    /// <summary>The base pack's manifests, with the art brief's manifest copied in as <c>interactables/manifest.json</c>, as the art AI will.</summary>
    internal static readonly Lazy<string> WithArt = new(() =>
    {
        var root = Directory.CreateTempSubdirectory("tactics-things-art-").FullName;
        foreach (var set in new[] { "props", "dungeon", "pickups", "emotes", "fx", "signature", "projectiles", "sfx", "music" })
        {
            var from = Path.Combine(Pack.Root, set, "manifest.json");
            if (!File.Exists(from))
                continue;
            Directory.CreateDirectory(Path.Combine(root, set));
            File.Copy(from, Path.Combine(root, set, "manifest.json"));
        }
        Directory.CreateDirectory(Path.Combine(root, ThingArt.Set));
        File.Copy(Brief, Path.Combine(root, ThingArt.Set, "manifest.json"));
        return root;
    });

    private static string Brief
    {
        get
        {
            for (var dir = new DirectoryInfo(Pack.Root); dir is not null; dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "ArcBox.slnx")))
                    return Path.Combine(dir.FullName, "docs", "superpowers", "specs", "2026-11-02-tactics-interactables-art.json");
            throw new DirectoryNotFoundException("No ArcBox.slnx above the pack.");
        }
    }

    /// <summary>Every state of every kind the art brief draws, as the TV would be sent it.</summary>
    internal static IEnumerable<ThingView> EveryState()
    {
        foreach (var kind in InteractableKinds.All.Values)
        {
            var states = kind.Family == InteractableFamily.FloorTrap ? ["armed", "disarmed", "sprung"] : kind.States.ToArray();
            foreach (var state in states)
                yield return new ThingView($"{kind.Id}.{state}", kind.Id, new Tile(1, 1), "sw", kind.Id == "ambient" ? "props.cart" : kind.Art, state,
                    kind.Id == "ambient" ? "props.cart" : kind.StandIn.GetValueOrDefault(state), kind.Name, false, false);
        }
    }

    [Fact]
    public void Kinds_without_generated_art_keep_their_stand_in_or_marker()
    {
        var art = Now.Value;
        Assert.True(art.HasOwnArt);
        foreach (var thing in EveryState())
        {
            var pick = art.Pick(thing);
            if (thing.Kind == "campfire")
            {
                Assert.Equal(ThingSource.Art, pick.Source);
                Assert.Equal("interactables.campfire." + thing.State, pick.Key);
                Assert.True(File.Exists(Path.Combine(Pack.Root, pick.Sprite!.Path)));
                continue;
            }
            if (thing.StandIn is null)
            {
                Assert.Equal(ThingSource.Marker, pick.Source);
                Assert.Null(pick.Sprite);
                continue;
            }
            Assert.Equal(ThingSource.StandIn, pick.Source);
            Assert.Equal(thing.StandIn, pick.Key);
            Assert.True(File.Exists(Path.Combine(Pack.Root, pick.Sprite!.Path)), $"{thing.Id}: {pick.Sprite.Path}");
        }
    }

    [Fact]
    public void A_stand_in_is_the_props_own_sprite_size_and_anchor()
    {
        var chest = Now.Value.Sprite("props.chest_open")!;
        Assert.Equal(("props/chest_open_oak.png", 26, 26, 13, 25), (chest.Path, chest.Width, chest.Height, chest.AnchorX, chest.AnchorY));

        var sack = Now.Value.Sprite("pickups.loot_sack")!;       // the pickups' extras: a strip of 6, looping
        Assert.Equal(("pickups/loot_sack.png", 24, 24, 12, 23), (sack.Path, sack.Width, sack.Height, sack.AnchorX, sack.AnchorY));
        Assert.Equal(6, sack.Idle.Frames.Length);
        Assert.Null(Now.Value.Sprite("props.no_such_prop"));
        Assert.Null(Now.Value.Sprite("interactables.chest.closed"));
    }

    [Fact]
    public void Once_the_brief_manifest_is_in_the_pack_every_state_draws_its_own_art_by_key_alone()
    {
        var art = ThingArt.Load(WithArt.Value);
        Assert.True(art.HasOwnArt);
        using var brief = JsonDocument.Parse(File.ReadAllText(Brief));
        var props = brief.RootElement.GetProperty("props");
        foreach (var thing in EveryState().Where(t => t.Kind != "ambient"))
        {
            var pick = art.Pick(thing);
            Assert.Equal(ThingSource.Art, pick.Source);
            Assert.Equal($"interactables.{thing.Kind}.{thing.State}", pick.Key);
            var asset = props.GetProperty(thing.Kind);
            Assert.Equal($"interactables/{asset.GetProperty("variants").GetProperty(thing.State).GetString()}", pick.Sprite!.Path);
            Assert.Equal(asset.GetProperty("size")[0].GetInt32(), pick.Sprite.Width);
            Assert.Equal(asset.GetProperty("anchor")[1].GetInt32(), pick.Sprite.AnchorY);
            Assert.Equal(asset.TryGetProperty("layer", out var layer) && layer.GetString() == "ground", pick.Flat);
        }
        Assert.Equal(ThingSource.StandIn, art.Pick(EveryState().Single(t => t.Kind == "ambient")).Source);     // no art of its own
    }

    [Fact]
    public void A_state_the_set_lacks_falls_back_to_the_stand_in_and_never_to_another_state()
    {
        var root = Directory.CreateTempSubdirectory("tactics-things-partial-").FullName;
        foreach (var set in new[] { "props", "pickups" })
        {
            Directory.CreateDirectory(Path.Combine(root, set));
            File.Copy(Path.Combine(Pack.Root, set, "manifest.json"), Path.Combine(root, set, "manifest.json"));
        }
        Directory.CreateDirectory(Path.Combine(root, ThingArt.Set));
        File.WriteAllText(Path.Combine(root, ThingArt.Set, "manifest.json"), """
            { "props": { "chest": { "variants": { "closed": "chest_closed.png" }, "size": [26, 26], "anchor": [13, 25], "frames": 1, "fps": 0,
              "blocks": true, "cover": "half", "height": 1, "tags": ["interactable"] } } }
            """);
        var art = ThingArt.Load(root);
        var open = EveryState().Single(t => t.Id == "chest.open");

        Assert.Equal(ThingSource.Art, art.Pick(EveryState().Single(t => t.Id == "chest.closed")).Source);
        Assert.Equal(("props.chest_open", ThingSource.StandIn), (art.Pick(open).Key, art.Pick(open).Source));
        Assert.Equal(ThingSource.Marker, art.Pick(EveryState().Single(t => t.Id == "door.open")).Source);
    }

    [Fact]
    public void Transitions_play_from_their_tags_forwards_or_back()
    {
        var art = ThingArt.Load(WithArt.Value);
        ThingView Of(string id) => EveryState().Single(t => t.Id == id);

        var opening = art.Strip(Of("chest.open"), "closed", "open")!;
        Assert.Equal(("interactables.chest_opening", 4, false), (opening.Key, opening.Frames, opening.Reverse));
        Assert.Equal("interactables/chest_opening.png", opening.Sprite.Path);
        Assert.Equal([0, 1, 2, 3, 3], new[] { 0, 0.1, 0.2, 0.3, 5 }.Select(opening.FrameAt));      // and holds its last frame

        var off = art.Strip(Of("lever.off"), "on", "off")!;      // drawn going on: going off plays it backwards
        Assert.True(off.Reverse);
        Assert.Equal([2, 1, 0], new[] { 0, 0.09, 1 }.Select(off.FrameAt));

        Assert.Equal("interactables.gate_opening", art.Strip(Of("gate.closed"), "open", "closed")!.Key);
        Assert.Equal("interactables.trap_springing", art.Strip(Of("trap.sprung"), "armed", "sprung")!.Key);
        Assert.Null(art.Strip(Of("trap.disarmed"), "armed", "disarmed"));          // the brief draws no disarming
        Assert.Null(art.Strip(Of("note.read"), "unread", "read"));
        Assert.Null(Now.Value.Strip(Of("chest.open"), "closed", "open"));          // nothing to play until the set exists
    }

    [Fact]
    public void Every_stand_in_the_kinds_name_has_a_picture()
    {
        foreach (var key in InteractableKinds.All.Values.SelectMany(k => k.StandIn.Values).Distinct())
            Assert.NotNull(Now.Value.Sprite(key));
    }

    [Fact]
    public void The_brief_names_a_state_for_every_state_a_kind_can_be_drawn_in()
    {
        var states = EveryState().Where(t => t.Kind != "ambient").Select(t => $"interactables.{t.Kind}.{t.State}").ToImmutableSortedSet();
        var keys = ArtManifests.Load(WithArt.Value).Keys;
        Assert.Empty(states.Except(keys));
    }
}
