using System.Text.Json;
using ArcBox.Sdk;
using ArcBox.Sdk.Protocol;
using ArcBox.Server.Tests.Support;
using Redline.Logic;

namespace ArcBox.Server.Tests.Redline;

/// <summary>
/// Issue #24: a REDLINE race interrupted by a server restart (spec §3.5), and a phone swapped mid-race (spec §3.2), with
/// the real <c>RedlineGame</c> on the fallback question pack (the test server has OpenTDB off). Phase timers run on the
/// rooms' fake clock, so the race only moves when a test advances it.
/// </summary>
public sealed class RedlineRestartTests
{
    private const string Restarted = "Game interrupted: the server restarted.";
    private const string Rejoin = "Couldn't rejoin. Please join again.";

    /// <summary>Longer than any REDLINE phase timer, and shorter than two of them back to back.</summary>
    private static readonly TimeSpan Step = TimeSpan.FromSeconds(30);

    /// <summary>Driver Select (nobody claims, the timer seats everyone), three qualifying laps, the Grid, then round 1.</summary>
    private static readonly string[] ToRound1Throttle =
    [
        "QualifyingReading:0:1", "QualifyingQuestion:0:1", "QualifyingResult:0:1",
        "QualifyingReading:0:2", "QualifyingQuestion:0:2", "QualifyingResult:0:2",
        "QualifyingReading:0:3", "QualifyingQuestion:0:3", "QualifyingResult:0:3",
        "Grid:0:0", "Throttle:1:0",
    ];

    /// <summary>A REDLINE host view's step: phase, round and qualifying lap, unique along a race.</summary>
    private static string Key(JsonElement view) =>
        $"{view.GetProperty("phase").GetString()}:{view.GetProperty("round").GetInt32()}:" +
        (view.TryGetProperty("qualifying", out var q) && q.ValueKind == JsonValueKind.Object ? q.GetProperty("lap").GetInt32() : 0);

    private static string? Message(JsonElement results) => results.GetProperty("message").GetString();

    private static string? Kind(JsonElement view) => view.GetProperty("kind").GetString();

