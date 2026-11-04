using System.Collections.Immutable;
using Tactics.Logic;

namespace Tactics.Game;

public sealed partial class TacticsGame
{
    private ImmutableArray<QuestDef> RegionQuests
    {
        get
        {
            if (_region is null) return [];
            var flags = StoryFlags();
            return [.. Lore.Quests.Where(q => q.Region == _region.Def.Id || q.State(flags) is not null)];
        }
    }
    private QuestEventView? _questEvent;
    private static double QuestNoticeSeconds(QuestEventView notice) => notice.Gifts is { Length: > 0 } gifts ? Math.Max(7, gifts.Length * 2.4 + 1) : 7;
    private int _questSeq;
    private IReadOnlySet<string>? _questObservedFlags;

    private void PrepareQuestSave()
    {
        if (_doc.World is null || Lore.Quests.IsEmpty) return;
        if (_questObservedFlags is null && _doc.World.PinnedQuest is { } pin
            && Lore.Quests.FirstOrDefault(q => q.Id == pin)?.State(StoryFlags())?.Status != "active")
            _doc = _doc with { World = _doc.World with { PinnedQuest = null } };
        QuestFlagsChanged(_questObservedFlags ?? StoryFlags());
        _questObservedFlags = StoryFlags();
    }

    /// <summary>Only document mutations: the caller saves flags, ledger, purses, bags and XP together.</summary>
    private void QuestFlagsChanged(IReadOnlySet<string> before)
    {
        var flags = StoryFlags();
        foreach (var quest in Lore.Quests)
        {
            var previous = quest.State(before);
            var state = quest.State(flags);
            if (state is null) continue;
            var kind = previous?.Status != state.Status ? state.Status switch { "done" => "complete", "failed" => "failed", _ => "start" }
                : previous.Stage != state.Stage ? "update" : null;
            if (kind is not null)
            {
                ImmutableArray<TrinketGiftView>? gifts = kind == "complete" && quest.Rewards.Trinkets is { } giftMap
                    ? [.. _doc.Members.Select(m => new TrinketGiftView(m.Key, m.Value.Character.Name, m.Value.Character.Class,
                        giftMap[m.Value.Character.Class], Rules.Trinkets[giftMap[m.Value.Character.Class]].Name, Online(new ArcBox.Sdk.ProfileId(m.Key))))] : null;
                _questEvent = new QuestEventView(++_questSeq, quest.Id, quest.Name, kind, _phase == TacticsPhase.Exploration ? _ctx.Now.AddSeconds(7) : DateTimeOffset.MaxValue, quest.Rewards.Gold, quest.Rewards.Xp, gifts);
                if (_phase == TacticsPhase.Exploration) _questEvent = _questEvent with { Until = _ctx.Now.AddSeconds(QuestNoticeSeconds(_questEvent)) };
                _ctx.CancelTimer("questResult");
                if (_phase == TacticsPhase.Exploration) _ctx.SetTimer("questResult", TimeSpan.FromSeconds(QuestNoticeSeconds(_questEvent)));
                if (kind == "start" && _doc.World!.PinnedQuest is null)
                    _doc = _doc with { World = _doc.World with { PinnedQuest = quest.Id } };
            }
            var rewarded = _doc.World!.QuestsRewarded.IsDefault ? [] : _doc.World.QuestsRewarded;
            if (state.Status != "done" || rewarded.Contains(quest.Id)) continue;
            ChangePartyGold(quest.Rewards.Gold);
            var members = _doc.Members;
            members = Ordinal.Map(members.Select(m => (m.Key, m.Value with { Xp = checked(m.Value.Xp + quest.Rewards.Xp) })));
            if (quest.Rewards.Trinkets is { } trinkets)
                members = Ordinal.Map(members.Select(m => (m.Key, m.Value with
                { Character = m.Value.Character with { Trinket = trinkets[m.Value.Character.Class] } })));
            var level = TacticsDocument.LevelFor(members.Values.Select(m => m.Xp), Rules.Xp);
            if (level > _doc.PartyLevel)
                members = Ordinal.Map(members.Select(m => (m.Key, m.Value with { Character = LevelUp(m.Value.Character, level) })));
            _doc = _doc with
            {
                Members = members, PartyLevel = Math.Max(level, _doc.PartyLevel),
                World = _doc.World with
                {
                    QuestsRewarded = rewarded.Add(quest.Id),
                    Flags = [.. _doc.World.Flags.Concat(quest.Rewards.Sets.IsDefault ? [] : quest.Rewards.Sets).Distinct(StringComparer.Ordinal)],
                },
            };
            // Its items go to whoever the party picks (USER DECISION 1), in this same save: the one who turned it in, for a party of one.
            if (!quest.Rewards.Items.IsDefaultOrEmpty)
                GiveFound(quest.Rewards.Items.Select(i => (new ItemId(i.Item), i.Count)), RewardMember(), quest.Name);
        }
    }

    private void ShowPendingQuestNotice()
    {
        if (_questEvent is not { Until: var until } notice || until != DateTimeOffset.MaxValue) return;
        _questEvent = notice with { Until = _ctx.Now.AddSeconds(QuestNoticeSeconds(notice)) };
        _ctx.SetTimer("questResult", TimeSpan.FromSeconds(QuestNoticeSeconds(_questEvent)));
    }

    private void QuestResultDone()
    {
        _questEvent = null;
        if (_doc.World?.PinnedQuest is { } pin && Lore.Quests.FirstOrDefault(q => q.Id == pin)?.State(StoryFlags()) is { Status: not "active" })
        {
            _doc = _doc with { World = _doc.World with { PinnedQuest = null } };
            Save();
        }
    }
}
