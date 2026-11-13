using System.Text.Json;
using ArcBox.Sdk;
using Meridian.Logic;

namespace Meridian.Game;

/// <summary>Builds the TV's view and each phone's from the session (spec §5.3).</summary>
internal static class Projection
{
    public const string Module = MeridianGame.Id;

    public static MeridianHostView Host(MeridianGame game)
    {
        var s = game.Session;
        var active = s.Game?.ActiveSeat;
        var seats = s.Seats.Select((x, i) => new SeatCard(x.Player.Value, x.Name,
            x.Crew is { } c ? game.Pack.CrewMember(c).Name : null, i, x.Connected, x.Auto, x.Seat == active)).ToEqArray();
        var banner = s.Phase switch
        {
            SessionPhase.Boarding => "Choose your crew",
            SessionPhase.Over => s.Ending ?? "",
            _ => "Exploring the Meridian",
        };
        return new MeridianHostView(Name(s.Phase), banner, seats);
    }

    public static ControllerView Player(MeridianGame game, PlayerId id)
    {
        var s = game.Session;
        if (s.Find(id) is not { } seat)
        {
            return new WaitView("You're watching this game.");
        }
        return s.Phase switch
        {
            SessionPhase.Boarding => Screen("crewPick", CrewPick(game, seat), game.Deadline),
            _ => new WaitView("On the main screen…"),
        };
    }

    private static CrewPickScreen CrewPick(MeridianGame game, SeatInfo me)
    {
        var s = game.Session;
        var crew = game.Pack.Crew.Select(c => Card(c, TraitSteps.Starting(c.Tracks),
            taken: s.Seats.Any(x => x.Crew == c.Id && x.Player != me.Player), mine: me.Crew == c.Id)).ToEqArray();
        return new CrewPickScreen(crew, s.Seats.Count(x => x.Crew is not null), s.Seats.Count);
    }

    public static CrewCard Card(CrewDef def, TraitSteps steps, bool taken, bool mine)
        => new(def.Id, def.Name, def.Role,
               Traits.All.Select(t => new TrackCard(t, def.Tracks[t].Values, steps[t])).ToEqArray(), taken, mine);

    public static CustomView Screen<T>(string screen, T data, DateTimeOffset? deadline)
        => new(Module, screen, JsonSerializer.SerializeToElement(data, ArcJson.Options)) { DeadlineUtc = deadline };

    private static string Name(SessionPhase phase) => JsonSerializer.Serialize(phase, ArcJson.Options).Trim('"');
}
