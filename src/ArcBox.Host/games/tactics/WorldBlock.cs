using System.Diagnostics;
using Godot;

namespace ArcBox.Host.Games.Tactics;

/// <summary>
/// One block of the world: its items recorded into the canvas once, in <see cref="_Draw"/>, and kept there, so moving the camera changes
/// only a transform. It stays hidden, recording nothing, until <see cref="Record"/>. Its colours were baked when its items were prepared, so
/// it reads no light and any number of these can wait unrecorded. Everything it holds is in painter's order, tall props too, so it's right against the ground and
/// against itself. A faded prop is left out (<see cref="Leave"/>), which records it again, once as the prop starts to fade and once as it comes back.
/// </summary>
internal sealed partial class WorldBlock : Node2D
{
    private readonly BoardPainter _painter;
    private readonly HashSet<int> _left = [];
    private object[] _items;
    private bool _again;

    public WorldBlock(BoardPainter painter, object[] items, int z)
    {
        _painter = painter;
        _items = items;
        ZIndex = z;
        Visible = false;
    }

    /// <summary>How many items it paints.</summary>
    public int Items => _items.Length;

    /// <summary>How many times it has been recorded.</summary>
    public int Recorded { get; private set; }

    /// <summary>How many times it has been recorded again for what was left out.</summary>
    public int Rerecorded { get; private set; }

    /// <summary>How long the last recording took, in milliseconds.</summary>
    public double LastDrawMs { get; private set; }

    /// <summary>True when a recording has finished and hasn't been read with <see cref="TakeDrawn"/>.</summary>
    public bool Drawn { get; private set; }

    /// <summary>Reads the finished recording's time, in milliseconds.</summary>
    public double TakeDrawn()
    {
        Drawn = false;
        return LastDrawMs;
    }

    /// <summary>Attaches the textures of its items. Only on the main thread.</summary>
    public void Bind()
    {
        foreach (var item in _items)
        {
            _painter.Bind(item);
        }
    }

    /// <summary>Leaves the item at <paramref name="item"/> out of the recording, or puts it back. A block that's yet to be recorded just remembers.</summary>
    public void Leave(int item, bool left)
    {
        if ((left ? _left.Add(item) : _left.Remove(item)) && Visible)
        {
            _again = true;
            QueueRedraw();
        }
    }

    /// <summary>
    /// Paints <paramref name="items"/>, prepared and bound, from now on, and records them at the end of this frame if it's been recorded before:
    /// a fight in the world changed the ground here, or it heals.
    /// </summary>
    public void Replace(object[] items)
    {
        _items = items;
        if (Visible)
        {
            QueueRedraw();
        }
    }

    /// <summary>Records it at the end of this frame, or again if it's already been recorded.</summary>
    public void Record()
    {
        if (Visible)
        {
            QueueRedraw();
        }
        Visible = true;
    }

    public override void _Draw()
    {
        var clock = Stopwatch.GetTimestamp();
        for (var i = 0; i < _items.Length; i++)
        {
            if (!_left.Contains(i))
            {
                _painter.Paint(this, _items[i], null, 0);
            }
        }
        LastDrawMs = Stopwatch.GetElapsedTime(clock).TotalMilliseconds;
        if (_again)
        {
            _again = false;
            Rerecorded++;
            return;
        }
        Recorded++;
        Drawn = true;
    }
}
