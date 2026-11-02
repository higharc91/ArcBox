namespace Tactics.Presentation.World;

/// <summary>
/// The numbers of a fight in the world (overworld 5c §5), chosen while the user was away: only a person watching can say if they're right.
/// Together they keep the walk-in under <see cref="FormingMost"/>, inside the 3 s the game waits for the first beat's <c>shown</c>
/// (<c>TacticsTiming.FormUp</c>), so a slow walk-in never holds the game. The plan had 1.2 s and 1.4 s, 2.6 s in all; they're 1.0 and 1.2 so
/// that the walk-in, the frame that builds the stage, the view's way to the TV and the opening beat's own playing still come in under 3 s.
/// </summary>
public static class StageTuning
{
    /// <summary>The longest the heroes take to walk to their slots, in seconds. A longer path walks faster.</summary>
    public const double WalkInMost = 1.0;

    /// <summary>A hero's step on the way to its slot when there's time, in seconds: the party's own walking pace.</summary>
    public const double StepSeconds = 0.3;

    /// <summary>A hero this many steps or more from its slot doesn't walk: the party fades in.</summary>
    public const int FarSteps = 14;

    /// <summary>A path passing this close to an enemy's start, in steps, fades too, rather than run through it.</summary>
    public const int EnemyClearance = 2;

    /// <summary>How long the grid takes to come up once the heroes are in place, in seconds.</summary>
    public const double GridIn = 0.5;

    /// <summary>How long the grid takes to go after the last blow, in seconds.</summary>
    public const double GridOut = 0.4;

    /// <summary>The gap between one enemy or companion stepping in and the next, in seconds, at most.</summary>
    public const double StepInStagger = 0.2;

    /// <summary>The longest all of them take to step in, from the first starting to the last arriving, in seconds.</summary>
    public const double StepInMost = 1.2;

    /// <summary>One enemy's or companion's step in from a tile out, in seconds.</summary>
    public const double StepInSeconds = 0.4;

    /// <summary>The walkers' tween from where they stood to where they stand in the world, after a win, in seconds.</summary>
    public const double FallIn = 0.6;

    /// <summary>A fade to black, in seconds: a far party, or waking at the recovery spawn.</summary>
    public const double FadeDown = 0.4;

    /// <summary>The fade back up, in seconds.</summary>
    public const double FadeUp = 0.6;

    /// <summary>The fade as the world, now drawn, takes a fight over from the plain board, in seconds.</summary>
    public const double Takeover = 0.25;

    /// <summary>The longest the walk-in can take: the heroes, then everyone else stepping in.</summary>
    public const double FormingMost = WalkInMost + StepInMost;
}
