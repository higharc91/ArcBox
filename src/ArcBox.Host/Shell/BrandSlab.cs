using Godot;

namespace ArcBox.Host.Shell;

/// <summary>The red slab with a sliced right edge and black-italic white text (<c>.brand</c> in broadcast.css).</summary>
public partial class BrandSlab : Control
{
    private string _text = "ARCBOX";
    private int _fontSize = 44;

    public BrandSlab()
    {
    }

    public BrandSlab(string text, int fontSize = 44)
    {
        _text = text;
        _fontSize = fontSize;
    }

    public string Text
    {
        get => _text;
        set
        {
            _text = value;
            UpdateMinimumSize();
            QueueRedraw();
        }
    }

    public Color Fill { get; set; } = ArcTheme.Red;

    private float Slant => Size.Y * 0.16f;

    public override Vector2 _GetMinimumSize()
    {
        var w = ArcTheme.BlackItalic.GetStringSize(_text, HorizontalAlignment.Left, -1, _fontSize).X;
        return new Vector2(w + (_fontSize * 1.1f) + 24, _fontSize * 1.9f);
    }

    public override void _Draw()
    {
        var w = Size.X;
        var h = Size.Y;
        DrawColoredPolygon([new(0, 0), new(w, 0), new(w - Slant, h), new(0, h)], Fill);

        var font = ArcTheme.BlackItalic;
        var ascent = font.GetAscent(_fontSize);
        var descent = font.GetDescent(_fontSize);
        var textW = font.GetStringSize(_text, HorizontalAlignment.Left, -1, _fontSize).X;
        var x = ((w - Slant - textW) / 2) + 4;
        var y = (h + ascent - descent) / 2;
        DrawString(font, new Vector2(x, y), _text, HorizontalAlignment.Left, -1, _fontSize, Colors.White);
    }
}
