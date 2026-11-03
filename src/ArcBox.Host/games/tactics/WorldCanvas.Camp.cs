using Godot;

namespace ArcBox.Host.Games.Tactics;

internal sealed partial class WorldCanvas
{
    private CanvasModulate? _campLight;
    private bool _campNight;
    /// <summary>Modulate the existing world canvas only. Cached world plans, blocks and light bakes remain intact.</summary>
    public void CampLight(bool night)
    {
        if (_campLight is null) { _campLight = new CanvasModulate { Color = Colors.White }; AddChild(_campLight); }
        if (night == _campNight) return;
        _campNight = night;
        var tween = CreateTween();
        tween.TweenProperty(_campLight, "color", night ? new Color(.48f, .50f, .70f) : Colors.White, .8);
    }
}
