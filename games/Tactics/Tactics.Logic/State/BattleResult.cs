namespace Tactics.Logic;

/// <summary>How a battle ended, from the party's point of view.</summary>
public enum BattleOutcome { Won, Lost, Fled }

/// <summary>An injury a unit gained when it went Out.</summary>
public sealed record RecordedInjury(UnitId Unit, InjuryId Injury);

/// <summary>A consumable a unit used, and whether the AI chose to use it.</summary>
public sealed record ItemUse(UnitId Unit, ItemId Item, bool ByAi);
