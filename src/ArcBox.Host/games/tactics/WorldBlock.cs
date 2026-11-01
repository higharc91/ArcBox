using System.Diagnostics;
using Godot;

namespace ArcBox.Host.Games.Tactics;

/// <summary>
/// One block of the world: its items recorded into the canvas once, in <see cref="_Draw"/>, and kept there, so moving the camera changes
/// only a transform. It stays hidden, recording nothing, until <see cref="Record"/>. Its colours were baked when its items were prepared, so
/// it reads no light and any number of these can wait unrecorded.
/// </summary>
internal sealed partial class WorldBlock : Node2D
{
    private readonly BoardPainter _painter;
    private readonly object[] _items;

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
        foreach (var item in _items)
        {
            _painter.Paint(this, item, null, 0);
        }
        LastDrawMs = Stopwatch.GetElapsedTime(clock).TotalMilliseconds;
        Recorded++;
        Drawn = true;
    }
}
