using Godot;
using Tactics.Game;
using Tactics.Presentation.World;

namespace ArcBox.Host.Games.Tactics;

internal sealed partial class WorldCanvas
{
    private CanvasModulate? _campLight;
    private CampProps? _campProps;
    private float _campZoom = 1;
    /// <summary>Transient grade and props; cached ground plans and light bakes stay intact.</summary>
    public void CampLight(CampView? camp, double seconds, bool morning, double delta)
    {
        if (_campLight is null) { _campLight = new CanvasModulate { Color = Colors.White }; AddChild(_campLight); }
        if (Plan is not { } plan || _world is null) return;
        var grade = global::Tactics.Presentation.World.CampLight.Grade(camp, seconds, plan.Lighting.Ambient, morning);
        _campLight.Color = _campLight.Color.Lerp(new Color(grade.R, grade.G, grade.B), (float)(1 - Math.Exp(-delta * 3)));
        var focus = camp is { Mode: "camp", Stage: not "vote" };
        var zoom = focus ? camp!.Stage == "dawn" ? 2 - (float)Math.Clamp((seconds - 5) / 3, 0, 1) : 2 : 1;
        _campZoom = Mathf.MoveToward(_campZoom, zoom, (float)delta * .75f);
        if (_campProps is null)
        {
            _campProps = new CampProps(_painter.Root) { ZIndex = _over.ZIndex - 1 };
            _world.AddChild(_campProps);
        }
        _campProps.ShowCamp(camp, plan.Terrain, seconds, delta);
    }
}
