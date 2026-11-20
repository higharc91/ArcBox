using System.Buffers.Binary;
using Meridian.Presentation.Art;
using Meridian.Presentation.Hud;

namespace Meridian.Presentation.Tests.Hud;

public sealed class UiKitTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("meridian-ui-art-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Png(string set, string name, int w, int h)
    {
        var bytes = new byte[33];
        new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G' }.CopyTo(bytes, 0);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(16), w);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(20), h);
        bytes[32] = (byte)name.Length;
        Directory.CreateDirectory(Path.Combine(_root, set));
        File.WriteAllBytes(Path.Combine(_root, set, name), bytes);
        return $"\"file\": \"{name}\"";
    }

    [Fact]
    public void Each_element_wears_its_piece_else_the_generic_panel_else_nothing()
    {
        Assert.Null(UiKit.For(MeridianArt.None, UiElement.Log));                 // no kit: the M4 flat panel
        var panel = Png("ui", "panel.png", 32, 32);
        var log = Png("ui", "log.png", 32, 32);
        File.WriteAllText(Path.Combine(_root, "ui", "manifest.json"), $$"""
            { "ui": { "panel": { {{panel}}, "insets": [8, 8, 8, 8] }, "logPanel": { {{log}}, "insets": [6, 6, 6, 6] } } }
            """);
        var art = MeridianArt.Load(_root, BasePack.Pack);
        Assert.EndsWith("log.png", UiKit.For(art, UiElement.Log)!.Path);
        Assert.EndsWith("panel.png", UiKit.For(art, UiElement.DiceTray)!.Path);   // no tray of its own: the panel
        Assert.Null(UiKit.For(art, UiElement.BreachPlate));                        // a plate never borrows the panel
    }

    [Fact]
    public void A_card_panel_wears_its_decks_frame_first()
    {
        var frame = Png("cards", "anomaly.png", 48, 64);
        File.WriteAllText(Path.Combine(_root, "cards", "manifest.json"), $$"""{ "frames": { "anomaly": { {{frame}}, "insets": [8, 12, 8, 12] } } }""");
        var art = MeridianArt.Load(_root, BasePack.Pack);
        Assert.EndsWith("anomaly.png", UiKit.For(art, UiElement.CardPanel, CardKind.Anomaly)!.Path);
        Assert.Null(UiKit.For(art, UiElement.CardPanel, CardKind.Salvage));
    }

    [Fact]
    public void Dice_use_their_faces_and_roll_strip_when_there()
    {
        var roll = Png("ui", "die-roll.png", 96, 24);
        var face1 = Png("ui", "die-1.png", 24, 24);
        File.WriteAllText(Path.Combine(_root, "ui", "manifest.json"), $$"""
            { "ui": { "die": { "roll": { {{roll}}, "frames": 4, "fps": 12 }, "face1": { {{face1}} } } } }
            """);
        var art = MeridianArt.Load(_root, BasePack.Pack);
        Assert.Equal(4, UiKit.DieRoll(art)!.Frames);
        Assert.NotNull(UiKit.DieFace(art, 1));
        Assert.Null(UiKit.DieFace(art, 2));   // the drawn die stands in
    }

    [Fact]
    public void Every_element_names_a_piece_in_the_contract_and_its_doc()
    {
        var doc = File.ReadAllText(Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(BasePack.Root)!)!, "..", "tools", "meridian-assets", "CONTRACT.md"));
        foreach (var element in Enum.GetValues<UiElement>())
        {
            Assert.Contains(UiKit.Piece(element), ArtContract.UiPieces);
            Assert.Contains($"| `{UiKit.Piece(element)}` |", doc);
        }
    }
}
