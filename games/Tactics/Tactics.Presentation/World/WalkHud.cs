using System.Globalization;
using Tactics.Game;

namespace Tactics.Presentation.World;

/// <summary>What the exploration screen's two panels say (mockup 02): where the party is, and who leads it. Pure, so <c>WorldHud</c> only draws it.</summary>
/// <param name="Title">The chapter and the region.</param>
/// <param name="Detail">The line under it.</param>
/// <param name="Leader">Who leads, or that nobody's there to.</param>
/// <param name="LeaderSprite">The leader's sprite key, for their face; null with nobody leading.</param>
public sealed record WalkHud(string Title, string Detail, string Leader, string? LeaderSprite)
{
    /// <summary>The small line over the leader's name.</summary>
    public const string LeaderLabel = "Party leader";

    /// <summary>The line under the leader's name.</summary>
    public const string LeaderNote = "Steers the party";

    private const string Nobody = "Nobody's here to lead";

    private static readonly string[] Numerals = ["", "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X"];

    /// <summary>The panels for <paramref name="explore"/>, with the party at <paramref name="level"/>.</summary>
    public static WalkHud Of(ExploreView explore, int level)
    {
        var chapter = Numeral(explore.Chapter);
        return new WalkHud(chapter.Length > 0 ? $"Chapter {chapter} · {explore.Name}" : explore.Name,
            $"Exploring · Party level {level.ToString(CultureInfo.InvariantCulture)}" + (explore.Gold is { } gold ? $" · ◆ {gold} gold" : ""),
            explore.Leader ?? Nobody,
            explore.Leader is null || explore.Party.IsDefault ? null : explore.Party.FirstOrDefault(w => w.Leads)?.Sprite);
    }

    private static string Numeral(int chapter) => chapter >= 0 && chapter < Numerals.Length ? Numerals[chapter] : chapter.ToString(CultureInfo.InvariantCulture);
}
