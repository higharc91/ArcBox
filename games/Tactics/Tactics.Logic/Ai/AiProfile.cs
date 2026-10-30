using System.Collections.Immutable;

namespace Tactics.Logic;

/// <summary>
/// The weights the planner scores a turn with (spec §5.3), from <c>ai-profiles.json</c> (<see cref="AiProfileDef"/>) and
/// scaled for the battle's difficulty (<see cref="ForDifficulty"/>). <see cref="Damage"/>, <see cref="Kill"/>,
/// <see cref="Control"/>, <see cref="Heal"/> and <see cref="Revive"/> weigh what an action does; <see cref="Caution"/>
/// weighs the threat of the tile the turn ends on; <see cref="FocusFire"/> favours hurt targets; <see cref="Objective"/>
/// weighs the encounter's objectives; <see cref="Hoard"/> is what spending resources costs. <see cref="Cruel"/> lets it
/// value hitting Downed targets; below <see cref="FleeBelowHp"/> (a fraction of max HP) it runs instead of fighting.
/// </summary>
public sealed record AiProfile(string Id, double Damage, double Kill, double Control, double Heal, double Revive,
    double Caution, double FocusFire, double Objective, double Hoard, bool Cruel, double FleeBelowHp)
{
    /// <summary>
    /// The weights used when a unit's profile isn't in the rules pack: a plain fighter that values its own skin a little
    /// (the base pack's <c>brute</c> numbers, a bit more careful).
    /// </summary>
    public static AiProfile Default { get; } = new("default", Damage: 1.0, Kill: 1.5, Control: 0.5, Heal: 1.0, Revive: 1.5,
        Caution: 0.4, FocusFire: 0.5, Objective: 1.0, Hoard: 0.3, Cruel: false, FleeBelowHp: 0);

    /// <summary>The weights of a content profile.</summary>
    public static AiProfile From(AiProfileDef def) => new(def.Id, def.Damage, def.Kill, def.Control, def.Heal, def.Revive,
        def.Caution, def.FocusFire, def.Objective, def.Hoard, def.Cruel, def.FleeBelowHp);

    /// <summary>
    /// The profile as the AI plays it on <paramref name="difficulty"/> (spec §5.4): Story halves focus fire and caution and
    /// hoards half as much again (×1.5), so it spreads damage and rarely retreats; Normal keeps the weights; Hard focus-fires
    /// (×1.5), uses cover and the threat map more (caution ×1.25) and spends more freely (hoard ×0.75).
    /// </summary>
    public AiProfile ForDifficulty(Difficulty difficulty) => difficulty switch
    {
        Difficulty.Story => this with { FocusFire = FocusFire * 0.5, Caution = Caution * 0.5, Hoard = Hoard * 1.5 },
        Difficulty.Hard => this with { FocusFire = FocusFire * 1.5, Caution = Caution * 1.25, Hoard = Hoard * 0.75 },
        _ => this,
    };

    /// <summary>
    /// The profile <paramref name="unit"/> plays with: <paramref name="overrideId"/> if given, else its controller's
    /// (<see cref="AiControlled.Profile"/>), else its sheet's (<see cref="UnitSheet.AiProfile"/>), looked up in
    /// <paramref name="rules"/>; <see cref="Default"/> (under the requested id) when the pack doesn't have it. Scaled for the
    /// battle's difficulty.
    /// </summary>
    public static AiProfile For(BattleState state, UnitId unit, RulesPack rules, string? overrideId = null)
    {
        var u = state.Units[unit];
        var id = overrideId ?? (u.Controller as AiControlled)?.Profile ?? u.Sheet.AiProfile;
        var profile = id is not null && rules.AiProfiles.TryGetValue(id, out var def) ? From(def)
            : Default with { Id = id ?? Default.Id };
        return profile.ForDifficulty(state.Difficulty);
    }
}

/// <summary>
/// How <see cref="Ai.Plan"/> may play a turn. <see cref="AllowItems"/> lets it use consumables from the unit's bag (off by
/// default; companions are given their own item rules in Task 15). <see cref="AllowDelay"/> and
/// <see cref="RemainingOnly"/> are for timed-out turns: the planner always works from what is left of the unit's turn
/// (Move, Action, Bonus) and never delays, so a plan always ends with <see cref="EndTurn"/>. <see cref="ProfileOverride"/>
/// names the profile to use instead of the unit's own.
/// </summary>
public sealed record AiOptions(bool AllowItems = false, bool AllowDelay = true, bool RemainingOnly = false,
    string? ProfileOverride = null);

/// <summary>
/// A whole turn the AI chose: the commands in order (the last is always <see cref="EndTurn"/> unless the unit can't be
/// planned for), the score the planner gave it and a deterministic explanation for logs and tests.
/// </summary>
public sealed record TurnPlan(ImmutableArray<Command> Commands, double Score, string Explain);
