using Meridian.Presentation.Beats;
using Meridian.Presentation.Board;

namespace Meridian.Presentation.Tests.Board;

public sealed class CrewMotionTests
{
    // An L of four rooms on Habitat: a(0,0) - b(1,0) - c(1,1), and d(0,1) joined to a only.
    private static CompartmentCard Room(string id, int x, int y, params Side[] hatches)
        => new(id, "storage_bay", null, new Cell(DeckId.Habitat, x, y), 0, [.. hatches], null, null);

    private static readonly EqArray<CompartmentCard> Rooms =
    [
        Room("a", 0, 0, Side.E, Side.S), Room("b", 1, 0, Side.W, Side.S), Room("c", 1, 1, Side.N), Room("d", 0, 1, Side.N),
        new("up", "command_lift_landing", null, new Cell(DeckId.Command, 0, 0), 0, [Side.S], null, null),
    ];

    private static MeridianHostView View(string at, bool echo = false, Presented? now = null)
        => new("explore", "", [new SeatCard("p1", "Ann", "Ines Okafor", 0, true, false, true)], Rooms,
               [new CrewMarker("p1", "Ann", at, 0, echo)], 40, 0, 1, now, []);

    private static void Run(CrewMotion motion, double seconds)
    {
        for (var t = 0.0; t < seconds; t += 1.0 / 60)
        {
            motion.Tick(1.0 / 60);
        }
    }

    [Fact]
    public void A_move_is_a_walk_through_the_rooms_between_facing_each_step()
    {
        // Review focus 4.
        var motion = new CrewMotion();
        motion.Receive(View("a"), null);
        Assert.Equal((0.0, 0.0, CrewLook.Idle), (motion.Poses[0].X, motion.Poses[0].Y, motion.Poses[0].State));
        motion.Receive(View("c"), null);                     // a -> b -> c
        Run(motion, 0.15);
        var pose = motion.Poses[0];
        Assert.Equal((CrewLook.Walk, Side.E), (pose.State, pose.Facing));
        Run(motion, 1.0 / CrewMotion.CellsPerSecond);
        Assert.Equal(Side.S, motion.Poses[0].Facing);         // turned the corner at b
        Run(motion, 2);
        Assert.Equal((1.0, 1.0, CrewLook.Idle, Side.S), (motion.Poses[0].X, motion.Poses[0].Y, motion.Poses[0].State, motion.Poses[0].Facing));
    }

    [Theory]
    [InlineData("b", Side.E)]
    [InlineData("d", Side.S)]
    public void Every_side_is_a_facing(string to, Side facing)
    {
        var motion = new CrewMotion();
        motion.Receive(View("a"), null);
        motion.Receive(View(to), null);
        Run(motion, 0.1);
        Assert.Equal(facing, motion.Poses[0].Facing);
        motion.Receive(View("a"), null);
        Run(motion, 0.1);
        Assert.Equal(facing.Opposite(), motion.Poses[0].Facing);
    }

    [Fact]
    public void A_lift_ride_and_a_first_look_are_cuts_not_walks()
    {
        var motion = new CrewMotion();
        motion.Receive(View("c"), null);
        Assert.Equal((1.0, 1.0), (motion.Poses[0].X, motion.Poses[0].Y));   // placed, not walked in
        motion.Receive(View("up"), null);
        Assert.Equal((DeckId.Command, CrewLook.Idle), (motion.Poses[0].Deck, motion.Poses[0].State));
    }

    [Fact]
    public void A_fight_hurts_its_target_and_a_death_falls_then_echoes()
    {
        var rules = new MeridianRules();
        var fight = new Presented(4, "fight", new SeatId("p2"), "Brood attacks Ann", "4 against 2: hit.", 1.5, Target: "p1");
        var motion = new CrewMotion();
        motion.Receive(View("a"), Beat.For(fight, rules));
        Assert.Equal(CrewLook.Hurt, motion.Poses[0].State);
        var death = new Presented(5, "death", new SeatId("p1"), "Ann is dead", "They stay as an echo.", 4);
        motion.Receive(View("a", echo: true), Beat.For(death, rules));
        Assert.Equal(CrewLook.Down, motion.Poses[0].State);
        motion.Receive(View("a", echo: true), null);
        Assert.Equal(CrewLook.Echo, motion.Poses[0].State);
    }

    [Fact]
    public void Real_games_walk_only_between_compartments_joined_by_open_hatches()
    {
        var game = new Harness(players: 4, seed: 2, ThePassenger.Id).Board();
        var motion = new CrewMotion();
        var walks = 0;
        game.PlayUntil(() =>
        {
            var view = (MeridianHostView)game.Game.HostView();
            motion.Receive(view, null);
            walks += motion.Poses.Count(p => p.Walking);
            Run(motion, 3);   // every walk finishes
            foreach (var pose in motion.Poses)
            {
                var room = view.Compartments.First(c => c.Id == pose.At);
                Assert.Equal((room.At.Deck, (double)room.At.X, (double)room.At.Y), (pose.Deck, pose.X, pose.Y));
            }
            return false;
        });
        Assert.True(walks > 10, $"{walks} walks");
    }
}