    private static string Text(JsonElement view, string property) =>
        view.TryGetProperty(property, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString()! : "";

    /// <summary>The id of the right answer on a question view, from the fallback pack the test server plays with.</summary>
    private static string RightAnswer(JsonElement view)
    {
        var correct = FallbackQuestions.All.First(q => q.Text == Text(view, "prompt")).Correct;
        return view.GetProperty("options").EnumerateArray().First(o => o.GetProperty("label").GetString() == correct).GetProperty("id").GetString()!;
    }

    /// <summary>The host view's row for the car <paramref name="playerName"/> drives.</summary>
    private static JsonElement Row(JsonElement view, string playerName) =>
        view.GetProperty("drivers").EnumerateArray().Single(d => d.TryGetProperty("playerName", out var n) && n.GetString() == playerName);

    /// <summary>Runs the current phase's timer out, then waits for the host view of <paramref name="key"/>.</summary>
    private static async Task<JsonElement> NextAsync(ArcServerFactory f, TestClient host, string key)
    {
        f.Time.Advance(Step);
        return await host.WaitForAsync(ClientMethods.HostView, v => Key(v) == key);
    }

    private static async Task<JsonElement> ThroughAsync(ArcServerFactory f, TestClient host, params string[] keys)
    {
        var view = default(JsonElement);
        foreach (var key in keys)
        {
            view = await NextAsync(f, host, key);
        }
        return view;
    }

    /// <summary>A room on <paramref name="f"/> with Ann (p1, the VIP) and Bob (p2), running REDLINE, at its Driver Select.</summary>
    private static async Task<(TestClient Host, TestClient Ann, TestClient Bob, CreateRoomResult Room, JoinResult AnnSeat, JoinResult BobSeat)>
        StartRaceAsync(ArcServerFactory f)
    {
        var host = await TestClient.ConnectAsync(f);
        var ann = await TestClient.ConnectAsync(f);
        var bob = await TestClient.ConnectAsync(f);
        var room = await host.InvokeAsync<CreateRoomResult>("CreateRoom");
        var annSeat = await ann.TakeSeatAsync(new TakeSeatRequest(room.Code, NewName: "Ann"));
        var bobSeat = await bob.TakeSeatAsync(new TakeSeatRequest(room.Code, NewName: "Bob"));
        await ann.InvokeAsync("SelectGame", "redline");
        await ann.InvokeAsync("StartGame");
        await host.WaitForAsync(ClientMethods.HostView, v => Key(v) == "DriverSelect:0:0");
        return (host, ann, bob, room, annSeat, bobSeat);
    }

    [Fact]
    public async Task Race_interrupted_by_restart_returns_to_lobby_with_note()
    {
        using var db = new TempDb();
        CreateRoomResult room;
        JoinResult ann, bob;
        using (var a = new ArcServerFactory(db.File))
        {
            var (host, annPhone, bobPhone, created, annSeat, bobSeat) = await StartRaceAsync(a);
            await using var _h = host;
            await using var _a = annPhone;
            await using var _b = bobPhone;
            (room, ann, bob) = (created, annSeat, bobSeat);

            // Mid-race: round 1's question is open on both phones when the server goes away.
            await ThroughAsync(a, host, [.. ToRound1Throttle, "Reading:1:0", "Question:1:0"]);
            Assert.Equal("choice", Kind(await annPhone.WaitForAsync(ClientMethods.ControllerView, v => Kind(v) == "choice" && Text(v, "header").Contains("ROUND 1/"))));
        }

        using var b = new ArcServerFactory(db.File);

        // The TV comes back to the same room, in the lobby, and is told once why the race is gone.
        await using var tv = await TestClient.ConnectAsync(b);
        await tv.InvokeAsync("ResumeHost", room.Code, room.HostToken);
        var lobby = await tv.WaitForAsync(ClientMethods.LobbyChanged);
        Assert.Equal(room.Code, lobby.GetProperty("code").GetString());
        Assert.False(lobby.GetProperty("inGame").GetBoolean());
        Assert.Equal("redline", lobby.GetProperty("selectedGameId").GetString());
        Assert.Equal(Restarted, Message(lobby.GetProperty("lastResults")));
        var results = await tv.WaitForAsync(ClientMethods.GameEnded);
        Assert.Equal(Restarted, Message(results));
        Assert.Equal(0, results.GetProperty("scores").GetArrayLength());

        // Both phones rejoin their seats with the secrets they kept: the note, then the same room's lobby.
        await using var annPhone2 = await TestClient.ConnectAsync(b);
        await using var bobPhone2 = await TestClient.ConnectAsync(b);
        await annPhone2.InvokeAsync("Rejoin", room.Code, ann.PlayerId.Value, ann.Secret);
        await bobPhone2.InvokeAsync("Rejoin", room.Code, bob.PlayerId.Value, bob.Secret);
        foreach (var phone in new[] { annPhone2, bobPhone2 })
        {
            Assert.Equal(Restarted, Message(await phone.WaitForAsync(ClientMethods.GameEnded)));
            var phoneLobby = await phone.WaitForAsync(ClientMethods.LobbyChanged);
            Assert.Equal(room.Code, phoneLobby.GetProperty("code").GetString());
            Assert.False(phoneLobby.GetProperty("inGame").GetBoolean());
        }
        await tv.WaitForAsync(ClientMethods.LobbyChanged, l => l.GetProperty("players").EnumerateArray().All(p => p.GetProperty("connected").GetBoolean()));

        // Nothing of the old race reaches anyone, however long the clock runs: its timers died with the old server.
        b.Time.Advance(Step);
        Assert.Equal(0, tv.Unclaimed(ClientMethods.HostView));
        Assert.Equal(0, annPhone2.Unclaimed(ClientMethods.ControllerView));
        Assert.Equal(0, bobPhone2.Unclaimed(ClientMethods.ControllerView));

        // The room is a working lobby: the VIP starts a new race, from Driver Select.
        await annPhone2.InvokeAsync("StartGame");
        Assert.Equal("DriverSelect:0:0", Key(await tv.WaitForAsync(ClientMethods.HostView)));
        Assert.Equal("Pick your driver", (await bobPhone2.WaitForAsync(ClientMethods.ControllerView)).GetProperty("prompt").GetString());
        Assert.Equal(0, tv.Unclaimed(ClientMethods.GameEnded));
    }

    [Fact]
    public async Task Swapped_phone_takes_offline_seat_mid_question()
    {
        using var f = new ArcServerFactory();
        var (host, ann, bob, room, _, bobSeat) = await StartRaceAsync(f);
        await using var _h = host;
        await using var _a = ann;

        // Round 1: Bob pushes and answers right, so his car is 3 ahead of the line by the time he swaps phones.
        await ThroughAsync(f, host, ToRound1Throttle);
        var throttle = await bob.WaitForAsync(ClientMethods.ControllerView, v => Kind(v) == "buttons");
        await bob.InvokeAsync("SendInput", throttle.GetProperty("viewId").GetString(), new { pressed = "push" });
        await NextAsync(f, host, "Reading:1:0");
        await NextAsync(f, host, "Question:1:0");
        var question1 = await bob.WaitForAsync(ClientMethods.ControllerView, v => Kind(v) == "choice" && Text(v, "header").Contains("ROUND 1/"));
        await bob.InvokeAsync("SendInput", question1.GetProperty("viewId").GetString(), new { selected = new[] { RightAnswer(question1) } });
        await host.WaitForAsync(ClientMethods.HostView, v => Key(v) == "Question:1:0" && Row(v, "Bob").GetProperty("status").GetString() == "Answered");
        var reveal = await NextAsync(f, host, "Reveal:1:0");
        Assert.Equal("Correct", Row(reveal, "Bob").GetProperty("outcome").GetString());
        Assert.Equal(3, Row(reveal, "Bob").GetProperty("distance").GetInt32());

        // Round 2's question is open when Bob's phone dies.
        var before = Row(await ThroughAsync(f, host, "Throttle:2:0", "Reading:2:0", "Question:2:0"), "Bob");
        var distance = before.GetProperty("distance").GetInt32();
        var position = before.GetProperty("position").GetInt32();
        var code = before.GetProperty("code").GetString();
        Assert.Equal(3, distance);
        await bob.DisposeAsync();
        await host.WaitForAsync(ClientMethods.HostView, v => Key(v) == "Question:2:0" && !Row(v, "Bob").GetProperty("connected").GetBoolean());

        // A new phone with nothing stored: Peek offers Bob's offline seat, and taking it rotates the secret.
        await using var newPhone = await TestClient.ConnectAsync(f);
        var peek = await newPhone.PeekAsync(room.Code);
        Assert.True(peek.InGame);
        var offer = Assert.Single(peek.Offers);
        Assert.Equal(new SeatRef(Slot: bobSeat.PlayerId), offer.Ref);
        var taken = await newPhone.TakeSeatAsync(new TakeSeatRequest(room.Code, Seat: offer.Ref));
        Assert.Equal(bobSeat.PlayerId, taken.PlayerId);
        Assert.NotEqual(bobSeat.Secret, taken.Secret);

        // It gets the question that is open now, as Bob's car: same driver, same distance, same place.
        var view = await newPhone.WaitForAsync(ClientMethods.ControllerView);
        Assert.Equal("choice", Kind(view));
        var hostView = await host.WaitForAsync(ClientMethods.HostView, v => Key(v) == "Question:2:0" && Row(v, "Bob").GetProperty("connected").GetBoolean());
        Assert.Equal(hostView.GetProperty("question").GetProperty("text").GetString(), view.GetProperty("prompt").GetString());
        Assert.EndsWith($"P{position} · ROUND 2/15", Text(view, "header"));
        var after = Row(hostView, "Bob");
        Assert.Equal(code, after.GetProperty("code").GetString());
        Assert.Equal(distance, after.GetProperty("distance").GetInt32());
        Assert.Equal(position, after.GetProperty("position").GetInt32());

        // The old phone's secret no longer works.
        await using var oldPhone = await TestClient.ConnectAsync(f);
        Assert.Equal(Rejoin, await oldPhone.ThrowsHubAsync("Rejoin", room.Code, bobSeat.PlayerId.Value, bobSeat.Secret));

        // The new phone's answer is Bob's: the round's Reveal scores it.
        await newPhone.InvokeAsync("SendInput", view.GetProperty("viewId").GetString(), new { selected = new[] { "b" } });
        await host.WaitForAsync(ClientMethods.HostView, v => Key(v) == "Question:2:0" && Row(v, "Bob").GetProperty("status").GetString() == "Answered");
        var reveal2 = await NextAsync(f, host, "Reveal:2:0");
        var scored = Row(reveal2, "Bob");
        Assert.Equal(code, scored.GetProperty("code").GetString());
        Assert.NotEqual("NoAnswer", scored.GetProperty("outcome").GetString());
        Assert.Equal(distance, scored.GetProperty("previousDistance").GetInt32());
        var result = await newPhone.WaitForAsync(ClientMethods.ControllerView, v => Kind(v) == "wait" && Text(v, "header").Contains("ROUND 2/")
            && !Text(v, "message").StartsWith("Answer locked", StringComparison.Ordinal));
        Assert.Matches("^(Correct!|Wrong answer)", Text(result, "message"));
    }

    /// <summary>A database file in its own temp directory, deleted (with its WAL files) on dispose.</summary>
    private sealed class TempDb : IDisposable
    {
        private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("arcbox-redline-restart-");

        public string File => Path.Combine(_dir.FullName, "arcbox.db");

        public void Dispose()
        {
            try { _dir.Delete(true); } catch (IOException) { }
        }
    }
}
