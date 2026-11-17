using Meridian.Presentation.Art;
using Meridian.Presentation.Board;

namespace Meridian.Presentation.Tests.Art;

/// <summary>Tile and hatch art on a board (M5 plan, Task 2), with the approved samples' set: some rooms art, some placeholders.</summary>
public sealed class BoardArtTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("meridian-board-art-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    /// <summary>The four tiles #357 drew (bridge, med bay, reactor core, airlock), each a real 64 px PNG header.</summary>
    private MeridianArt SampleArt()
    {
        Directory.CreateDirectory(Path.Combine(_root, "tiles"));
        var entries = new List<string>();
        foreach (var (id, deck) in new[] { ("bridge", "command"), ("med_bay", "habitat"), ("reactor_core", "engineering"), ("airlock", "habitat") })
        {
            var bytes = new byte[33];
            new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G' }.CopyTo(bytes, 0);
            bytes[19] = 64;
            bytes[23] = 64;
            bytes[32] = (byte)id.Length;
            File.WriteAllBytes(Path.Combine(_root, "tiles", $"{id}.png"), bytes);
            entries.Add($"\"{id}\": {{ \"file\": \"{id}.png\", \"deck\": \"{deck}\" }}");
        }
        File.WriteAllText(Path.Combine(_root, "tiles", "manifest.json"), $"{{ \"tiles\": {{ {string.Join(", ", entries)} }} }}");
        return MeridianArt.Load(_root, BasePack.Pack);
    }

    [Fact]
    public void A_board_mixes_art_and_placeholders_room_by_room()
    {
        // Review focus 1: the start layout plus a med bay; the airlock and med bay have art, the docking ring and lift don't.
        var art = SampleArt();
        var game = new Harness(players: 3, seed: 1).Board();
        var rooms = ((MeridianHostView)game.Game.HostView()).Compartments;
        var drawn = TileDraw.Of(rooms, DeckId.Habitat).ToDictionary(t => t.Tile, t => art.Tile(t.Tile, t.At.Deck) is not null);
        Assert.True(drawn["airlock"]);
        Assert.False(drawn["docking_ring"]);
        Assert.False(drawn["central_lift"]);
    }

    [Fact]
    public void Art_drawn_for_one_deck_is_still_the_tiles_art_on_its_own_deck()
    {
        var art = SampleArt();
        Assert.NotNull(art.Tile("bridge", DeckId.Command));
        Assert.NotNull(art.Tile("bridge"));
    }
}
