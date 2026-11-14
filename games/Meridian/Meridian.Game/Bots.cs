using Meridian.Logic;

namespace Meridian.Game;

// Disconnects and bots (spec §3.3, Explore rows; M2 plan, Task 4).
public sealed partial class MeridianGame
{
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
        var seat = BotRunner.Actor(game);
        var rng = Session.BotRng;
        var command = ExploreBot.Instance.Choose(_pack, Visibility.For(game, seat), seat, Rules.Legal(_pack, game, seat), ref rng);
        Session = Session with { BotRng = rng };
        if (!Apply(command))
        {
            Apply(new Meridian.Logic.Timeout(seat));
        }
    }
}
