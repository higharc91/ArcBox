using Tactics.Logic;

namespace Tactics.Game;

/// <summary>One-time story and peaceful-resolution experience, saved with the beat's completion.</summary>
public static class CampaignExperience
{
    public static int MobReward(int xp, int? encounterLevel, int partyLevel) => encounterLevel is { } level && partyLevel > level + 1 ? 0 : xp;

    public static TacticsDocument CompleteBeat(TacticsDocument doc, ArcBeat beat, RulesPack rules)
    {
        if (doc.World is not { } world || world.Beats.Contains(beat.Id)) return doc;
        var xp = checked(beat.Xp + beat.Choices.Where(c => !c.Sets.IsEmpty && c.Sets.All(world.Flags.Contains)).Sum(c => c.Xp));
        if (xp == 0) return doc;
        var members = Ordinal.Map(doc.Members.Select(m => (m.Key, m.Value with { Xp = checked(m.Value.Xp + xp) })));
        var level = Math.Max(doc.PartyLevel, TacticsDocument.LevelFor(members.Values.Select(m => m.Xp), rules.Xp));
        if (level > doc.PartyLevel)
            members = Ordinal.Map(members.Select(m => (m.Key, m.Value with { Character = CharacterRules.LevelUp(m.Value.Character, level, rules) })));
        return doc with { Members = members, PartyLevel = level, World = world with { Beats = world.Beats.Add(beat.Id) } };
    }
}
