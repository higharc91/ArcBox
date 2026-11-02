using System.Collections.Immutable;
using Tactics.Game;
using Tactics.Logic;
using Tactics.Presentation.Audio;
using Tactics.Presentation.Board;
using Tactics.Presentation.Hud;
using Tactics.Presentation.Tests.Board;

namespace Tactics.Presentation.Tests.Story;

/// <summary>Story views as the server writes them, and the real pack's lore, clips and art, for the story's tests.</summary>
internal static class StoryFeed
{
    public static readonly DateTimeOffset T0 = new(2026, 11, 1, 20, 0, 0, TimeSpan.Zero);

    private static readonly Lazy<LoreBook> LoreOnce = new(() => LoreBook.Load(Pack.Root));
    private static readonly Lazy<VoiceClips> ClipsOnce = new(() => VoiceClips.Load(Pack.Root));
    private static readonly Lazy<UnitArt> UnitsOnce = new(() => UnitArt.Load(Pack.Root));
    private static readonly Lazy<HudArt> ArtOnce = new(() => HudArt.Load(Pack.Root, UnitsOnce.Value));

    public static LoreBook Lore => LoreOnce.Value;

    public static VoiceClips Clips => ClipsOnce.Value;

    public static UnitArt Units => UnitsOnce.Value;

    public static HudArt Art => ArtOnce.Value;

    /// <summary>Every line of every scene in the pack, with its scene.</summary>
    public static IEnumerable<(SceneDef Scene, SceneLine Line)> AllLines() => Lore.Scenes.SelectMany(s => s.Lines.Select(l => (s, l)));

    /// <summary>A line as the server words it for the TV (<c>TacticsGame.LineView</c>).</summary>
    public static StoryLineView LineOf(SceneLine line)
    {
        var who = Lore.Character(line.Speaker);
        return new StoryLineView(line.Id, line.Speaker, who?.Name, who?.Preset, line.Mood, line.Text, who?.Role);
    }

    /// <summary>A line by its id in the real lore.</summary>
    public static StoryLineView Line(string id) => LineOf(AllLines().Single(l => l.Line.Id == id).Line);

    /// <summary>A line of the narrator, or of someone, with a made-up id that has no clip.</summary>
    public static StoryLineView Unvoiced(string id = "no_such_line", string text = "The wind drops.", string speaker = "narrator")
        => speaker == "narrator" ? new StoryLineView(id, speaker, null, null, "idle", text) : new StoryLineView(id, speaker, "Someone", "mayor", "idle", text, "npc");

    public static StoryView Showing(int seq, StoryLineView? line, string state = TacticsGame.StoryStates.Lines, StoryAskView? ask = null,
        StoryCheckView? check = null, string scene = "ch1_mayor", string title = "The Mayor's Request", DateTimeOffset? until = null, string? campaign = null)
        => new(seq, scene, title, state, line, ask, check, until ?? T0.AddSeconds(5), campaign);

    public static StoryOptionView Option(string id, string label, params string[] voters) => new(id, label, null, null, [.. voters]);

    public static StoryOptionView Check(string id, string label, string skill, int dc, params string[] voters) => new(id, label, skill, dc, [.. voters]);

    public static StoryAskView Ask(bool party, int voted, int of, ImmutableArray<StoryOptionView> options, ImmutableArray<string>? away = null, bool tie = false,
        string? decider = null, DateTimeOffset? deadline = null, double? span = null)
        => new(party, voted, of, away ?? [], tie, decider, deadline, options, span);

    public static StoryCardView Card(string member, string name, string player, int bonus, string note = "CHA 12", bool here = true, int votes = 0)
        => new(member, name, player, $"characters.class_fighter", bonus, note, here, votes);
}
