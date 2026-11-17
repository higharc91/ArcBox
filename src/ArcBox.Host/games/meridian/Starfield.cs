using Godot;

namespace ArcBox.Host.Games.Meridian;

/// <summary>The back layer (spec §5.5): a slow, seeded drift of stars, so the ship hangs in space rather than on a slate.</summary>
public partial class Starfield : Node2D
{
    private const int Count = 160;
    private readonly (Vector2 At, float Size, float Speed, float Glow)[] _stars = new (Vector2, float, float, float)[Count];
    private Vector2 _size;
    private double _time;

    public override void _Ready()
    {
        var rng = new RandomNumberGenerator { Seed = 0x4d455249 };   // the same sky every game
        for (var i = 0; i < Count; i++)
        {
            _stars[i] = (new Vector2(rng.Randf(), rng.Randf()), rng.RandfRange(1f, 2.6f), rng.RandfRange(2f, 9f), rng.RandfRange(0.25f, 0.8f));
        }
    }

    public override void _Process(double delta)
    {
        _time += delta;
        _size = GetViewportRect().Size;
        QueueRedraw();
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, _size), new Color("07070c"));
        foreach (var (at, size, speed, glow) in _stars)
        {
            var x = (at.X * _size.X - (float)(_time * speed)) % _size.X;
            if (x < 0)
            {
                x += _size.X;
            }
            DrawCircle(new Vector2(x, at.Y * _size.Y), size, new Color(0.75f, 0.82f, 1f, glow));
        }
    }
}
