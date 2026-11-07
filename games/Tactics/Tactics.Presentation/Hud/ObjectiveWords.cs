using Tactics.Game;
using Tactics.Logic;

namespace Tactics.Presentation.Hud;

/// <summary>The shared objective sentence shown on the TV and phones.</summary>
public static class ObjectiveWords
{
    public static string? Of(ObjectivesDef? objectives, IEnumerable<UnitView>? units = null)
        => BattleObjectiveWords.Of(objectives, units);
}
