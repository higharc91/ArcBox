using Godot;
using Meridian.Presentation.Art;
using Meridian.Presentation.Beats;
using Meridian.Presentation.Fx;

namespace ArcBox.Host.Games.Meridian;

/// <summary>
/// The board effects (spec §5.5; M5 plan, Task 6): it draws the cues <see cref="EffectPlan"/> says are playing, at the
/// beat's own pace. The board calls it between its tokens and its crew; the Breach overlay calls it for the screen-wide
/// cues, under its words. Each cue's strip is drawn upright, centred on its place, at the board's scale. With
/// <see cref="Reduced"/> on, only reduced art plays: no flash, no shake.
/// </summary>
public sealed class FxLayer
{
    /// <summary>The TV's reduced effects (<c>ReducedBattleEffects</c>, shared with Tactics).</summary>
    public bool Reduced { get; set; }

    /// <summary>Whether any cue of the current beat is playing now (the board then redraws every frame).</summary>
    public static bool Playing(Director director)
        => director.Current is { } beat && EffectPlan.Playing(beat, director.Elapsed).Any();

    /// <summary>
    /// Draws the playing cues whose place <paramref name="where"/> can put on screen: for each, the centres it plays at
    /// and the scale of one art pixel. Places it doesn't know return nothing.
    /// </summary>
    public void Draw(CanvasItem on, Director director, MeridianArt art, Func<string, Texture2D?> texture,
                     Func<Cue, IEnumerable<(Vector2 At, float Scale)>> where)
    {
        if (director.Current is not { } beat)
        {
            return;
        }
        var t = director.Elapsed;
        foreach (var cue in EffectPlan.Playing(beat, t))
        {
            if (EffectPlan.Art(art, cue.Fx, Reduced) is not { } strip || texture(strip.Path) is not { } sheet)
            {
                continue;   // no art for it (or not in reduced form): the beat plays without it
            }
            var frame = EffectPlan.Frame(strip, cue, t);
            var source = new Rect2(frame * strip.FrameW, 0, strip.FrameW, strip.FrameH);
            foreach (var (at, scale) in where(cue))
            {
                var size = new Vector2(strip.FrameW, strip.FrameH) * scale;
                on.DrawTextureRectRegion(sheet, new Rect2(at - size / 2, size), source);
            }
        }
    }
}
