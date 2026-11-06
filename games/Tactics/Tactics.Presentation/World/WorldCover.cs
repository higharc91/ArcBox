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
/// make progress within <see cref="WorldTuning.OpenTimeout"/>, or exceeds <see cref="WorldTuning.OpenMaxWait"/>, is given up. Godot only applies
/// the numbers.
/// </summary>
public sealed class WorldCover
{
    private double _waiting;
    private double _total;
    private long? _progress;

    /// <summary>Where the card and the panels are.</summary>
    public WorldReveal Reveal { get; private set; } = WorldReveal.Loading;

    /// <summary>The world was opened, or brought back from a fight: the card goes up again, and the wait for it starts again.</summary>
    public void Opened() => (Reveal, _waiting, _total, _progress) = (WorldReveal.Loading, 0, 0, null);

    /// <summary>
    /// A frame of a world at <paramref name="stage"/>, with its fade <paramref name="lifted"/> by so much, 0 to 1. False when the world must
    /// be given up: it failed, made no progress for <see cref="WorldTuning.OpenTimeout"/>, or exceeded the overall wait limit.
    /// When supplied, <paramref name="progress"/> must increase as loading work completes.
    /// </summary>
    public bool Frame(double seconds, WorldStage stage, double lifted, long? progress = null)
    {
        switch (stage)
        {
            case WorldStage.Failed:
                return false;
            case WorldStage.Open:
                Reveal = WorldReveal.Of(open: true, lifted);
                return true;
            default:
                var elapsed = Math.Max(0, seconds);
                _total += elapsed;
                if (progress is { } current && (_progress is null || current > _progress))
                {
                    _progress = current;
                    _waiting = 0;
                }
                else _waiting += elapsed;
                Reveal = WorldReveal.Loading;
                return _waiting < WorldTuning.OpenTimeout && _total < WorldTuning.OpenMaxWait;
        }
    }

    /// <summary>
    /// What to draw: with the world <paramref name="walking"/>, the card as far as it's up and the panels as they come in; otherwise the whole
    /// screen, with the header and table only out of a <paramref name="battle"/>, where the board has their place.
    /// <para>
    /// A fight in the world (overworld 5c) has the world drawn too. Its board, on the world, has the screen as a fight has it: no header, no
    /// table, and none of the world's panels, which would cover the battle's HUD. Its results and its wait to resume (<paramref name="overWorld"/>)
    /// put the header and table straight over the world, with no backdrop and no world panels.
    /// </para>
    /// </summary>
    public CoverLook Look(bool walking, bool battle, bool overWorld = false)
    {
        if (walking && overWorld)
            return new CoverLook(1, Reveal.Card > 0, true, 0);
        var card = walking ? Reveal.Card : 1f;
        var panels = walking && !battle ? Reveal.Panels : 0f;
        return new CoverLook(card, card > 0, !battle && card > 0, panels);
    }
}
