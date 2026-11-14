using System.Text.Json;
using ArcBox.Sdk;
using ArcBox.Sdk.Protocol;
using ArcBox.Server.Tests.Support;

namespace ArcBox.Server.Tests.Meridian;

/// <summary>
/// Meridian end to end over the real hub (M2 plan, Task 10; M3 plan, Task 14): a TV and three phones, from creating the
/// room through Explore, the Breach, the briefings and each scenario's endgame to the Debrief, and back to the lobby. The phones decide only from what their screens show, as a person would; the TV signals each
/// presented event; the server's fake clock stands in for waiting.
/// </summary>
public sealed class MeridianHubTests
{
    [Theory]
    [Trait("Category", "Integration")]
    [InlineData("the_passenger")]
    [InlineData("one_of_us_isnt")]
    [InlineData("tiller_says_no")]
    [InlineData("decaying_orbit")]
    [InlineData("salvage_rights")]
    public async Task Three_phones_play_through_the_breach_and_the_room_returns_to_the_lobby(string scenario)
    {
        using var f = new ArcServerFactory(withTactics: false, withMeridian: true) { MeridianScenario = scenario };
        await using var host = await TestClient.ConnectAsync(f);
        var room = await host.InvokeAsync<CreateRoomResult>("CreateRoom");
        var phones = new List<TestClient>();
        foreach (var name in new[] { "Ann", "Bo", "Cy" })
        {
            var phone = await TestClient.ConnectAsync(f);
            await phone.InvokeAsync<JoinResult>("Join", room.Code, name);
            phones.Add(phone);
        }
        try
        {
            await phones[0].InvokeAsync("SelectGame", "meridian");
            await phones[0].InvokeAsync("StartGame");

            var random = new Random(7);
            var acted = new string?[phones.Count];
            var views = new JsonElement?[phones.Count];
            var presented = 0;
            var picked = new HashSet<string>();
            var seen = new HashSet<string>();
            string? title = null;
            JsonElement? ended = null;
            for (var step = 0; step < 6000 && ended is null; step++)
            {
                var busy = false;
                var hostView = host.TakeLatest(ClientMethods.HostView);
                if (hostView is { } hv)
                {
                    title ??= Title(hv);
                }
                if (hostView is { } shownView
                    && shownView.TryGetProperty("now", out var now) && now.ValueKind == JsonValueKind.Object
                    && now.GetProperty("seq").GetInt32() is var seq && seq > presented)
                {
                    presented = seq;
                    await host.InvokeAsync("HostSignal", MeridianSignal, JsonSerializer.SerializeToElement(new { seq }));
                    busy = true;
                }
                for (var p = 0; p < phones.Count; p++)
                {
                    views[p] = phones[p].TakeLatest(ClientMethods.ControllerView) ?? views[p];
                    if (phones[p].TakeLatest(ClientMethods.GameEnded) is { } end)
                    {
                        ended = end;
                    }
                    if (views[p] is { } shown && shown.TryGetProperty("screen", out var s))
                    {
                        seen.Add(s.GetString()!);
                    }
                    if (views[p] is not { } view || view.GetProperty("viewId").GetString() is not { } id || id == acted[p])
                    {
                        continue;
                    }
                    if (Decide(view, random, picked) is { } input)
                    {
                        acted[p] = id;
                        await phones[p].InvokeAsync("SendInput", id, input);
                        busy = true;
                    }
                }
                if (busy)
                {
                    // The hub allows about 20 messages a second per connection, on the real clock; stay under it.
                    await Task.Delay(70, TestContext.Current.CancellationToken);
                }
                else
                {
                    f.Time.Advance(TimeSpan.FromSeconds(1));   // nothing to do: let deadlines and fallbacks run
                    await Task.Delay(5, TestContext.Current.CancellationToken);
                }
            }

            // The forced scenario ran, every phone read a briefing, and the game went through the Debrief.
            Assert.Equal(ScenarioTitle(f, scenario), title);
            Assert.Contains("briefing", seen);
            Assert.Contains("debrief", seen);

            var results = Assert.IsType<JsonElement>(ended);
            Assert.False(string.IsNullOrWhiteSpace(results.GetProperty("message").GetString()));   // the scenario's outcome
            Assert.Equal(3, results.GetProperty("scores").GetArrayLength());
            await host.WaitForAsync(ClientMethods.LobbyChanged, l => !l.GetProperty("inGame").GetBoolean() && l.TryGetProperty("lastResults", out var r) && r.ValueKind == JsonValueKind.Object);
        }
        finally
        {
            foreach (var phone in phones)
            {
                await phone.DisposeAsync();
            }
        }
    }

    private const string MeridianSignal = "presented";

