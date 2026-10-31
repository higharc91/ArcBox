using System.Text.Json;
using Tactics.Logic;
using Tactics.Presentation.Board;

namespace Tactics.Presentation.Tests.Board;

/// <summary>The real base pack, for tests that check the board against the content it draws.</summary>
internal static class Pack
{
    public static readonly string[] Maps = ["river_crossing", "forest_ruins", "crypt_hall", "warlord_bridge", "crystal_cave", "keep_gate", "hall_of_the_flame"];

    private static readonly Lazy<string> RootDir = new(() =>
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "ArcBox.slnx")))
                return Path.Combine(dir.FullName, "content", "tactics", "base");
        throw new DirectoryNotFoundException("No ArcBox.slnx above the test binaries.");
    });

    private static readonly Lazy<ArtIndex> ArtOnce = new(() => ArtIndex.Load(Root));

    public static string Root => RootDir.Value;

    public static ArtIndex Art => ArtOnce.Value;

    /// <summary>The columns of an animation, by a set's manifest: what the art says, so a redraw doesn't need a test changed.</summary>
    public static int[] Frames(string set, string anim)
    {
        using var doc = Manifest(set);
        return [.. doc.RootElement.GetProperty("anims").GetProperty(anim).GetProperty("frames").EnumerateArray().Select(f => f.GetInt32())];
    }

    /// <summary>A set's manifest, parsed.</summary>
    public static JsonDocument Manifest(string set) => JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, set, "manifest.json")));

    public static BattleMap Map(string id) => BoardMap.Parse(File.ReadAllText(Path.Combine(Root, "rules", "maps", $"{id}.json")));
}
