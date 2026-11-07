using System.Text.Json;
using ArcBox.Sdk;
using Tactics.Logic;

namespace Tactics.Game;

/// <summary>A short-lived explanation of a change in who decides for a player's character.</summary>
public sealed record ControlNoticeView(int Seq, string Reason, string Text, string? Policy, string? Recap);

public sealed partial class TacticsGame
{
    private sealed record ControlNotice(int Seq, int Round, UnitId Hero, string Reason, string Text, string Tv, string? Policy);
    private readonly Dictionary<string, ControlNotice> _controlNotices = [];
    private readonly Dictionary<string, List<string>> _aiRecaps = [];
    private int _controlSeq;
    private UnitId? _timedOutReaction;
    private const string AwayPolicy = "The AI may spend class resources and use one healing item on itself per battle when in danger. Skill points and subclass choices stay yours.";
    private const string TimeoutPolicy = "The AI may spend class resources. Timed-out turns don't use items. Skill points and subclass choices stay yours.";

    private ProfileId? OwnerOf(BattleState state, UnitId actor)
    {
        for (var i = 0; i < Save_.Members.Length; i++)
            if (Plays(state, UnitFor(i), actor))
                return Save_.Members[i];
        return null;
    }

    private void ControlChanged(ProfileId profile, string reason, string? reaction = null)
    {
        if (UnitOf(profile) is not { } hero || !State.Units.TryGetValue(hero, out var unit))
            return;
        var name = unit.Sheet.Name;
        var player = _players.FirstOrDefault(p => p.Profile == profile)?.Name ?? name;
        var (text, tv, policy) = reason switch
        {
            "disconnected" => ($"You're away. The AI is playing {name}. Rejoin to take control.", $"{player} is away. The AI is playing {name}. Rejoin anytime; skill points and subclass choices wait for you.", AwayPolicy),
            "reconnected" => ($"Welcome back, {player}. You're back in control of {name}.", $"{player} is back in control of {name}.", (string?)null),
            "turnTimeout" => ($"Time ran out. The AI is finishing your turn for {name}.", $"{name}'s turn timed out. The AI is finishing it.", TimeoutPolicy),
            "reactionTimeout" => ($"Reaction time ran out. The AI chose {reaction} for you.", $"{name}'s reaction timed out. The AI chose {reaction}.", (string?)null),
            _ => (HandBackFailed, $"{name}: {HandBackFailed}", (string?)null),
        };
        if (reason == "disconnected")
            _aiRecaps.Remove(profile.Value);
        _controlNotices[profile.Value] = new(++_controlSeq, State.Round, hero, reason, text, tv, policy);
    }

    private bool CurrentNotice(string profile, ControlNotice notice)
        => _doc.Battle is not { } battle || battle.State.Round <= notice.Round + 1
            || notice.Reason == "disconnected" && !Online(new ProfileId(profile));

    private void ReturnedAfterBattle(ProfileId profile)
    {
        if (!_controlNotices.TryGetValue(profile.Value, out var notice) || _doc.For(profile) is not { } member)
            return;
        var name = member.Character.Name;
        var player = _players.FirstOrDefault(p => p.Profile == profile)?.Name ?? name;
        _controlNotices[profile.Value] = notice with
        {
            Seq = ++_controlSeq, Reason = "reconnected", Policy = null,
            Text = $"Welcome back, {player}. The battle is over; you're back in control of {name}.",
            Tv = $"{player} is back in control of {name}. The battle is over.",
        };
    }

    private string NoticeText(ControlNotice notice, bool tv)
    {
        var text = tv ? notice.Tv : notice.Text;
        return notice.Reason == "turnTimeout" && (_doc.Battle is not { } battle || _aiFinishing is not { } finishing || !Plays(battle.State, notice.Hero, finishing))
            ? text.Replace("is finishing", "finished", StringComparison.Ordinal)
            : text;
    }

    private ControlNoticeView? PhoneControlNotice(ProfileId profile)
    {
        if (!_controlNotices.TryGetValue(profile.Value, out var notice) || !CurrentNotice(profile.Value, notice))
            return null;
        var recap = notice.Reason != "reconnected" ? null
            : _aiRecaps.TryGetValue(profile.Value, out var actions) && actions.Count > 0
                ? $"While you were away: {string.Join("; ", actions)}."
                : "No moves or abilities were completed while you were away.";
        return new(notice.Seq, notice.Reason, NoticeText(notice, false), notice.Policy, recap);
    }

    private string? TvControlNotice()
        => _phase is TacticsPhase.Battle or TacticsPhase.Results
            ? _controlNotices.Where(n => CurrentNotice(n.Key, n.Value)).Select(n => n.Value).OrderByDescending(n => n.Seq)
                .Select(n => NoticeText(n, true)).FirstOrDefault()
            : null;

    private void RecordAiControl(BattleState before, Command command)
    {
        if (OwnerOf(before, command.Actor) is not { } profile)
            return;
        if (command is React react && _timedOutReaction == react.Actor)
            ControlChanged(profile, "reactionTimeout", react.Choice.Ability is { } ability
                ? Rules.Abilities[ability].Name : "Don't react");
        if (Online(profile))
            return;
        var action = command switch
        {
            Move => "moved",
            Use use when use.Item is { } item => $"used {Rules.Items[item].Name}",
            Use use => $"used {Rules.Abilities[use.Ability].Name}",
            React { Choice.Ability: { } ability } => $"reacted with {Rules.Abilities[ability].Name}",
            _ => null,
        };
        if (action is null)
            return;
        if (UnitOf(profile) is { } hero && command.Actor != hero)
            action = $"{before.Units[command.Actor].Sheet.Name} {action}";
        if (!_aiRecaps.TryGetValue(profile.Value, out var actions))
            _aiRecaps[profile.Value] = actions = [];
        if (actions.LastOrDefault() != action)
            actions.Add(action);
        if (actions.Count > 3)
            actions.RemoveAt(0);
    }

    private ControllerView WithControlNotice(ControllerView view, ProfileId profile)
    {
        if (_phase is not (TacticsPhase.Battle or TacticsPhase.Results) || view is not CustomView custom || PhoneControlNotice(profile) is not { } notice)
            return view;
        var data = JsonSerializer.SerializeToNode(custom.Data, ArcJson.Options)!.AsObject();
        data["controlNotice"] = JsonSerializer.SerializeToNode(notice, ArcJson.Options);
        return custom with { Data = JsonSerializer.SerializeToElement(data, ArcJson.Options) };
    }
}
