using System.Collections.Immutable;
using System.Text.Json;
using ArcBox.Sdk;
using Tactics.Logic;

namespace Tactics.Game;

public sealed partial class TacticsGame
{
    private static Stat? ParseStat(string s)
        => Enum.GetValues<Stat>().Cast<Stat?>().FirstOrDefault(st => string.Equals(st.ToString(), s, StringComparison.OrdinalIgnoreCase));

    private void HandleCharacterChoice(Player player, ProfileId profile, string action, JsonElement? data)
    {
        var mine = _doc.For(profile);
        if (mine is null)
        {
            Toast(player.Id, "Create your character first.");
            return;
        }

        (CharacterRecord? Character, string? Problem) result;
        switch (action)
        {
            case "chooseSubclass":
                var sub = Str(data, "subclass");
                result = CharacterRules.ChooseSubclass(mine.Character, sub ?? "", Rules);
                break;
            case "spendIncrease":
                var stats = data.HasValue && data.Value.TryGetProperty("stats", out var st) && st.ValueKind == JsonValueKind.Array
                    ? st.EnumerateArray().Select(e => e.ValueKind == JsonValueKind.String ? ParseStat(e.GetString()!) : null).ToList()
                    : [];
                if (stats.Any(x => x is null))
                {
                    Toast(player.Id, "Pick abilities to raise.");
                    return;
                }
                result = CharacterRules.SpendIncrease(mine.Character, [.. stats.Select(x => x!.Value)], Rules);
                break;
            case "setPicks":
                var picks = data.HasValue && data.Value.TryGetProperty("picks", out var list) && list.ValueKind == JsonValueKind.Array
                    ? list.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => new AbilityId(e.GetString()!)).ToList()
                    : [];
                result = CharacterRules.SetPicks(mine.Character, picks, Rules);
                break;
            default:
                return;
        }

        if (result.Problem is not null)
        {
            Toast(player.Id, result.Problem);
            return;
        }

        if (result.Character is { } updated)
        {
            _doc = _doc with { Members = _doc.Members.SetItem(profile.Value, mine with { Character = updated }) };
            Save();
            PartyChanged();
        }
    }

    /// <summary>Number of pending choices (subclass, ability increases, open picks) waiting for this character.</summary>
    internal int PendingChoices(CharacterRecord c)
    {
        var count = 0;
        if (CharacterRules.NeedsSubclass(c))
            count++;
        count += CharacterRules.IncreasesLeft(c) / 2;
        if (Rules.Classes.TryGetValue(c.Class, out var cls))
        {
            var subclass = cls.Subclasses.FirstOrDefault(s => s.Id == c.Subclass);
            var choices = SheetBuilder.PickChoices(cls, subclass, c.Level);
            var placed = SheetBuilder.AssignPicks(c.Picks, choices);
            for (var i = 0; i < choices.Length; i++)
            {
                var choose = choices[i].Feature.Choose ?? 0;
                if (placed[i].Length < choose)
                    count += (choose - placed[i].Length);
            }
        }
        return count;
    }

    /// <summary>The view model for character choices (subclass, ability increases, picks) and level-up info for the phone.</summary>
    internal object CharacterChoices(MemberRecord record)
    {
        var c = record.Character;
        Rules.Classes.TryGetValue(c.Class, out var cls);
        var subclass = cls?.Subclasses.FirstOrDefault(s => s.Id == c.Subclass);
        var choices = cls is null ? [] : SheetBuilder.PickChoices(cls, subclass, c.Level);
        var picks = c.Picks.IsDefaultOrEmpty && cls is not null ? SheetBuilder.DefaultLoadout(cls, c.Level) : c.Picks;
        var placed = SheetBuilder.AssignPicks(picks, choices);
        var boosts = Rules.Backgrounds.GetValueOrDefault(c.Background)?.Boosts;
        var scores = Enum.GetValues<Stat>().ToDictionary(s => s.ToString().ToLowerInvariant(),
            s => Math.Min(20, c.BaseScores.GetValueOrDefault(s, 10) + (boosts?.GetValueOrDefault(s) ?? 0) + c.Increases.GetValueOrDefault(s)));
        var nextLevelXp = c.Level < Rules.Xp.Thresholds.Length ? Rules.Xp.Thresholds[c.Level] : (int?)null;
        var prevLevelXp = c.Level > 1 && c.Level - 1 < Rules.Xp.Thresholds.Length ? Rules.Xp.Thresholds[c.Level - 1] : 0;
        return new
        {
            name = c.Name,
            @class = c.Class,
            className = cls?.Name ?? c.Class,
            subclass = c.Subclass,
            subclassName = subclass?.Name,
            level = c.Level,
            xp = record.Xp,
            nextLevelXp,
            prevLevelXp,
            scores,
            needsSubclass = CharacterRules.NeedsSubclass(c),
            subclasses = CharacterRules.NeedsSubclass(c) && cls is not null ? cls.Subclasses.Select(s => new { id = s.Id, name = s.Name, role = s.Name }) : null,
            increasesLeft = CharacterRules.IncreasesLeft(c),
            choices = choices.Select((ch, i) => new
            {
                level = ch.Level,
                choose = ch.Feature.Choose,
                from = ch.Feature.From.Select(id => Option(id, c.Level, Rules)),
                picked = placed[i].Select(p => p.Value).ToArray(),
            }).ToArray(),
            waiting = PendingChoices(c),
        };
    }

    private static string Humanize(string id) => id.Length == 0 ? id : char.ToUpperInvariant(id[0]) + id[1..].Replace('_', ' ');

    private static object Option(string id, int level, RulesPack rules)
        => rules.Abilities.TryGetValue(new AbilityId(id), out var a)
            ? new { id, name = a.Name, tooltip = Tooltips.For(a, level, rules) }
            : rules.Passives.TryGetValue(id, out var p)
                ? (object)new { id, name = Humanize(id.Split('.')[^1]), tooltip = Tooltips.For(p, level, rules) }
                : new { id, name = id, tooltip = "" };
}
