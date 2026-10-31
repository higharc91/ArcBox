using System.Diagnostics;
using System.Text.Json;
using ArcBox.Sdk;
using ArcBox.Sdk.Protocol;
using ArcBox.Server.Tests.Support;

namespace ArcBox.Server.Tests.Tactics;

/// <summary>
/// ArcBox Tactics over the wire: the real server, a TV and four phones on SignalR, from the lobby's campaign step to a
/// finished battle. Phones end their turns and pass on reactions; the TV answers every beat with <c>shown</c>, so the AI
/// is paced by the signal alone (the rooms' fake clock never moves).
/// </summary>
public sealed class TacticsEndToEndTests
{
    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static Task Send(TestClient phone, JsonElement view, object payload) =>
        phone.InvokeAsync("SendInput", Str(view, "viewId"), JsonSerializer.SerializeToElement(payload, ArcJson.Options));

    private static Func<JsonElement, bool> Screen(string screen) => v => Str(v, "kind") == "custom" && Str(v, "screen") == screen;

    [Fact]
    public async Task A_campaign_session_plays_a_battle_to_its_results_and_saves_the_party()
    {
        await using var f = new ArcServerFactory(withTactics: true);
        await using var tv = await TestClient.ConnectAsync(f);
        var room = await tv.InvokeAsync<CreateRoomResult>("CreateRoom");
        string[] names = ["Ann", "Bob", "Cy", "Dee"];
        string[] classes = ["fighter", "cleric", "rogue", "wizard"];
        var phones = new List<TestClient>();
        foreach (var name in names)
        {
            var phone = await TestClient.ConnectAsync(f);
            phones.Add(phone);
            await phone.TakeSeatAsync(new TakeSeatRequest(room.Code, NewName: name));
        }
        try
        {
            // The lobby's campaign step: Ann (the VIP) creates the campaign, the others join it as new members.
            await phones[0].InvokeAsync("SelectGame", "tactics");
            await phones[0].InvokeAsync<ProfileKey>("CreateCampaign", "Embers");
            foreach (var phone in phones.Skip(1))
                await phone.InvokeAsync<ProfileKey?>("ClaimMember", new ClaimMemberRequest(null));
            await phones[0].InvokeAsync("StartGame");

            // Muster and the battle pick.
            for (var i = 0; i < 4; i++)
                await Send(phones[i], await phones[i].WaitForAsync(ClientMethods.ControllerView, Screen("pickClass")),
                    new { action = "pickClass", data = new { @class = classes[i] } });
            await Send(phones[0], await phones[0].WaitForAsync(ClientMethods.ControllerView, Screen("pickEncounter")),
                new { action = "pickEncounter", data = new { encounter = "river_crossing" } });

            var latest = new Dictionary<TestClient, JsonElement>();
            var phase = await PlayAsync(tv, phones, latest);
            Assert.Equal("Results", phase);

            // Ann ends the session; the lobby's roster now shows the saved characters.
            var results = latest.TryGetValue(phones[0], out var seen) && Screen("results")(seen)
                ? seen
                : await phones[0].WaitForAsync(ClientMethods.ControllerView, Screen("results"));
            await Send(phones[0], results, new { action = "end" });
            await tv.WaitForAsync(ClientMethods.GameEnded);
            // The first lobby after the game (earlier ones, from before the session, show nobody with a character yet).
            var lobby = await tv.WaitForAsync(ClientMethods.LobbyChanged, l =>
                l.TryGetProperty("campaign", out var c) && c.ValueKind == JsonValueKind.Object
                && c.GetProperty("roster").EnumerateArray().Any(r => Str(r.GetProperty("card"), "subtitle")?.Contains("Lv") == true));
            var cards = lobby.GetProperty("campaign").GetProperty("roster").EnumerateArray()
                .Select(r => Str(r.GetProperty("card"), "subtitle")).ToArray();
            Assert.Equal(["Human Fighter · Lv 1", "Human Cleric · Lv 1", "Human Rogue · Lv 1", "Human Wizard · Lv 1"], cards);
        }
        finally
        {
            foreach (var phone in phones)
                await phone.DisposeAsync();
        }
    }

    /// <summary>
    /// Plays the battle out: each phone's turn ends at once and each reaction is passed; the TV signals <c>shown</c> for
    /// every new beat. Returns the phase the host view settles on (Results, unless it stalls); <paramref name="latest"/> keeps
    /// each phone's last view, which the loop has taken.
    /// </summary>
    private static async Task<string?> PlayAsync(TestClient tv, List<TestClient> phones, Dictionary<TestClient, JsonElement> latest)
    {
        var lastSeq = 0;
        string? phase = null;
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < TimeSpan.FromSeconds(120) && phase != "Results")
        {
            var acted = false;
            foreach (var phone in phones)
                while (phone.Unclaimed(ClientMethods.ControllerView) > 0)
                {
                    var view = await phone.WaitForAsync(ClientMethods.ControllerView);
                    latest[phone] = view;
                    if (Screen("turn")(view))
                        await Send(phone, view, new { action = "endTurn" });
                    else if (Screen("react")(view))
                        await Send(phone, view, new { action = "react", data = new { choice = "pass" } });
                    acted = true;
                }
            while (tv.Unclaimed(ClientMethods.HostView) > 0)
            {
                var host = await tv.WaitForAsync(ClientMethods.HostView);
                phase = Str(host, "phase");
                if (host.TryGetProperty("battle", out var battle) && battle.ValueKind == JsonValueKind.Object
                    && battle.GetProperty("beats").EnumerateArray().LastOrDefault() is { ValueKind: JsonValueKind.Object } beat
                    && beat.GetProperty("seq").GetInt32() is var seq && seq > lastSeq)
                {
                    lastSeq = seq;
                    // The hub rate-limits each connection; a real TV animating the beat is far slower, so back off as one would.
                    var payload = JsonSerializer.SerializeToElement(new { seq }, ArcJson.Options);
                    while (await tv.TryInvokeMessageAsync("HostSignal", "shown", payload) == "Slow down.")
                        await Task.Delay(250);
                }
                acted = true;
            }
            if (!acted)
                await Task.Delay(10);
        }
        return phase;
    }
}
