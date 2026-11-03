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
    private PanelContainer? _campPanel;
    private VBoxContainer? _campRows;
    private string? _campMoment;
    private double _mendAt;
    private string? _mendedMember;

    private void ShowCamp(TacticsHostView view)
    {
        _camp = view.Camp;
        _world?.CampLight(RecoveryShow.Night(_camp));
        if (_campPanel is null)
        {
            _campRows = ArcTheme.VBox(7);
            _campPanel = ArcTheme.Panel(ArcTheme.GlassBox(ArcTheme.Gold, 24, 20), _campRows);
            _campPanel.SetAnchorsPreset(LayoutPreset.CenterRight);
            _campPanel.OffsetLeft = -620; _campPanel.OffsetRight = -40; _campPanel.OffsetTop = -340; _campPanel.OffsetBottom = 340;
            AddChild(_campPanel);
        }
        _campPanel.Visible = _camp is { Mode: "camp", Stage: not ("vote" or "talk") };
        if (_camp is not { } camp) { _campMoment = null; return; }
        var moment = $"{camp.Source}/{camp.Visit}/{camp.Stage}/{camp.ResultCount}/{string.Join(',', camp.People.Select(p => p.Job))}";
        if (_campMoment == moment) return;
        var before = _campMoment;
        _campMoment = moment;
        foreach (var child in _campRows!.GetChildren()) { _campRows.RemoveChild(child); child.QueueFree(); }
        _campRows.AddChild(ArcTheme.Meta(camp.Stage == "dawn" ? "DAWN" : "BY THE FIRE"));
        _campRows.AddChild(ArcTheme.Label(camp.Stage == "dawn" ? "A new day" : "Around the fire", ArcTheme.Bold, 30, ArcTheme.Gold));
        if (camp.Members is { } members)
            foreach (var member in members)
                _campRows.AddChild(RecoveryCard(member, camp.People.FirstOrDefault(p => p.Member == member.Member)?.Job is { } job ? CampJobLabel(job) : null));
        else foreach (var person in camp.People)
            _campRows.AddChild(ArcTheme.Label($"{person.Name} · {CampJobLabel(person.Job)}", ArcTheme.SemiBold, 23));
        if (camp.Result is { } result && camp.Stage == "results")
        {
            if (result.Die > 0)
            {
                var die = Math.Max(result.Die, result.SecondDie ?? result.Die);
                _campRows.AddChild(ArcTheme.Label($"d20 {die} + {result.Bonus} = {die + result.Bonus}" + (result.Dc > 0 ? $" · DC {result.Dc}" : ""), ArcTheme.Bold, 27, result.Passed ? new Color(.5f, 1, .6f) : ArcTheme.Gold));
                if (result.SecondDie is { } second) _campRows.AddChild(ArcTheme.Label($"Advantage · {result.Die} / {second}", ArcTheme.SemiBold, 22));
            }
            var words = ArcTheme.Label(result.Text, ArcTheme.SemiBold, 23);
            words.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            _campRows.AddChild(words);
            var sound = result.Passed && result.Job is "tend" or "rally" ? "camp_mend" : result.Job == "cook" && result.Passed ? "camp_cook" : null;
            if (sound is not null) PlayRecovery(sound);
            if (result.Passed && result.Job is "tend" or "rally") { _mendedMember = result.Target; _mendAt = _clock.Elapsed.TotalSeconds; }
            _narrator?.Say(result.Text, "tactics.narrator", ArcBox.Host.Core.Audio.NarrationPriority.Question);
        }
        if (camp.Stage == "dawn") { PlayRecovery("camp_dawn"); _campRows.AddChild(ArcTheme.Label("Rest injuries mended · Ready for the road", ArcTheme.SemiBold, 22)); }
        else if (camp.Mode == "camp" && (before is null || before.Contains("/vote/", StringComparison.Ordinal))) PlayRecovery("camp_start");
        _shots?.Capture("camp-" + camp.Stage + "-" + camp.ResultCount);
    }

    private void PlayRecovery(string action)
    { if (RecoveryShow.Sound(action, _sound) is { } sound) _audio.Play(sound, ThingSounds.Db); }

    private WalkStage? CampStage(BoardTerrain terrain, double delta, WalkStage? normal)
    {
        var places = _recovery.Gather(_camp, terrain, _walk!.Places, delta);
        return places is null ? normal : new WalkStage(normal?.Hold, normal?.Cuts ?? 0, places, false, normal?.Shake ?? default);
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
    private static string CampJobLabel(string? job) => job switch { "tend" => "Tend wounds", "rally" => "Rally", "cook" => "Cook", "watch" => "Keep watch", "rest" => "Rest easy", _ => "Choosing a job" };
}
