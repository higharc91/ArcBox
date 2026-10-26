using System.Text.Encodings.Web;
using System.Text.Json;
using Godot;

namespace ArcBox.Host.Shell;

/// <summary>Fallback when a game has no installed TV scene: says so and pretty-prints the latest host view.</summary>
public partial class MissingScene : Control, IGameScene
{
    private const int LinesPerColumn = 31;
    private const int MaxColumns = 6;
    private static readonly JsonSerializerOptions Pretty = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private string _gameId = "";
    private string _scenePath = "";
    private HBoxContainer _columns = null!;
    private Font _mono = null!;
    private Label _meta = null!;
    private int _views;

    public void Setup(string gameId, string? scenePath)
    {
        _gameId = gameId;
        _scenePath = scenePath ?? "(no scene registered in GameSceneMap)";
    }

    public override void _Ready()
    {
        var column = ShellLayout.Frame(this, 24);
        column.AddChild(ShellLayout.Header(
            _gameId.ToUpperInvariant(),
            ArcTheme.Meta("Scene not installed", 18, ArcTheme.Gold),
            ArcTheme.Label("The game is running, but this TV has no scene for it yet", ArcTheme.Bold, 38)));

        _meta = ArcTheme.Meta($"Expected {_scenePath} · waiting for the first view", 16);
        _mono = new SystemFont { FontNames = ["DejaVu Sans Mono", "Consolas", "Menlo", "monospace"] };
        _columns = ArcTheme.HBox(28);
        _columns.SizeFlagsVertical = SizeFlags.ExpandFill;
        _columns.ClipContents = true;

        var panel = ArcTheme.Panel(ArcTheme.GlassBox(null, 32, 22), ArcTheme.VBox(14, _meta, _columns));
        panel.SizeFlagsVertical = SizeFlags.ExpandFill;
        panel.ClipContents = true;
        column.AddChild(panel);
    }

    public void SetShots(Screenshotter? shots)
    {
    }

    public void Render(JsonElement view, JsonElement? previous)
    {
        _views++;
        var lines = JsonSerializer.Serialize(view, Pretty).Split('\n');
        foreach (var child in _columns.GetChildren())
        {
            child.QueueFree();
        }

        var shown = 0;
        for (var c = 0; c < MaxColumns && shown < lines.Length; c++)
        {
            var take = Math.Min(LinesPerColumn, lines.Length - shown);
            var text = string.Join('\n', lines.Skip(shown).Take(take));
            shown += take;
            if (c == MaxColumns - 1 && shown < lines.Length)
            {
                text += $"\n… {lines.Length - shown} more lines";
            }
            var label = ArcTheme.Label(text, _mono, 15, new Color("c9c9d6"));
            label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            label.ClipText = true;
            label.VerticalAlignment = VerticalAlignment.Top;
            label.SizeFlagsVertical = SizeFlags.Fill;
            _columns.AddChild(label);
        }
        _meta.Text = $"Expected {_scenePath} · view #{_views} · {lines.Length} lines of JSON".ToUpperInvariant();
    }
}
