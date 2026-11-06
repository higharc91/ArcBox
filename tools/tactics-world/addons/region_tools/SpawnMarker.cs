using Godot;

namespace Tactics.World.Editor;

/// <summary>A spawn, under <c>Spawns</c>. A region has exactly one of each kind.</summary>
[Tool]
[GlobalClass]
public partial class SpawnMarker : TileMarker
{
    /// <summary>What a spawn is for.</summary>
    public enum SpawnKind
    {
        /// <summary>Where the party starts.</summary>
        Start,

        /// <summary>Where the party recovers after a defeat.</summary>
        Recovery,

        /// <summary>An authored entrance destination.</summary>
        Named,
    }

    private static readonly Color Ink = new(0.5f, 1f, 0.6f);

    private SpawnKind _kind;

    /// <summary>The destination name, used when Kind is Named.</summary>
    [Export]
    public string SpawnName { get; set; } = "";

    /// <inheritdoc />
    public override string Group => RegionScene.Spawns;

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
        if (!Engine.IsEditorHint())
            return;
        DrawTile(Ink);
        DrawString(ThemeDB.FallbackFont, new Vector2(18, -6), Kind == SpawnKind.Named ? SpawnName : Kind.ToString(), modulate: Ink);
    }
}