    private static string? Title(JsonElement hostView)
        => hostView.TryGetProperty("scenario", out var panel) && panel.ValueKind == JsonValueKind.Object
            ? panel.GetProperty("title").GetString()
            : null;

    private static string ScenarioTitle(ArcServerFactory f, string id)
    {
        var file = Path.Combine(f.ContentRoot, "meridian", "base", "rules", "scenarios", id + ".json");
        return JsonDocument.Parse(File.ReadAllText(file)).RootElement.GetProperty("title").GetString()!;
    }

    /// <summary>What a phone does with the screen in front of it, from the screen alone.</summary>
    private static object? Decide(JsonElement view, Random random, HashSet<string> picked)
    {
        if (view.GetProperty("kind").GetString() != "custom")
        {
            return null;
        }
        var data = view.GetProperty("data");
        object Act(string action, object? payload = null) => payload is null ? new { action } : new { action, data = payload };
        switch (view.GetProperty("screen").GetString())
        {
            case "crewPick":
                var free = data.GetProperty("crew").EnumerateArray()
                    .FirstOrDefault(c => !c.GetProperty("taken").GetBoolean() && !c.GetProperty("mine").GetBoolean()
                                         && !picked.Contains(c.GetProperty("id").GetString()!));
                if (free.ValueKind != JsonValueKind.Object || data.GetProperty("crew").EnumerateArray().Any(c => c.GetProperty("mine").GetBoolean()))
                {
                    return null;
                }
                picked.Add(free.GetProperty("id").GetString()!);
                return Act("pick", new { crew = free.GetProperty("id").GetString() });
            case "turn":
                // The endgame first: a scenario action, an attack, or a monster to command, each often enough to matter.
                if (Pick(data, "actions", random) is { } a && random.Next(2) == 0)
                {
                    var id = a.GetProperty("id").GetString();
                    return a.GetProperty("data").ValueKind == JsonValueKind.String
                        ? Act("act", new { action = id, data = a.GetProperty("data").GetString() })
                        : Act("act", new { action = id });
                }
                if (Pick(data, "attacks", random) is { } target && random.Next(2) == 0)
                {
                    return Act("attack", new { target = target.GetProperty("id").GetString() });
                }
                if (Pick(data, "monsters", random) is { } monster && random.Next(2) == 0)
                {
                    var mid = monster.GetProperty("id").GetString();
                    if (Pick(monster, "targets", random) is { } prey)
                    {
                        return Act("monsterAttack", new { monster = mid, target = prey.GetProperty("id").GetString() });
                    }
                    if (monster.GetProperty("moves").EnumerateArray().ToList() is { Count: > 0 } moves)
                    {
                        return Act("monsterMove", new { monster = mid, to = moves[random.Next(moves.Count)].GetString() });
                    }
                }
                var opens = data.GetProperty("opens").EnumerateArray().ToList();
                if (opens.Count > 0)
                {
                    var o = opens[random.Next(opens.Count)];
                    return Act("open", new { at = o.GetProperty("at").GetString(), side = o.GetProperty("side").GetString() });
                }
                var reach = data.GetProperty("reach").EnumerateArray().ToList();
                if (reach.Count > 0 && random.Next(4) > 0)
                {
                    return Act("moveTo", new { to = reach[random.Next(reach.Count)].GetProperty("id").GetString() });
                }
                return Act("endTurn");
            case "orient":
                return Act("orient", new { rotation = data.GetProperty("options")[0].GetProperty("rotation").GetInt32() });
            case "roll":
                return Act("roll");
            case "card":
                return Act("choose", new { option = data.GetProperty("options")[0].GetProperty("id").GetString() });
            case "trade":
                return Act("answerTrade", new { accept = false });
            case "briefing":
                return data.GetProperty("ready").GetBoolean() ? null : Act("ready");
            case "allocate":
                return Act("allocate", new { first = random.Next(data.GetProperty("steps").GetInt32() + 1) });
            case "vote":
                return data.GetProperty("cast").GetBoolean() ? null
                    : Act("vote", new { choice = Pick(data, "options", random)!.Value.GetProperty("id").GetString() });
            case "echo":
                return !data.GetProperty("lent").GetBoolean() && Pick(data, "targets", random) is { } to
                    ? Act("lend", new { to = to.GetProperty("id").GetString() })
                    : Act("endTurn");
            case "debrief":
                return data.GetProperty("vip").GetBoolean() ? Act("continue") : null;
            default:
                return null;   // sheet, watch, over: nothing to send
        }
    }

    private static JsonElement? Pick(JsonElement data, string array, Random random)
        => data.TryGetProperty(array, out var items) && items.ValueKind == JsonValueKind.Array && items.GetArrayLength() > 0
            ? items[random.Next(items.GetArrayLength())]
            : null;
}
