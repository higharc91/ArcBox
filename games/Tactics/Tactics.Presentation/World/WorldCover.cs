namespace Tactics.Presentation.World;

/// <summary>How far the TV's world has got, as <see cref="WorldCover"/> needs to know it.</summary>
public enum WorldStage
{
    /// <summary>Being built, its textures loading or the blocks in view recording: nothing to see yet.</summary>
    Building,

    /// <summary>Showing, and its fade lifting or lifted.</summary>
    Open,

    /// <summary>It can't be built.</summary>
    Failed,
}

/// <summary>What the screen's own nodes do over the world, this frame.</summary>
/// <param name="Card">How much of the backdrop, header and table shows, 0 to 1.</param>
/// <param name="Backdrop">The backdrop is drawn at all.</param>
/// <param name="HeaderAndTable">The header and the table are drawn: not in a fight, where the board takes their place.</param>
/// <param name="Panels">How much of the world's own panels shows, 0 to 1.</param>
public readonly record struct CoverLook(float Card, bool Backdrop, bool HeaderAndTable, float Panels)
{
    /// <summary>The world's panels are drawn at all.</summary>
    public bool PanelsShown => Panels > 0;
}

/// <summary>
/// The screen over the world, frame by frame (interaction review 5b, item 14): the region's title card while the world opens, then the
/// world. A world that's opened, or brought back from a fight, puts the card up again (<see cref="Opened"/>). A world that fails, or doesn't
/// open within <see cref="WorldTuning.OpenTimeout"/>, is given up, and the screen says where the party is without it. Godot only applies
/// the numbers.
/// </summary>
public sealed class WorldCover
{
    private double _waiting;

    /// <summary>Where the card and the panels are.</summary>
    public WorldReveal Reveal { get; private set; } = WorldReveal.Loading;

    /// <summary>The world was opened, or brought back from a fight: the card goes up again, and the wait for it starts again.</summary>
    public void Opened() => (Reveal, _waiting) = (WorldReveal.Loading, 0);

    /// <summary>
    /// A frame of a world at <paramref name="stage"/>, with its fade <paramref name="lifted"/> by so much, 0 to 1. False when the world must
    /// be given up: it failed, or it hasn't opened in <see cref="WorldTuning.OpenTimeout"/> of frames.
    /// </summary>
    public bool Frame(double seconds, WorldStage stage, double lifted)
    {
        switch (stage)
        {
            case WorldStage.Failed:
                return false;
            case WorldStage.Open:
                Reveal = WorldReveal.Of(open: true, lifted);
                return true;
            default:
                _waiting += Math.Max(0, seconds);
                Reveal = WorldReveal.Loading;
                return _waiting < WorldTuning.OpenTimeout;
        }
    }

    /// <summary>
    /// What to draw: with the world <paramref name="walking"/>, the card as far as it's up and the panels as they come in; otherwise the whole
    /// screen, with the header and table only out of a <paramref name="battle"/>, where the board has their place.
    /// </summary>
    public CoverLook Look(bool walking, bool battle)
    {
        var card = walking ? Reveal.Card : 1f;
        var panels = walking ? Reveal.Panels : 0f;
        return new CoverLook(card, card > 0, !battle && card > 0, panels);
    }
}
