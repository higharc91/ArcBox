using System.Collections.Immutable;

namespace Tactics.Presentation.Board;

/// <summary>Something of a <see cref="BoardPlan"/>, ready to draw in its light, at <see cref="Order"/>.</summary>
public abstract record LitItem(long Order);

/// <summary>A face with what it needs from the light.</summary>
/// <param name="Face">The face, or one cell of it.</param>
/// <param name="Probes">One per corner; empty if light doesn't change it.</param>
/// <param name="Art">Draw its art.</param>
/// <param name="Glow">Draw what glows of its art over that, unlit.</param>
public sealed record LitFace(Face Face, ImmutableArray<LightProbe> Probes, bool Art, bool Glow) : LitItem(Face.Order)
{
    /// <summary>A corner's colour now: the face's own, times the light there if it takes any.</summary>
    public Rgb ColourAt(int corner, BoardLighting lighting) => Probes.IsEmpty ? Face.Tint : Face.Tint.Times(lighting.At(Probes[corner]));
}

/// <summary>A sprite with what it needs from the light.</summary>
/// <param name="Sprite">The sprite.</param>
/// <param name="Probe">Where it reads the light, or null if it doesn't.</param>
/// <param name="Glow">Draw what glows of it over that, unlit.</param>
public sealed record LitSprite(Sprite Sprite, LightProbe? Probe, bool Glow) : LitItem(Sprite.Order)
{
    /// <summary>What to modulate it by now.</summary>
    public Rgb LightNow(BoardLighting lighting) => Probe is null ? new Rgb(1, 1, 1) : lighting.At(Probe);
}

/// <summary>
/// A plan in its light, in the plan's order: what the board draws, and all it decides about light. A top that light reaches becomes four
/// cells, so a pool can peak inside a tile, then the whole face again for what goes once over all of them: its glow and its outline. Pure
/// and safe off the main thread, as long as nothing there calls <see cref="BoardLighting.Step"/>: a world's blocks share one
/// <see cref="BoardLighting"/> and each builds its own of these, so a pool doesn't stop at a block's edge.
/// </summary>
/// <param name="Items">Back to front.</param>
/// <param name="Lighting">The light they read.</param>
public sealed record LitBoard(ImmutableArray<LitItem> Items, BoardLighting Lighting)
{
    /// <summary>The items of <paramref name="plan"/> in <paramref name="lighting"/>.</summary>
    public static LitBoard Build(BoardPlan plan, BoardLighting lighting)
    {
        var items = ImmutableArray.CreateBuilder<LitItem>(plan.Items.Length);
        foreach (var item in plan.Items)
        {
            switch (item)
            {
                case Face face:
                    AddFace(items, face, lighting);
                    break;
                case Sprite sprite:
                    items.Add(Of(sprite, lighting));
                    break;
                default:
                    throw new InvalidOperationException($"Unknown board item {item.GetType().Name}.");
            }
        }
        return new LitBoard(items.ToImmutable(), lighting);
    }

    private static void AddFace(ImmutableArray<LitItem>.Builder items, Face face, BoardLighting lighting)
    {
        var lit = lighting.Active && face.Lit;
        var glow = lighting.Active && face.Image?.Glow is not null;
        if (!lit || face.Kind != FaceKind.Top || !Reached(face, lighting))
        {
            items.Add(new LitFace(face, lit ? Probes(face, lighting) : [], true, glow));
            return;
        }
        foreach (var cell in face.Cells())
            items.Add(new LitFace(cell, Probes(cell, lighting), true, false));
        if (glow || face.Grid)
            items.Add(new LitFace(face, [], false, glow));
    }

    private static bool Reached(Face face, BoardLighting lighting)
        => face.Points.Append((face.Points[0] + face.Points[2]) / 2).Any(p => lighting.Probe(p).Count > 0);

    private static ImmutableArray<LightProbe> Probes(Face face, BoardLighting lighting) => [.. face.Points.Select(lighting.Probe)];

    private static LitSprite Of(Sprite sprite, BoardLighting lighting)
        => new(sprite, lighting.Active && sprite.Lit ? lighting.Probe(sprite.TopLeft + sprite.Size / 2) : null, lighting.Active && sprite.Light is not null);
}
