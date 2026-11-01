using Tactics.Logic;

namespace Tactics.WorldTools.Tests;

public sealed class SceneTextTests : IDisposable
{
    private const string Materials = """
        [gd_resource type="TileSet" format=3]

        [sub_resource type="TileSetAtlasSource" id="TileSetAtlasSource_a"]
        resource_name = "grass/lush"

        [resource]
        tile_size = Vector2i(32, 32)
        sources/0 = SubResource("TileSetAtlasSource_a")
        """;

    private const string Head = """
        [gd_scene format=4 uid="uid://x"]

        [ext_resource type="Script" uid="uid://a" path="res://addons/region_tools/Region.cs" id="1_a"]
        [ext_resource type="Script" uid="uid://b" path="res://addons/region_tools/BeatMarker.cs" id="2_b"]
        [ext_resource type="Script" uid="uid://c" path="res://addons/region_tools/StampMarker.cs" id="3_c"]
        [ext_resource type="Script" uid="uid://d" path="res://addons/region_tools/SpawnMarker.cs" id="4_d"]

        [node name="Region" type="Node2D" unique_id=1]
        script = ExtResource("1_a")
        Id = "r"
        Name = "The \"Old\" Ford"
        Size = Vector2i(64, 32)

        [node name="Material" type="TileMapLayer" parent="." unique_id=2 index="0"]

        [node name="Height" type="TileMapLayer" parent="." unique_id=3]

        [node name="Flags" type="TileMapLayer" parent="." unique_id=4]

        [node name="Beats" type="Node2D" parent="." unique_id=5]

        [node name="Stamps" type="Node2D" parent="." unique_id=6]

        [node name="Spawns" type="Node2D" parent="." unique_id=7]

        """;

    private readonly DirectoryInfo _folder = Directory.CreateTempSubdirectory("tactics-scene-text-");

    public void Dispose() => _folder.Delete(recursive: true);

    private PaintedRegion Read(string rest, string head = Head)
    {
        var (scene, materials) = (Path.Combine(_folder.FullName, "r.tscn"), Path.Combine(_folder.FullName, "materials.tres"));
        File.WriteAllText(scene, head + rest);
        File.WriteAllText(materials, Materials);
        return SceneText.Read(scene, materials);
    }

    /// <summary>A <c>tile_map_data</c> value holding one cell on source <paramref name="source"/>.</summary>
    private static string TileData(int x, int y, int source)
    {
        var bytes = new byte[14];
        BitConverter.TryWriteBytes(bytes.AsSpan(2), (short)x);
        BitConverter.TryWriteBytes(bytes.AsSpan(4), (short)y);
        BitConverter.TryWriteBytes(bytes.AsSpan(6), (short)source);
        return $"PackedByteArray(\"{Convert.ToBase64String(bytes)}\")";
    }

    [Fact]
    public void A_name_is_unescaped_in_a_value_and_in_a_node_header()
    {
        var region = Read("""
            [node name="say \"hi\"" type="Marker2D" parent="Beats" unique_id=8]
            position = Vector2(48, 16)
            script = ExtResource("2_b")
            Beat = "a\"b"

            """);

        Assert.Equal("The \"Old\" Ford", region.Name);
        Assert.Equal("a\"b", Assert.Single(region.Beats).Beat);
    }

    [Fact]
    public void A_stamp_at_tile_0_0_has_no_position_line()
    {
        var region = Read("""
            [node name="river_crossing" type="Marker2D" parent="Stamps" unique_id=8]
            script = ExtResource("3_c")
            Map = "river_crossing"

            """);

        Assert.Equal(new RegionStamp("river_crossing", new Tile(0, 0), 0), Assert.Single(region.Stamps));
        Assert.Empty(region.MovedNodes);
    }

    [Fact]
    public void A_source_dropped_from_the_palette_is_named_not_a_crash()
    {
        var head = Head.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("""unique_id=2 index="0"]""" + "\n", """unique_id=2 index="0"]""" + "\n" + "tile_map_data = " + TileData(3, 4, 7) + "\n", StringComparison.Ordinal);

        Assert.Equal("source 7", Read("", head).Materials[new Tile(3, 4)]);
    }

    [Fact]
    public void A_known_source_is_named()
    {
        var head = Head.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("""unique_id=2 index="0"]""" + "\n", """unique_id=2 index="0"]""" + "\n" + "tile_map_data = " + TileData(3, 4, 0) + "\n", StringComparison.Ordinal);

        Assert.Equal("grass/lush", Read("", head).Materials[new Tile(3, 4)]);
    }

    [Fact]
    public void A_node_header_can_hold_several_ints()
    {
        var region = Read("""
            [node name="b" type="Marker2D" parent="Beats" unique_id=8 instance_placeholder=9 index=2]
            position = Vector2(48, 16)
            script = ExtResource("2_b")
            Beat = "b"

            """);

        Assert.Equal(new BeatSpot("b", new Tile(1, 0)), Assert.Single(region.Beats));
    }

    [Fact]
    public void A_stamps_old_areas_are_read()
    {
        var old = new RegionStamp("river_crossing", new Tile(6, 5), 1);
        var region = Read($$"""
            [node name="river_crossing" type="Marker2D" parent="Stamps" unique_id=8]
            script = ExtResource("3_c")
            Map = "river_crossing"
            PendingAreas = PackedStringArray("{{StampBrush.Encode(old)}}")

            """);

        Assert.Equal([old], region.OldStamps);
    }

    [Fact]
    public void A_moved_layer_or_group_is_reported()
    {
        var head = Head.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("""[node name="Height" type="TileMapLayer" parent="." unique_id=3]""" + "\n", """[node name="Height" type="TileMapLayer" parent="." unique_id=3]""" + "\nposition = Vector2(32, 0)\n", StringComparison.Ordinal)
            .Replace("""[node name="Beats" type="Node2D" parent="." unique_id=5]""" + "\n", """[node name="Beats" type="Node2D" parent="." unique_id=5]""" + "\nrotation = 1.5\n", StringComparison.Ordinal);

        Assert.Equal(["Height", "Beats"], Read("", head).MovedNodes);
    }

    [Fact]
    public void A_marker_under_the_wrong_group_and_a_plain_node_in_a_group_are_strays()
    {
        var region = Read("""
            [node name="gate" type="Marker2D" parent="Beats" unique_id=8]
            position = Vector2(64, 32)
            script = ExtResource("3_c")
            Map = "river_crossing"

            [node name="note" type="Node2D" parent="Stamps" unique_id=9]
            position = Vector2(32, 0)

            [node name="lost" type="Marker2D" parent="." unique_id=10]
            script = ExtResource("4_d")

            """);

        Assert.Empty(region.Stamps);
        Assert.Empty(region.Starts);
        Assert.Equal(
            [
                new StrayNode("StampMarker 'gate'", "Beats", "Move it under Stamps", new Tile(2, 1)),
                new StrayNode("SpawnMarker 'lost'", "the root", "Move it under Spawns", new Tile(0, 0)),
                new StrayNode("node 'note'", "Stamps", "Give it a marker type, or delete it", new Tile(1, 0)),
            ],
            region.Strays);
    }

    [Fact]
    public void A_tile_layer_in_another_format_is_refused()
    {
        var bad = $"PackedByteArray(\"{Convert.ToBase64String(new byte[] { 1, 0 })}\")";
        var head = Head.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("""unique_id=2 index="0"]""" + "\n", """unique_id=2 index="0"]""" + "\n" + "tile_map_data = " + bad + "\n", StringComparison.Ordinal);

        Assert.Throws<InvalidDataException>(() => Read("", head));
    }
}
