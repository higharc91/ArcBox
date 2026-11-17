using System.Text.RegularExpressions;

namespace Meridian.Presentation.Tests;

/// <summary>
/// The scene decides nothing (M4 plan, Global constraints): the Godot code under src/ArcBox.Host/games/meridian may read
/// host views, Presentation models and the content pack, but never the rules. A source scan, because the Godot project
/// can't be loaded by a test.
/// </summary>
public sealed partial class BoundaryTests
{
    /// <summary>Rules-side names the scene must never touch.</summary>
    [GeneratedRegex(@"\b(Rules\.(Step|Legal)|Visibility|BotRunner|ExploreBot|PolicyBot|ScenarioRegistry|IScenario|GameState|Resolver|Command)\b")]
    private static partial Regex Forbidden();

    public static string SceneDir => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(BasePack.Root)!)!, "..",
                                                                   "src", "ArcBox.Host", "games", "meridian"));

    [Fact]
    public void The_scene_reads_no_rules()
    {
        var files = Directory.GetFiles(SceneDir, "*.cs", SearchOption.AllDirectories);
        Assert.NotEmpty(files);
        var hits = files.SelectMany(f => File.ReadLines(f).Select((line, i) => (f, i, line)))
            .Where(x => !x.line.TrimStart().StartsWith("//") && !x.line.TrimStart().StartsWith("///") && Forbidden().IsMatch(x.line))
            .Select(x => $"{Path.GetFileName(x.f)}:{x.i + 1}: {x.line.Trim()}")
            .ToList();
        Assert.True(hits.Count == 0, string.Join("\n", hits));
    }

    [Fact]
    public void The_scan_catches_what_it_should()
    {
        Assert.Matches(Forbidden(), "var next = Rules.Step(pack, state, command);");
        Assert.Matches(Forbidden(), "var seen = Visibility.For(state, null);");
        Assert.DoesNotMatch(Forbidden(), "var view = json.Deserialize<MeridianHostView>(ArcJson.Options);");
        Assert.DoesNotMatch(Forbidden(), "var pack = PackLoader.Load(files, root);");
    }
}
