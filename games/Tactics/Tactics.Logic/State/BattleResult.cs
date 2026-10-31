using System.Collections.Immutable;

namespace Tactics.Logic;

/// <summary>How a battle ended, from the party's point of view.</summary>
public enum BattleOutcome { Won, Lost, Fled }

/// <summary>An injury a unit gained when it went Out.</summary>
public sealed record RecordedInjury(UnitId Unit, InjuryId Injury);

/// <summary>A consumable a unit used, and whether the AI chose to use it.</summary>
public sealed record ItemUse(UnitId Unit, ItemId Item, bool ByAi);

/// <summary>
/// What an ended battle hands the campaign (spec §2.5, §3.9; see <see cref="Battle.Result"/>): the outcome, XP for every
/// party member (full on a win, half on a loss or flight), injuries gained and items used by unit, the loot tables to roll (sub-project 6) and the story flags set.
/// </summary>
public sealed record BattleResult(BattleOutcome Outcome, ImmutableSortedDictionary<UnitId, int> Xp,
    ImmutableSortedDictionary<UnitId, ImmutableArray<InjuryId>> Injuries,
    ImmutableSortedDictionary<UnitId, ImmutableArray<ItemUse>> ItemsUsed, ImmutableArray<LootTableId> Loot,
    ImmutableArray<string> FlagsSet);
