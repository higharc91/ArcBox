using ArcBox.Host.Shell;
using System.Collections.Immutable;
using Godot;
using Tactics.Game;
using Tactics.Logic;
using Tactics.Presentation.Beats;
using Tactics.Presentation.Board;
using Tactics.Presentation.World;
using Tactics.Presentation.Audio;

namespace ArcBox.Host.Games.Tactics;

public partial class TacticsScene
{
    private readonly RecoveryShow _recovery = new();
    private CampView? _camp;
    private CampHud? _campHud;
    private string? _campMoment;
    private double _mendAt;
    private double _campPhaseSeconds;
    private string? _campPhaseKey;
    private string? _mendedMember;

    private void ShowCamp(TacticsHostView view)
    {
        _camp = view.Camp;
        var phaseKey = _camp is { } current ? current.Source + "/" + current.Stage : null;
        if (_campPhaseKey != phaseKey) { _campPhaseKey = phaseKey; _campPhaseSeconds = 0; }

        if (_campHud is null && _unitArt is not null && _content.TryGetValue(BasePack, out var root))
        { _campHud = new CampHud(m => RecoveryCard(m, CampJobNote(_camp?.People.FirstOrDefault(p => p.Member == m.Member)), compact: true), root, _unitArt); AddChild(_campHud); }
        _campHud?.Show(_camp);
        if (_camp is not { } camp) { _campMoment = null; return; }
        var moment = $"{camp.Source}/{camp.Visit}/{camp.Stage}/{camp.ResultCount}/{string.Join(',', camp.People.Select(p => p.Job))}";
        if (_campMoment == moment) return;
        var before = _campMoment;
        _campMoment = moment;
        if (camp.Result is { } result && camp.Stage == "results")
        {
            var sound = result.Passed && result.Job is "tend" or "rally" ? "camp_mend" : result.Job == "cook" && result.Passed ? "camp_cook" : null;
            if (sound is not null) PlayRecovery(sound);
            if (result.Passed && result.Job is "tend" or "rally") { _mendedMember = result.Target; _mendAt = _clock.Elapsed.TotalSeconds; }
            _narrator?.Say(result.Text, "tactics.narrator", ArcBox.Host.Core.Audio.NarrationPriority.Question);
        }
        if (camp.Stage == "dawn") PlayRecovery("camp_dawn");
        else if (camp.Mode == "camp" && (before is null || before.Contains("/vote/", StringComparison.Ordinal))) PlayRecovery("camp_start");
        _shots?.Capture("camp-" + camp.Stage + "-" + camp.ResultCount);
    }

    private void PlayRecovery(string action)
    { if (RecoveryShow.Sound(action, _sound) is { } sound) _audio.Play(sound, ThingSounds.Db); }

    private WalkStage? CampStage(BoardTerrain terrain, double delta, WalkStage? normal)
    {
        if (_campHud is null && _unitArt is not null && _content.TryGetValue(BasePack, out var root))
        { _campHud = new CampHud(m => RecoveryCard(m, CampJobNote(_camp?.People.FirstOrDefault(p => p.Member == m.Member)), compact: true), root, _unitArt); AddChild(_campHud); _campHud.Show(_camp); }
        _campPhaseSeconds += delta;
        _campHud?.Tick(_campPhaseSeconds, delta);
        var places = _recovery.Gather(_camp, terrain, _walk!.Places, delta);
        return places is null ? normal : new WalkStage(_camp is { Mode: "camp" } camp ? new StageCamera(new System.Numerics.Vector2(camp.At.X + .5f, camp.At.Y + .5f), terrain.Top(camp.At)) : normal?.Hold, normal?.Cuts ?? 0, places, false, normal?.Shake ?? default);
    }

    private ImmutableArray<FxDraw> MendDraws()
    {
        if (_mendedMember is null || _clock.Elapsed.TotalSeconds - _mendAt > 1.5 || _world?.Plan is not { } plan || _walk is null || _fx?.Clip("fx.poof") is not { } clip) return [];
        var person = _camp?.People.FirstOrDefault(p => p.Member == _mendedMember);
        if (person is null) return [];
        var feet = UnitPlace.On(plan.Terrain, person.At).Feet;
        var frame = clip.FrameAt(Math.Min(_clock.Elapsed.TotalSeconds - _mendAt, clip.Seconds * .99));
        return [new FxDraw(clip.Path, new ArtRect(frame * clip.Width, 0, clip.Width, clip.Height),
            new System.Numerics.Vector2(feet.X - clip.AnchorX, feet.Y - clip.AnchorY - clip.Lift), new System.Numerics.Vector2(clip.Width, clip.Height), 0, clip.Additive, 1)
            { Tint = new Rgb(.4f, 1, .55f) }];
    }
    private static string? CampJobNote(CampPersonView? person) => person?.Job is not { } job ? null
        : (person.Outcome == "success" ? "✓ " : person.Outcome == "failed" ? "× " : "") + CampJobLabel(job);

    private static string CampJobLabel(string? job) => job switch { "tend" => "Tend", "rally" => "Rally", "cook" => "Cook", "watch" => "Watch", "rest" => "Rest", _ => "Choosing a job" };
}
