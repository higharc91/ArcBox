using System.Globalization;
using System.Text;
using Tactics.Logic;

namespace Tactics.Sim;

/// <summary>The owner's lighter roaming-fight targets, kept separate from the story-fight bands.</summary>
public static class MobReport
{
    public static string Markdown(IReadOnlyList<SimBattle> battles)
    {
        var text = new StringBuilder("# Overworld mob balance (#79)\n\nOwner targets: **85–95% player wins and 4–8 rounds** at levels 1–3 and party sizes 1–4. Deviations are reported for review.\n\n");
        text.AppendLine("| Encounter | Level | Members | Battles | Win rate | Mean rounds | Caps | Assessment |");
        text.AppendLine("|---|---:|---:|---:|---:|---:|---:|---|");
        foreach (var group in battles.GroupBy(b => (b.Encounter, b.Level, Size: b.Classes.Length)).OrderBy(g => g.Key.Encounter, StringComparer.Ordinal).ThenBy(g => g.Key.Level).ThenBy(g => g.Key.Size))
        {
            var wins = 100.0 * group.Count(b => b.Outcome == BattleOutcome.Won) / group.Count();
            var rounds = group.Average(b => b.Rounds);
            var notes = new List<string>();
            if (wins < 85) notes.Add("harder than target");
            if (wins > 95) notes.Add("easier than target");
            if (rounds < 4) notes.Add("short");
            if (rounds > 8) notes.Add("long");
            text.AppendLine(CultureInfo.InvariantCulture, $"| {group.Key.Encounter} | {group.Key.Level} | {group.Key.Size} | {group.Count()} | {wins:0.0}% | {rounds:0.0} | {group.Count(b => b.Capped)} | {(notes.Count == 0 ? "within target" : string.Join("; ", notes))} |");
        }
        text.AppendLine("\nThe following standard simulator report retains its story-fight bands for role/ability diagnostics; the table above is the applicable mob assessment.\n");
        return text.ToString();
    }
}
