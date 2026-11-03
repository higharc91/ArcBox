using Godot;
using Tactics.Game;
using Tactics.Presentation.Board;
using Tactics.Presentation.World;

namespace ArcBox.Host.Games.Tactics;

/// <summary>Small generated props and a single local fire light, positioned in the cached world's art pixels.</summary>
internal sealed partial class CampProps(string root) : Node2D
{
    private readonly Dictionary<string, Texture2D> _textures = new(StringComparer.Ordinal);
    private CampView? _camp;
    private BoardTerrain? _terrain;
    private double _clock;
    private double _stageSeconds;
    private PointLight2D? _fireLight;
    private readonly Node2D _glowing = new() { Material = new CanvasItemMaterial { LightMode = CanvasItemMaterial.LightModeEnum.Unshaded } };

    public override void _Ready()
    {
        AddChild(_glowing);
        var gradient = new Gradient { Offsets = [0, .3f, 1], Colors = [Colors.White, new Color(1, 1, 1, .7f), new Color(1, 1, 1, 0)] };
        var texture = new GradientTexture2D { Gradient = gradient, Width = 256, Height = 256, Fill = GradientTexture2D.FillEnum.Radial, FillFrom = new Vector2(.5f, .5f), FillTo = new Vector2(1, .5f) };
        _fireLight = new PointLight2D { Texture = texture, TextureScale = .85f, Color = new Color(1, .67f, .32f), Energy = 1.2f, RangeZMin = -4096, RangeZMax = 4096, RangeLayerMin = -1, RangeLayerMax = -1 };
        AddChild(_fireLight);
        _glowing.Draw += DrawFire;
    }
    public void ShowCamp(CampView? camp, BoardTerrain terrain, double seconds, double delta)
    {
        _clock += delta; _camp = camp; _terrain = terrain; _stageSeconds = seconds;
        Visible = camp is { Mode: "camp", Stage: not "vote" };
        if (!Visible) return;
        var feet = UnitPlace.On(terrain, camp!.At).Feet;
        if (_fireLight is not null)
        {
            _fireLight.Position = new Vector2(feet.X, feet.Y - 10);
            _fireLight.Energy = camp.Stage == "dawn" ? 1.1f * (1 - (float)Math.Clamp(seconds / 5, 0, 1)) : camp.Stage == "sleep" ? Mathf.Lerp(1.1f, .3f, (float)Math.Clamp(seconds / 3, 0, 1)) : 1.1f + .08f * MathF.Sin((float)_clock * 7);
        }
        QueueRedraw(); _glowing.QueueRedraw();
    }
    private Texture2D? Texture(string name)
    {
        if (_textures.TryGetValue(name, out var found)) return found;
        var path = Path.Combine(root, "camp", name + ".png");
        if (!File.Exists(path)) return null;
        return _textures[name] = ImageTexture.CreateFromImage(Image.LoadFromFile(path));
    }
    private void Prop(string name, Vector2 feet, Vector2 anchor)
    { if (Texture(name) is { } tex) DrawTexture(tex, feet - anchor); }
    public override void _Draw()
    {
        if (_camp is not { } camp || _terrain is null) return;
        var fire = UnitPlace.On(_terrain, camp.At).Feet;
        var at = new Vector2(fire.X, fire.Y);
        Prop("shelter", at + new Vector2(-85, -23), new Vector2(32, 59));
        foreach (var person in camp.People)
        {
            var feet = UnitPlace.On(_terrain, person.At).Feet;
            if (Texture("bedroll") is { } roll) DrawTextureRect(roll, new Rect2(feet.X - 12, feet.Y - 8, 24, 16), false);
            if (person.WellFed && camp.Stage is not ("sleep" or "dawn") && Texture("bowl") is { } bowl)
                DrawTextureRect(bowl, new Rect2(feet.X + 7, feet.Y - 2, 12, 12), false);
        }
        var cooking = camp.People.Any(p => p.Job == "cook");
        if (cooking && camp.Stage is not ("sleep" or "dawn"))
            Prop("pot_" + (int)(_clock * 5 % 5), at + new Vector2(35, 5), new Vector2(24, 43));
        // Small drifting particles differ by locale; the ground itself remains the authored region.
        for (var i = 0; i < 7; i++)
        {
            var age = (float)((_clock * .18 + i * .19) % 1);
            var x = at.X + MathF.Sin(i * 4 + (float)_clock * .25f) * 100;
            var y = at.Y - 8 - age * 42;
            var alpha = MathF.Sin(age * MathF.PI) * .5f;
            DrawCircle(new Vector2(x, y), .65f, CampScene.Theme(camp) == "memorial" ? new Color(.7f, .85f, 1, alpha) : new Color(.9f, 1, .4f, alpha));
        }
    }
    private void DrawFire()
    {
        if (_camp is not { } camp || _terrain is null) return;
        var fire = UnitPlace.On(_terrain, camp.At).Feet;
        var name = camp.Stage == "dawn" && _stageSeconds > 3 ? "fire_unlit" : "fire_" + (int)(_clock * 6 % 5);
        if (Texture(name) is { } tex) _glowing.DrawTexture(tex, new Vector2(fire.X - 24, fire.Y - 44));
    }
}
