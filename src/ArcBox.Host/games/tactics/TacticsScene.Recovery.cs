using System.Text.Json;
using ArcBox.Host.Shell;
using Godot;
using Tactics.Game;

namespace ArcBox.Host.Games.Tactics;

public partial class TacticsScene
{
    private readonly Dictionary<string, Texture2D> _injuryTextures = new(StringComparer.Ordinal);

    private Texture2D? InjuryTexture(string id)
    {
        if (_injuryTextures.TryGetValue(id, out var found)) return found;
        if (!_content.TryGetValue(BasePack, out var root)) return null;
        var manifest = Path.Combine(root, "injury_icons", "manifest.json");
        if (!File.Exists(manifest)) return null;
        using var doc = JsonDocument.Parse(File.ReadAllBytes(manifest));
        if (!doc.RootElement.GetProperty("icons").TryGetProperty(id, out var icon)) return null;
        var path = Path.Combine(root, "injury_icons", icon.GetProperty("file").GetString()!);
        if (!File.Exists(path)) return null;
        return _injuryTextures[id] = ImageTexture.CreateFromImage(Image.LoadFromFile(path));
    }

    private Texture2D? _mealTexture;
    private Texture2D? MealTexture()
    {
        if (_mealTexture is not null) return _mealTexture;
        if (!_content.TryGetValue(BasePack, out var root)) return null;
        var path = Path.Combine(root, "camp", "bowl.png");
        return File.Exists(path) ? _mealTexture = ImageTexture.CreateFromImage(Image.LoadFromFile(path)) : null;
    }

    private static Control RecoveryMeter(string label, int current, int max, Color color, bool compact = false)
    {
        var row = ArcTheme.VBox(3);
        row.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        row.AddChild(TvText.Label($"{label}  {current}/{max}", ArcTheme.SemiBold, 24, new Color(.75f, .78f, .8f)));
        var bar = new ProgressBar { MinValue = 0, MaxValue = Math.Max(1, max), Value = current, ShowPercentage = false, CustomMinimumSize = new Vector2(compact ? 20 : 100, 6), MouseFilter = MouseFilterEnum.Ignore };
        bar.AddThemeStyleboxOverride("background", new StyleBoxFlat { BgColor = new Color(0, 0, 0, .5f) });
        bar.AddThemeStyleboxOverride("fill", new StyleBoxFlat { BgColor = color });
        row.AddChild(bar);
        return row;
    }

    private Control RecoveryCard(RecoveryMemberView member, string? note = null, bool compact = false)
    {
        var body = ArcTheme.VBox(5);
        var name = TvText.Label(member.Name, ArcTheme.Bold, compact ? 24 : 28);
        name.ClipText = true;
        name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var heading = ArcTheme.HBox(6, name);
        foreach (var injury in member.Injuries)
        {
            if (InjuryTexture(injury.Id) is { } texture)
                heading.AddChild(new TextureRect { Texture = texture, CustomMinimumSize = new Vector2(compact ? 24 : 32, compact ? 24 : 32), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, TextureFilter = TextureFilterEnum.Nearest, TooltipText = injury.Detail });
        }
        if (member.WellFed)
        {
            if (MealTexture() is { } meal) heading.AddChild(new TextureRect { Texture = meal, CustomMinimumSize = new Vector2(24, 24),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                TextureFilter = TextureFilterEnum.Nearest, TooltipText = "Well fed · 3 temporary HP next battle" });
            else heading.AddChild(TvText.Label("♨", ArcTheme.Bold, 24, ArcTheme.Gold));
        }
        if (member.Room) note = "Room booked";
        if (!member.Here && note is null) note = "Away";
        if (note is not null) heading.AddChild(TvText.Label(note, ArcTheme.SemiBold, 24, ArcTheme.Gold));
        body.AddChild(heading);
        var meters = ArcTheme.HBox(12, RecoveryMeter("HP", member.Hp, member.MaxHp, new Color(.4f, .73f, .5f), compact));
        if (compact) body.AddChild(meters);
        BoxContainer resources = compact ? ArcTheme.VBox(5) : meters;
        foreach (var resource in member.Resources)
        {
            var label = System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(resource.Name.Replace('_', ' '));
            resources.AddChild(RecoveryMeter(label, resource.Current, resource.Max, new Color(.4f, .57f, .8f), compact));
        }
        body.AddChild(resources);
        if (member.WellFed)
        {
            var fed = TvText.Label("Well fed · 3 temporary HP next battle", ArcTheme.SemiBold, 24, ArcTheme.Gold);
            fed.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            body.AddChild(fed);
        }
        return ArcTheme.Panel(ArcTheme.GlassBox(null, 12, 8), body);
    }
}
