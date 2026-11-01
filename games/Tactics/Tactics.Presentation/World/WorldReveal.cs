namespace Tactics.Presentation.World;

/// <summary>
/// What's over the world while it opens: the region's title card, and the world's own panels (interaction review 5b, item 14). Phones are
/// live as soon as the world is asked for, and the world takes a while to build, so the title card says where the party is until the world
/// is there to see. It stays whole while the world builds behind it, then gives way to the world as the world's fade lifts, and is gone
/// once the world is <c>Revealed</c>. The panels come in over the second half, once the card is mostly gone, so they never sit on the card's
/// own header. Coming back from a fight is the same.
/// </summary>
/// <param name="Card">How much of the title card shows, 0 to 1.</param>
/// <param name="Panels">How much of the world's panels shows, 0 to 1.</param>
public readonly record struct WorldReveal(float Card, float Panels)
{
    /// <summary>The title card, whole, and no panels: the world isn't there yet.</summary>
    public static readonly WorldReveal Loading = new(1, 0);

    /// <summary>The world as it is when <paramref name="open"/> and its fade has <paramref name="lifted"/> by so much, 0 to 1.</summary>
    public static WorldReveal Of(bool open, double lifted)
    {
        if (!open || double.IsNaN(lifted))
            return Loading;
        var shown = (float)Math.Clamp(lifted, 0, 1);
        return new WorldReveal(1 - shown, Math.Clamp(2 * shown - 1, 0, 1));
    }

    /// <summary>True while any of the title card shows.</summary>
    public bool CardUp => Card > 0;

    /// <summary>True once the world is all there is: no card, and the panels whole.</summary>
    public bool Revealed => Card <= 0;
}
