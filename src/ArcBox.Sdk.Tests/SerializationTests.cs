using System.Text.Json;
using ArcBox.Sdk;
using Xunit;

namespace ArcBox.Sdk.Tests;

public class SerializationTests
{
    public static TheoryData<ControllerView> AllKinds => new()
    {
        new WaitView("Waiting for players...") { ViewId = "v1", Header = "Round 1" },
        new TextEntryView("Your answer", 50, "type here") { ViewId = "v2" },
        new ChoiceView("Pick", [new Option("a", "A"), new Option("b", "B")], true, 2) { ViewId = "v3" },
        new ButtonsView("Tap a button", [new Option("btn1", "Button 1")]) { ViewId = "v4" },
        new DrawView("Draw a cat", ["#ff0000", "#00ff00"]) { ViewId = "v5" },
    };

    [Fact]
    public void PlayerId_serializes_as_plain_string()
        => Assert.Equal("\"p1\"", JsonSerializer.Serialize(new PlayerId("p1"), ArcJson.Options));

    [Fact]
    public void Option_hint_serializes_camelCase_and_round_trips()
    {
        ControllerView v = new ButtonsView(null, [new Option("redline", "REDLINE", "+4 if right · spin out if wrong")]) { ViewId = "v" };
        var json = JsonSerializer.SerializeToElement(v, ArcJson.Options);
        Assert.Equal("+4 if right · spin out if wrong", json.GetProperty("buttons")[0].GetProperty("hint").GetString());
        Assert.Equal(json.GetRawText(), JsonSerializer.SerializeToElement(JsonSerializer.Deserialize<ControllerView>(json, ArcJson.Options), ArcJson.Options).GetRawText());
    }

    [Fact]
    public void ControllerView_includes_kind_discriminator_and_camelCase()
    {
        ControllerView v = new ChoiceView("Pick", [new Option("a", "A")]) { ViewId = "v3" };
        var json = JsonSerializer.SerializeToElement(v, ArcJson.Options);
        Assert.Equal("choice", json.GetProperty("kind").GetString());
        Assert.Equal("v3", json.GetProperty("viewId").GetString());
        Assert.Equal("A", json.GetProperty("options")[0].GetProperty("label").GetString());
    }

    [Theory]
    [MemberData(nameof(AllKinds))]
    public void Every_widget_round_trips(ControllerView view)
    {
        var json = JsonSerializer.Serialize(view, ArcJson.Options);
        var back = JsonSerializer.Deserialize<ControllerView>(json, ArcJson.Options);
        Assert.Equal(json, JsonSerializer.Serialize(back, ArcJson.Options));
    }

    [Fact]
    public void PlayerInput_reads_typed_payload()
    {
        var input = new PlayerInput(JsonSerializer.SerializeToElement(new { pressed = "go" }));
        Assert.Equal("go", input.Read<ButtonsInput>().Pressed);
    }
}
