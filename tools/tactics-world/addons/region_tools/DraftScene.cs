using Godot;
using Tactics.Logic;
using Tactics.WorldTools;

namespace Tactics.World.Editor;

/// <summary>
/// The <c>--draft</c> command: writes Claude's draft of a region into its scene. Painted work is never overwritten:
/// <see cref="RegionScene.Save"/> refuses a scene that exists.
/// </summary>
public static class DraftScene
{
    /// <summary>The drafts there are, by region id.</summary>
    private static readonly Dictionary<string, Func<RulesPack, RegionSource>> Drafts = new(StringComparer.Ordinal)
    {
        ["ashen_ford"] = AshenFordDraft.Build,
    };

    /// <summary>The dressers there are, by region id.</summary>
    private static readonly Dictionary<string, Action<RegionSource, RulesPack?>> Dressers = new(StringComparer.Ordinal)
    {
        ["ashen_ford"] = AshenFordDraft.Dress,
    };

    /// <summary>Writes the draft of region <paramref name="id"/>. Returns 0 if it did, 2 if there's no such draft, and throws <see cref="SceneExistsException"/> if the scene exists.</summary>
    public static int Run(string id)
    {
        if (!Drafts.TryGetValue(id, out var draft))
        {
            GD.PrintErr($"There's no draft of '{id}'. There are: {string.Join(", ", Drafts.Keys)}.");
            return 2;
        }

        var path = RegionPaths.Scene(id);
        RegionScene.Write(draft(RulesPack.Load([RegionPaths.Rules])), path);
        GD.Print($"Wrote {path}.");
        return 0;
    }

    /// <summary>
    /// Dresses the existing scene of region <paramref name="id"/> with props.
    /// Returns 0 on success, 2 if there's no such draft or the scene is missing,
    /// and throws <see cref="InvalidOperationException"/> if the scene already has props.
    /// Terrain is never rewritten: only the Props layer is added.
    /// </summary>
    public static int Dress(string id)
    {
        if (!Dressers.TryGetValue(id, out var dress))
        {
            GD.PrintErr($"There's no dressing for '{id}'. There are: {string.Join(", ", Dressers.Keys)}.");
            return 2;
        }

        var path = RegionPaths.Scene(id);
        if (ResourceLoader.Load<PackedScene>(path)?.Instantiate() is not Region root)
        {
            GD.PrintErr($"{path} isn't a region scene.");
            return 2;
        }

        try
        {
            var existingProps = root.GetNodeOrNull<TileMapLayer>(RegionScene.PropsLayer);
            if (existingProps is not null && existingProps.GetUsedCells().Count > 0)
                throw new InvalidOperationException($"The scene {path} already has props.");

            var rules = RulesPack.Load([RegionPaths.Rules]);
            var painted = RegionScene.Read(root);
            var (source, _) = painted.ToSource();
            dress(source, rules);

            var props = existingProps;
            if (props is null)
            {
                var propsSet = GD.Load<TileSet>(Palette.Props);
                props = RegionScene.AddLayer(root, RegionScene.PropsLayer, propsSet);
                root.MoveChild(props, 3);
            }

            foreach (var group in source.Props)
            {
                var (px, py) = PaletteBuilder.PropAtlas(group.Id, group.Variant);
                foreach (var tile in group.At)
                    props.SetCell(new Vector2I(tile.X, tile.Y), 0, new Vector2I(px, py));
            }

            RegionScene.Save(root, path, overwrite: true);
            GD.Print($"Dressed {path} with {source.Props.Sum(p => p.At.Length)} props.");
            return 0;
        }
        finally
        {
            root.Free();
        }
    }
}
