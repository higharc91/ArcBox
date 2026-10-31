using Godot;

namespace Tactics.World.Editor;

/// <summary>A spawn, under <c>Spawns</c>. A region has exactly one of each kind.</summary>
[Tool]
[GlobalClass]
public partial class SpawnMarker : Marker2D
{
    /// <summary>What a spawn is for.</summary>
    public enum SpawnKind
    {
        /// <summary>Where the party starts.</summary>
        Start,

        /// <summary>Where the party recovers after a defeat.</summary>
        Recovery,
    }

    private static readonly Color Ink = new(0.5f, 1f, 0.6f);

    private SpawnKind _kind;

    /// <summary>Which spawn this is.</summary>
    [Export]
    public SpawnKind Kind
    {
        get => _kind;
        set
        {
            _kind = value;
            QueueRedraw();
        }
    }

    /// <inheritdoc />
    public override void _Draw()
    {
        if (Engine.IsEditorHint())
            DrawString(ThemeDB.FallbackFont, new Vector2(10, -6), Kind.ToString(), modulate: Ink);
    }
}
