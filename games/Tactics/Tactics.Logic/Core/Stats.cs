using System.Text.Json.Serialization;

namespace Tactics.Logic;

/// <summary>The six ability scores. JSON: <c>"str"</c>, <c>"dex"</c>, <c>"con"</c>, <c>"int"</c>, <c>"wis"</c>, <c>"cha"</c>.</summary>
public enum Stat
{
    /// <summary>Strength.</summary>
    [JsonStringEnumMemberName("str")] Str,
    /// <summary>Dexterity.</summary>
    [JsonStringEnumMemberName("dex")] Dex,
    /// <summary>Constitution.</summary>
    [JsonStringEnumMemberName("con")] Con,
    /// <summary>Intelligence.</summary>
    [JsonStringEnumMemberName("int")] Int,
    /// <summary>Wisdom.</summary>
    [JsonStringEnumMemberName("wis")] Wis,
    /// <summary>Charisma.</summary>
    [JsonStringEnumMemberName("cha")] Cha,
}

/// <summary>Ability-score arithmetic shared by the rules.</summary>
public static class Stats
{
    /// <summary>The ability modifier, ⌊(score − 10) / 2⌋.</summary>
    public static int Mod(int score) => (score - 10) >> 1;

    /// <summary>The proficiency bonus: +2 at levels 1–4, +3 at 5–8, +4 at 9–10.</summary>
    public static int Proficiency(int level)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(level, 1);
        return 2 + (level - 1) / 4;
    }
}
