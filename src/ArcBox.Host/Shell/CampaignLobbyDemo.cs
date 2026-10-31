using ArcBox.Sdk;
using ArcBox.Sdk.Campaigns;
using ArcBox.Sdk.Protocol;
using Tactics.Game;

namespace ArcBox.Host.Shell;

/// <summary>
/// <c>--demo=campaign-lobby</c>: a built-in lobby with a campaign game selected, its campaign list and a selected campaign
/// whose roster has seated and away members, so the lobby can be screenshotted without a server or phones.
/// </summary>
public static class CampaignLobbyDemo
{
    public static LobbyState Lobby()
    {
        var ann = new PlayerId("p_ann");
        var ben = new PlayerId("p_ben");
        var cat = new PlayerId("p_cat");
        var dev = new PlayerId("p_dev");
        ProfileId Profile(string name) => new($"pr_{name.PadRight(16, '0')}");

        var players = new List<LobbyPlayer>
        {
            new(ann, "Ann", IsVip: true, Connected: true, Profile("ann")),
            new(ben, "Ben", IsVip: false, Connected: true, Profile("ben")),
            new(cat, "Cat", IsVip: false, Connected: false, Profile("cat")),
            new(dev, "Dev", IsVip: false, Connected: true, Profile("dev")),
        };
        var games = new List<GameManifest>
        {
            new("redline", "REDLINE", "Racing trivia: bet your throttle on the category, then answer. 22-car 2026 grid.", 2, 8),
            new("counter", "Counter", "Mash the button. Most presses in 20 seconds wins.", 1, 8),
            new("tactics", Brand.Name, Brand.Tagline, 2, 8, UsesCampaigns: true),
        };
        var updated = new DateTimeOffset(2026, 10, 28, 21, 0, 0, TimeSpan.Zero);
        var campaigns = new List<CampaignSummary>
        {
            new("c_embers", "BRKD", "Embers of the Vale", 5, updated),
            new("c_salt", "TQMX", "The Salt Road", 3, updated.AddDays(-6)),
            new("c_kestrel", "HZNW", "Night at the Kestrel", 4, updated.AddDays(-20)),
        };
        var roster = new List<RosterEntry>
        {
            new(Profile("ann"), new SeatCard("Aldric", "Dwarf Cleric · Lv 3"), ann, Connected: true),
            new(Profile("ben"), new SeatCard("Brin", "Elf Ranger · Lv 3"), ben, Connected: true),
            new(Profile("cat"), new SeatCard("Corra", "Human Rogue · Lv 2"), cat, Connected: false),
            new(Profile("dun"), new SeatCard("Dunmore", "Gnome Wizard · Lv 3"), null, Connected: false),
            new(Profile("dev"), new SeatCard("Esk", "Orc Fighter · Lv 3", Color: "#9b59ff"), dev, Connected: true),
        };

        return new LobbyState("KQZT", players, "tactics", games, InGame: false, CanStart: true, LastResults: null,
            Campaign: new LobbyCampaign("c_embers", "BRKD", "Embers of the Vale", roster),
            Campaigns: campaigns);
    }
}
