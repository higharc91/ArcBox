using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using ArcBox.Sdk;
using Tactics.Logic;

namespace Tactics.Game;

public sealed partial class TacticsGame
{
    private BackgroundLore? PrivateBackground(ProfileId profile)
    {
        if (_phase == TacticsPhase.Muster || _doc.World is null || _doc.For(profile) is not { } member
            || !Lore.Backgrounds.TryGetValue(member.Character.Background, out var background) || background.Secret is null
            || !member.BackgroundSecretsRead.IsDefaultOrEmpty && member.BackgroundSecretsRead.Contains(background.Id)) return null;
        var chapter = _region?.Def.Chapter ?? StoryArc?.Chapter ?? 0;
        return background.UnlockChapter is { } unlock && chapter >= unlock
            || background.UnlockFlag is { } flag && StoryFlags().Contains(flag) ? background : null;
    }

    private ControllerView WithBackgroundSecret(ControllerView view, ProfileId profile)
    {
        if (view is not CustomView custom || PrivateBackground(profile) is not { } background) return view;
        var data = JsonNode.Parse(custom.Data.GetRawText())!.AsObject();
        data["backgroundSecret"] = JsonSerializer.SerializeToNode(new
        {
            id = background.Id, background = Rules.Backgrounds.GetValueOrDefault(background.Id)?.Name ?? background.Id,
            text = background.Secret,
        }, ArcJson.Options);
        return custom with { Data = JsonSerializer.SerializeToElement(data, ArcJson.Options) };
    }

    private void RememberBackgroundSecret(ProfileId profile)
    {
        if (PrivateBackground(profile) is not { } background || _doc.For(profile) is not { } member) return;
        var read = member.BackgroundSecretsRead.IsDefault ? ImmutableArray<string>.Empty : member.BackgroundSecretsRead;
        _doc = _doc with { Members = _doc.Members.SetItem(profile.Value, member with { BackgroundSecretsRead = read.Add(background.Id) }) };
        Save();
    }

    private IEnumerable<string> OptionOwners(SceneOption option) => _doc.Members.Where(m =>
        option.Tags.Background.Contains(m.Value.Character.Background) || option.Tags.Race.Contains(m.Value.Character.Race)
        || option.Tags.Class.Contains(m.Value.Character.Class)).Select(m => m.Key);

    private string? OptionCredit(SceneOption option)
    {
        if (!option.Tags.Any) return null;
        var tags = option.Tags.Background.Select(id => Rules.Backgrounds.GetValueOrDefault(id)?.Name ?? id)
            .Concat(option.Tags.Race.Select(id => Rules.Races.GetValueOrDefault(id)?.Name ?? id))
            .Concat(option.Tags.Class.Select(id => Rules.Classes.GetValueOrDefault(id)?.Name ?? id));
        return string.Join(" / ", tags) + " · " + string.Join(", ", OptionOwners(option).Select(id => _doc.Members[id].Character.Name));
    }

    private bool OwnerAgreed(SceneOption option) => OptionOwners(option).Any(owner =>
        _votes.TryGetValue(owner, out var vote) && vote == option.Id
        || _pickedBy == owner && _votes.TryGetValue(LeaderPick, out var pick) && pick == option.Id);

    private bool MayChooseOption(string member, SceneOption option) => option.Tags.Any
        ? OptionOwners(option).Contains(member) || member == _lead && OwnerAgreed(option)
        : PartyQuestion || member == Decider();

    private ImmutableArray<SceneOption> DefaultOptions => [.. _offered.Where(o => !o.Tags.Any || OwnerAgreed(o))];
}
