using Meridian.Logic;

namespace Meridian.Game;

// Disconnects and bots (spec §3.3, Explore rows; M2 plan, Task 4).
public sealed partial class MeridianGame
{
    /// <summary>
    /// The endgame pause ran out: every owed seat still disconnected is played by its role's bot until it reconnects, and
    /// this disconnection's pause is spent.
    /// </summary>
    private void PauseOver()
    {
        foreach (var seat in Owed().Select(Session.SeatOf).Where(s => !s.Connected).ToList())
        {
            Session = Session.With(seat with { Auto = true, PauseUsed = true });
        }
        Rearm(fresh: true);
    }

    /// <summary>
    /// A deadline passed: the actor times out, or, in a vote, every seat still to cast abstains (bots cast for theirs).
    /// </summary>
    private void TimeOut()
    {
        if (Session.Game!.Waiting is VotePending vote)
        {
            foreach (var seat in vote.Owed.ToList())
            {
                if (Session.Game!.Waiting is VotePending still && still.Owed.Contains(seat))
                {
                    Apply(new Meridian.Logic.Timeout(seat));
                }
            }
            return;
        }
        Apply(new Meridian.Logic.Timeout(BotRunner.Actor(Session.Game!)));
    }

    /// <summary>The disconnected seat's grace ran out: a bot plays it until the player comes back.</summary>
    private void HandToBot()
    {
        var seat = Session.SeatOf(BotRunner.Actor(Session.Game!));
        Session = Session.With(seat with { Auto = true });
        Rearm(fresh: true);
    }

    /// <summary>
    /// One bot command for the seat the game waits on, from the bots' own generator, through the same
    /// <see cref="Apply"/> a phone's intent takes. A bot never sends anything illegal, but if it ever did, the seat
    /// times out instead, so the game can't stall.
    /// </summary>
    private void BotStep()
    {
        var game = Session.Game!;
        // In a vote, the bot casts for the first seat still owed that it plays.
        var seat = Owed().FirstOrDefault(s => Session.SeatOf(s).Auto) is { Value: not null } auto ? auto : BotRunner.Actor(game);
        var rng = Session.BotRng;
        IBotPolicy bot = game.Phase == Phase.Endgame && game.RoleOf(seat) is { } role
            ? _pack.Scenarios.Get(game.Board!.Scenario).Bot(role.Role)
            : ExploreBot.Instance;
        var command = bot.Choose(_pack, Visibility.For(game, seat), seat, Rules.Legal(_pack, game, seat), ref rng);
        Session = Session with { BotRng = rng };
        if (!Apply(command))
        {
            Apply(new Meridian.Logic.Timeout(seat));
        }
    }
}
