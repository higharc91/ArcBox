using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using Meridian.Presentation.Art;
using Meridian.Presentation.Board;

namespace Meridian.Presentation.Tests.Art;

/// <summary>ArtCheck and MeridianArt (M5 plan, Task 1): the contract as code, and the art the TV draws from it.</summary>
public sealed class MeridianArtTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("meridian-art-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static RulesPack Pack => BasePack.Pack;

    /// <summary>A minimal valid PNG header of the given size (the check reads only the header).</summary>
    private static byte[] Png(int w, int h, byte salt = 0)
    {
        var bytes = new byte[33];
        new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 13, 10, 26, 10, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R' }.CopyTo(bytes, 0);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(16), w);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(20), h);
        bytes[32] = salt;
        return bytes;
    }

    /// <summary>Writes a file into a set and returns its manifest entry's file and sha256.</summary>
    private string File(string set, string name, byte[] bytes)
    {
        Directory.CreateDirectory(Path.Combine(_root, set));
        System.IO.File.WriteAllBytes(Path.Combine(_root, set, name), bytes);
        return $"\"file\": \"{name}\", \"sha256\": \"{Convert.ToHexStringLower(SHA256.HashData(bytes))}\"";
    }

    private void Manifest(string set, string json) => System.IO.File.WriteAllText(Path.Combine(_root, set, "manifest.json"), json);

    [Fact]
    public void Art_with_a_matching_hash_is_used_and_anything_else_falls_back()
    {
        var bridge = File("tiles", "bridge.a.png", Png(64, 64, 1));
        File("tiles", "med_bay.b.png", Png(64, 64, 2));
        Manifest("tiles", $$"""
            { "version": 1, "tiles": {
                "bridge": { {{bridge}}, "deck": "command" },
                "med_bay": { "file": "med_bay.b.png", "sha256": "{{new string('0', 64)}}" },
                "airlock": { "file": "airlock.c.png", "sha256": "{{new string('0', 64)}}" } } }
            """);
        var art = MeridianArt.Load(_root, Pack);
        Assert.NotNull(art.Tile("bridge"));
        Assert.Null(art.Tile("med_bay"));      // its bytes don't match
        Assert.Null(art.Tile("airlock"));      // its file is missing
        Assert.Null(art.Crew("ines_okafor"));  // no crew set at all
        Assert.Equal(2, art.Problems.Count);
        var tiles = art.Coverage.Single(c => c.Set == "tiles");
        Assert.Equal(["bridge"], tiles.Covered);
        Assert.Contains("med_bay", tiles.Missing);   // review focus 1: the rest of the set still draws its placeholders
    }

    [Fact]
    public void The_shipped_pack_passes()
    {
        var check = ArtCheck.Run(Path.GetDirectoryName(BasePack.Root)!, Pack);
        Assert.True(!check.Errors.Any(), check.Report());
    }

    [Fact]
    public void The_approved_samples_manifest_format_is_read()
    {
        // The format of draft PR #357's crew manifest: anchor and frame size on the crew member, strips left to right.
        var idle = File("crew", "ines-idle.a.png", Png(24, 24));
        var walk = File("crew", "ines-walk.b.png", Png(96, 24));
        Manifest("crew", $$"""
            { "version": 1, "crew": { "ines_okafor": { "frameSize": [24, 24], "anchor": [12, 22],
                "idle": { {{idle}}, "frames": 1, "fps": 0 },
                "walk": { {{walk}}, "frames": 4, "fps": 6 } } } }
            """);
        var crew = MeridianArt.Load(_root, Pack).Crew("ines_okafor")!;
        Assert.Equal((4, 6, 12, 22), (crew.WalkFrames, crew.Fps, crew.AnchorX, crew.AnchorY));
    }

    [Fact]
    public void A_deck_variant_is_used_on_its_deck_and_never_on_another()
    {
        // Review focus 3. The storage bay is placeable on more than one deck.
        var tile = Pack.Tiles.First(t => t.Decks.Count > 1);
        var decks = tile.Decks.Select(d => BoardStyle.DeckName(d).ToLowerInvariant()).ToList();
        var variant = File("tiles", "v.png", Png(64, 64, 3));
        Manifest("tiles", $$"""{ "tiles": { "{{tile.Id.Value}}": { "variants": { "{{decks[0]}}": { {{variant}} } } } } }""");
        var art = MeridianArt.Load(_root, Pack);
        Assert.NotNull(art.Tile(tile.Id.Value, tile.Decks[0]));
        Assert.Null(art.Tile(tile.Id.Value, tile.Decks[1]));   // its placeholder, not the other deck's art
    }

    [Theory]
    [InlineData("tiles", "tiles", "not_a_tile", 64, 64, 1, "is not an id the game has")]
    [InlineData("tiles", "tiles", "bridge", 48, 48, 1, "the contract allows 64x64")]
    [InlineData("crew", "crew", "ines_okafor", 98, 24, 4, "doesn't divide into 4 frames")]
    public void Each_kind_of_fault_is_caught(string set, string key, string id, int w, int h, int frames, string message)
    {
        var entry = File(set, "x.png", Png(w, h));
        Manifest(set, $$"""{ "{{key}}": { "{{id}}": { {{entry}}, "frames": {{frames}} } } }""");
        var check = ArtCheck.Run(_root, Pack);
        Assert.Contains(check.Errors, e => e.Message.Contains(message));
        Assert.Empty(check.Entries);   // review focus 2: a rejected file is left out, so it draws as its placeholder
    }

    [Fact]
    public void A_wrong_deck_text_claims_missing_insets_and_a_broken_manifest_are_caught()
    {
        var a = File("tiles", "a.png", Png(64, 64, 4));
        Manifest("tiles", $$"""{ "tiles": { "bridge": { {{a}}, "deck": "engineering" } } }""");
        var frame = File("cards", "f.png", Png(48, 64));
        Manifest("cards", $$"""{ "frames": { "incident": { {{frame}} }, "salvage": { {{frame}}, "text": "SALVAGE" } } }""");
        Directory.CreateDirectory(Path.Combine(_root, "fx"));
        Manifest("fx", "{ not json");
        var errors = ArtCheck.Run(_root, Pack).Errors.Select(e => e.ToString()).ToList();
        Assert.Contains(errors, e => e.Contains("bridge") && e.Contains("engineering deck"));
        Assert.Contains(errors, e => e.Contains("incident") && e.Contains("9-slice insets"));
        Assert.Contains(errors, e => e.Contains("salvage") && e.Contains("claims text"));
        Assert.Contains(errors, e => e.Contains("fx/manifest.json"));
    }

    [Fact]
    public void A_partial_hatch_set_says_which_states_are_missing()
    {
        var open = File("hatches", "open.png", Png(64, 64, 5));
        Manifest("hatches", $$"""{ "states": { "open": { {{open}} } } }""");
        var check = ArtCheck.Run(_root, Pack);
        Assert.Equal(["closed", "sealed", "none"], check.Errors.Where(e => e.Set == "hatches").Select(e => e.Where));
        Assert.NotNull(MeridianArt.Load(_root, Pack).Hatch(HatchState.Open));
    }
}

public sealed class ArtContractDocTests
{
    [Fact]
    public void The_contract_page_names_every_set_and_every_listed_piece()
    {
        var page = File.ReadAllText(Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(BasePack.Root)!)!, "..", "tools", "meridian-assets", "CONTRACT.md"));
        foreach (var set in ArtContract.Sets)
        {
            Assert.Contains($"| `{set.Name}` | `{set.MapKey}` |", page);
        }
        foreach (var piece in ArtContract.UiPieces.Concat(ArtContract.Effects).Concat(ArtContract.TokenKinds).Concat(ArtContract.HatchStates))
        {
            Assert.Contains($"`{piece}`", page);
        }
        foreach (var sound in ArtContract.Sounds)
        {
            Assert.Contains(sound.StartsWith("sfx.") ? $"`{sound[4..]}`" : $"`{sound}`", page);
        }
    }
}
