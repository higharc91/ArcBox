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
        new ShakeView("Shake to build pressure!", "#ff8000", CanFire: true, InitialCharge: 0.35) { ViewId = "v6" },
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
    public void Option_without_hint_has_no_hint_property()
    {
        ControllerView v = new ButtonsView(null, [new Option("go", "GO")]) { ViewId = "v" };
        var json = JsonSerializer.SerializeToElement(v, ArcJson.Options);
        Assert.False(json.GetProperty("buttons")[0].TryGetProperty("hint", out _));
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
    public void ShakeView_serializes_kind_shake_with_camelCase_fields()
    {
        ControllerView v = new ShakeView("Pop the cork!", "#dc0000", CanFire: true, InitialCharge: 0.4) { ViewId = "v7" };
        var json = JsonSerializer.SerializeToElement(v, ArcJson.Options);
        Assert.Equal("shake", json.GetProperty("kind").GetString());
        Assert.Equal("Pop the cork!", json.GetProperty("prompt").GetString());
        Assert.Equal("#dc0000", json.GetProperty("color").GetString());
        Assert.True(json.GetProperty("canFire").GetBoolean());
        Assert.Equal(0.4, json.GetProperty("initialCharge").GetDouble());
    }

    [Fact]
    public void ShakeView_defaults_to_no_color_no_fire_and_empty_charge()
    {
        var v = new ShakeView("Shake!");
        Assert.Null(v.Color);
        Assert.False(v.CanFire);
        Assert.Equal(0, v.InitialCharge);
    }

    [Fact]
    public void ShakeInput_reads_charge_and_fire()
    {
        var input = new PlayerInput(JsonSerializer.SerializeToElement(new { charge = 0.75, fire = true }));
        Assert.Equal(new ShakeInput(0.75, true), input.Read<ShakeInput>());
        var chargeOnly = new PlayerInput(JsonSerializer.SerializeToElement(new { charge = 0.25 }));
        Assert.Equal(new ShakeInput(0.25, false), chargeOnly.Read<ShakeInput>());
    }

    [Fact]
    public void ShakeInput_without_a_numeric_charge_is_rejected()
    {
        Assert.Throws<JsonException>(() => new PlayerInput(JsonSerializer.SerializeToElement(new { fire = true })).Read<ShakeInput>());
        Assert.Throws<JsonException>(() => new PlayerInput(JsonSerializer.SerializeToElement(new { pressed = "go" })).Read<ShakeInput>());
        Assert.Throws<JsonException>(() => new PlayerInput(JsonSerializer.SerializeToElement(new { charge = "lots" })).Read<ShakeInput>());
    }

    [Fact]
    public void PlayerInput_reads_typed_payload()
    {
        var input = new PlayerInput(JsonSerializer.SerializeToElement(new { pressed = "go" }));
        Assert.Equal("go", input.Read<ButtonsInput>().Pressed);
    }
}
