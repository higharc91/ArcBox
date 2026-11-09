using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Tactics.Game;
using Tactics.Logic;
using Tactics.Presentation.Board;
using Tactics.Presentation.Hud;

namespace Tactics.Presentation.Story;

/// <summary>A stretch of a caption's row, lit (<paramref name="Strong"/>) or not: the question a line asks is lit, as in mockup 02.</summary>
public sealed record CaptionRun(string Text, bool Strong);

/// <summary>The line showing, at the bottom left (mockup 02).</summary>
/// <param name="Line">The line's id.</param>
/// <param name="Speaker">The speaker's name, or null for the narrator.</param>
/// <param name="Portrait">Their face in the line's mood, or null: the narrator has none.</param>
/// <param name="Rows">The words, wrapped to <see cref="StoryScreen.CaptionColumns"/>: a speaker's in quotes, the narrator's bare.</param>
public sealed record StoryCaption(string Line, string? Speaker, HudPortrait? Portrait, ImmutableArray<ImmutableArray<CaptionRun>> Rows)
{
    /// <summary>The narrator's words: no name, no face, no quotes.</summary>
    public bool Narrator => Speaker is null;

    /// <summary>By its words, row by row: the same line drawn again is equal, so the TV doesn't rebuild it (<see cref="Contents"/>).</summary>
    public bool Equals(StoryCaption? other)
        => other is not null && Line == other.Line && Speaker == other.Speaker && Equals(Portrait, other.Portrait)
            && Contents.Same(Rows, other.Rows, Contents.Rows<CaptionRun>.Instance);

    public override int GetHashCode() => HashCode.Combine(Line, Speaker, Portrait, Contents.Hash(Rows, Contents.Rows<CaptionRun>.Instance));
}

/// <summary>A voter's pip on the vote panel: their initial, in <paramref name="Ink"/>, on their colour.</summary>
/// <param name="Name">The voter's player's name.</param>
/// <param name="Initial">Its first letter, in capitals; "?" for an empty name.</param>
/// <param name="Fill">Their colour (<see cref="StoryAskView.Colors"/>), or the panel's gold with none (<see cref="PlayerInk.PipFallback"/>).</param>
/// <param name="Ink">Black or white, whichever reads on <paramref name="Fill"/> (<see cref="PlayerInk.InkOn"/>).</param>
public sealed record VoterPip(string Name, string Initial, Rgb Fill, Rgb Ink)
{
    /// <summary>The pip of <paramref name="name"/> in <paramref name="hex"/>, their colour as the server sends it.</summary>
    public static VoterPip Of(string name, string? hex)
    {
        var fill = PlayerInk.Of(hex) ?? PlayerInk.PipFallback;
        return new VoterPip(name, name.Length > 0 ? name[..1].ToUpperInvariant() : "?", fill, PlayerInk.InkOn(fill));
    }
}

/// <summary>An option on the vote panel.</summary>
/// <param name="Id">The option's id.</param>
/// <param name="Letter">"A", "B" and so on, in the node's order.</param>
/// <param name="Label">What it says, in quotes, wrapped to <see cref="StoryScreen.LabelColumns"/>.</param>
/// <param name="Detail">"Persuasion check · DC 15" for a check, else null.</param>
/// <param name="Voters">Who voted for it, as pips: the TV's alone (5d flag 7).</param>
public sealed record VoteRow(string Id, string Letter, ImmutableArray<string> Label, string? Detail, ImmutableArray<VoterPip> Voters)
{
    /// <summary>By its words and voters (<see cref="Contents"/>).</summary>
    public bool Equals(VoteRow? other)
        => other is not null && Id == other.Id && Letter == other.Letter && Detail == other.Detail && Contents.Same(Label, other.Label)
            && Contents.Same(Voters, other.Voters);

    public override int GetHashCode() => HashCode.Combine(Id, Letter, Detail, Contents.Hash(Label), Contents.Hash(Voters));
}

/// <summary>The question open, at the bottom right (mockup 02).</summary>
/// <param name="Heading">"Party vote", "Leader's choice" or "Tie-break".</param>
/// <param name="Party">A vote of the party's, rather than the leader's choice.</param>
/// <param name="Tie">A tie is being broken: <paramref name="Rows"/> are only the tied.</param>
/// <param name="Clock">The countdown, or null while the question waits for a phone.</param>
/// <param name="Rows">What's offered.</param>
/// <param name="Tally">The line under them: "4 of 4 voted · 2–2 tie, Sam decides".</param>
/// <param name="Away">"Dave is away and doesn't vote", or null.</param>
/// <param name="Scale">The retained full-size factor; columns keep every option legible (<see cref="VoteLayout"/>).</param>
public sealed record VotePanel(string Heading, bool Party, bool Tie, string? Clock, ImmutableArray<VoteRow> Rows, string Tally, string? Away,
    float Scale = 1)
{
    /// <summary>How tall the panel is drawn, in layout pixels.</summary>
    public float Height => VoteLayout.Height(Rows, Scale);

    /// <summary>By what it shows, row by row (<see cref="Contents"/>): only a new clock, vote or tally tells two frames apart.</summary>
    public bool Equals(VotePanel? other)
        => other is not null && Heading == other.Heading && Party == other.Party && Tie == other.Tie && Clock == other.Clock && Tally == other.Tally
            && Away == other.Away && Scale == other.Scale && Contents.Same(Rows, other.Rows);

    public override int GetHashCode() => HashCode.Combine(Heading, Party, Tie, Clock, Tally, Away, Scale, Contents.Hash(Rows));
}

/// <summary>A character who can roll, as a card (mockup 03).</summary>
/// <param name="Member">Their member id.</param>
/// <param name="Name">The character's name.</param>
/// <param name="Player">Their player's name.</param>
/// <param name="Sprite">Their sprite key, for the face.</param>
/// <param name="Bonus">"+5", "+0", "−1".</param>
/// <param name="Note">Where the bonus comes from: "Proficient · CHA 14".</param>
/// <param name="Tag">The tab over the card: "Rolls for the party", "Dave away", or null.</param>
/// <param name="Chosen">This one rolls.</param>
/// <param name="Here">Their phone is here.</param>
/// <param name="Votes">How many picked them.</param>
/// <param name="Who">The line under the name (<see cref="WhoOf"/>).</param>
/// <param name="Colour">Their player's colour, for the card's edge (<see cref="StoryCardView.Color"/>); null for no edge.</param>
public sealed record CheckCard(string Member, string Name, string Player, string? Sprite, string Bonus, string Note, string? Tag, bool Chosen, bool Here, int Votes,
    string Who = "", Rgb? Colour = null)
{
    /// <summary>How many picked them, in words for under the card: "1 vote", "3 votes", or empty for none.</summary>
    public string VotesText => Votes switch { <= 0 => "", 1 => "1 vote", _ => string.Create(CultureInfo.InvariantCulture, $"{Votes} votes") };

    /// <summary>
    /// The line under the name, by the phone's rule: "Priya · Rogue" when the player isn't the character's namesake, the class alone when
    /// they are, and the player when the server sent no class.
    /// </summary>
    public static string WhoOf(string name, string player, string? cls)
        => cls is null ? player : player != name ? $"{player} · {cls}" : cls;
}

/// <summary>The roll, at the bottom (mockup 03).</summary>
/// <param name="Roller">Who rolled: the character's name.</param>
/// <param name="Face">The die's face showing: the roll's own once it has <paramref name="Landed"/>, others as it tumbles.</param>
/// <param name="Landed">The die has stopped: the sum and verdict show.</param>
/// <param name="Sum">"14 + 5 = 19 vs DC 15".</param>
/// <param name="Verdict">"Success" or "Failure".</param>
/// <param name="Success">Whether it passed.</param>
public sealed record RollReveal(string Roller, int Face, bool Landed, string Sum, string Verdict, bool Success)
{
    /// <summary>The line under the sum: who rolls, as the die tumbles, and who rolled, once it lands.</summary>
    public string Line => Landed ? $"{Roller} rolled for the party" : $"{Roller} rolls\u2026";
}

/// <summary>A skill check, full over the dimmed world (mockup 03).</summary>
/// <param name="Heading">"Skill check".</param>
/// <param name="Skill">"Persuasion".</param>
/// <param name="Dc">"DC 15", or "DC hidden" for an active Search, whose DC isn't shown (interactables design §7.4).</param>
/// <param name="Title">The option it's for, in quotes.</param>
/// <param name="Cards">Who can roll, in member order.</param>
/// <param name="Clock">The countdown on picking who rolls, or null.</param>
/// <param name="Footer">"Vote for who rolls · 2 voted", "2–2 tie, Sam decides", or null once rolled.</param>
/// <param name="Roll">The roll, once made.</param>
public sealed record CheckPanel(string Heading, string Skill, string Dc, string Title, ImmutableArray<CheckCard> Cards, string? Clock, string? Footer, RollReveal? Roll)
{
    /// <summary>By what it shows, card by card (<see cref="Contents"/>).</summary>
    public bool Equals(CheckPanel? other)
        => other is not null && Heading == other.Heading && Skill == other.Skill && Dc == other.Dc && Title == other.Title && Clock == other.Clock
            && Footer == other.Footer && Equals(Roll, other.Roll) && Contents.Same(Cards, other.Cards);

    public override int GetHashCode() => HashCode.Combine(Heading, Skill, Dc, Title, Clock, Footer, Roll, Contents.Hash(Cards));
}

/// <summary>
/// What the TV shows of a scene (overworld 5d §10), worded and laid out from mockups 02 and 03: the caption at the bottom left, the question
/// at the bottom right, a skill check full over the dimmed world, and between two scenes the next one's title. Pure, so Godot only draws it.
/// </summary>
/// <param name="Seq">The view's line number.</param>
/// <param name="Scene">The scene's id.</param>
/// <param name="Title">The scene's title.</param>
/// <param name="State">One of <see cref="TacticsGame.StoryStates"/>.</param>
/// <param name="Caption">The line showing, or null.</param>
/// <param name="Vote">The question open, or null.</param>
/// <param name="Check">The skill check under way, or null.</param>
/// <param name="Interlude">Between scenes, the next one's title; else null.</param>
public sealed record StoryScreen(int Seq, string Scene, string Title, string State, StoryCaption? Caption, VotePanel? Vote, CheckPanel? Check, string? Interlude)
{
    /// <summary>
    /// How many characters a caption's row holds. Mockup 02's box takes about 80 of its font's average characters a row; 64 leaves a fifth for
    /// wide letters, and every line of the lore fits <see cref="CaptionRows"/> of it.
    /// </summary>
    public const int CaptionColumns = 64;

    /// <summary>How many rows a caption has room for (mockup 02: three).</summary>
    public const int CaptionRows = 3;

    /// <summary>How many characters an option's row holds on the vote panel, about half the caption's at a smaller size.</summary>
    public const int LabelColumns = 40;

    /// <summary>How many rows an option's label has room for.</summary>
    public const int LabelRows = 3;

    /// <summary>How long the die tumbles before it lands, in seconds: well inside the 4.5 s the result shows for (<see cref="StoryTiming.Reveal"/>).</summary>
    public const double Tumble = 1.2;

    /// <summary>How often a tumbling die shows a new face, a second.</summary>
    public const double TumbleFaces = 12;

    private const string Minus = "−";
    private const string EnDash = "–";

    /// <summary>The scene dims the world: a skill check is up.</summary>
    public bool Dimmed => Check is not null;

    /// <summary>
    /// The screen for <paramref name="view"/>. <paramref name="art"/> gives the speaker's face (none without it), <paramref name="clock"/> is the
    /// question's countdown (<see cref="VoteClock.Text"/>), and <paramref name="revealed"/> how long the roll has shown: a roll the TV didn't
    /// see made is shown landed.
    /// </summary>
    public static StoryScreen Of(StoryView view, HudArt? art = null, string? clock = null, double revealed = double.PositiveInfinity)
    {
        // A check has the screen to itself (mockup 03): its line is under the cards, so no caption.
        var caption = view.Check is null && view.Line is { } line && view.State is not (TacticsGame.StoryStates.Gap or TacticsGame.StoryStates.Rolled)
            ? CaptionOf(line, art, asking: view.Ask is not null)
            : null;
        return new StoryScreen(view.Seq, view.Scene, view.Title, view.State, caption,
            view.Ask is { } ask ? VoteOf(ask, clock) : null,
            view.Check is { } check ? CheckOf(check, clock, revealed) : null,
            view.State == TacticsGame.StoryStates.Gap && view.Title.Length > 0 ? view.Title : null);
    }

    // ---- The caption ---------------------------------------------------------------------------------------------------------

    private static StoryCaption CaptionOf(StoryLineView line, HudArt? art, bool asking)
    {
        var narrator = line.Speaker == SceneDef.Narrator;
        var name = narrator ? null : line.Name ?? Presentation.Words.Title(line.Speaker);   // someone the lore doesn't list still speaks
        var text = Typeset(line.Text);
        var strong = asking ? QuestionStart(text) : -1;
        var words = Words(text, strong).ToList();
        if (!narrator && words.Count > 0)
        {
            words[0] = words[0] with { Text = $"“{words[0].Text}" };
            words[^1] = words[^1] with { Text = $"{words[^1].Text}”" };
        }
        return new StoryCaption(line.Id, name, narrator ? null : art?.Speaker(line.Portrait, line.Mood, line.Role), Wrap(words, CaptionColumns));
    }

    /// <summary>
    /// Where the question a line ends on starts: its last sentence, when that ends with a question mark; -1 when it doesn't. A sentence starts
    /// after a full stop, a question or an exclamation mark and a space.
    /// </summary>
    private static int QuestionStart(string text)
    {
        var end = text.TrimEnd();
        if (!end.EndsWith('?'))
            return -1;
        for (var i = end.Length - 2; i > 0; i--)
            if (end[i] == ' ' && end[i - 1] is '.' or '?' or '!')
                return i + 1;
        return 0;
    }

    private static IEnumerable<CaptionRun> Words(string text, int strongFrom)
    {
        var at = 0;
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            at = text.IndexOf(word, at, StringComparison.Ordinal);
            yield return new CaptionRun(word, strongFrom >= 0 && at >= strongFrom);
            at += word.Length;
        }
    }

    /// <summary>
    /// Wraps words to rows of at most <paramref name="columns"/> characters, on spaces only, and joins each row's words into runs of the same
    /// light. A word longer than a row has a row of its own.
    /// </summary>
    public static ImmutableArray<ImmutableArray<CaptionRun>> Wrap(IEnumerable<CaptionRun> words, int columns)
    {
        var rows = ImmutableArray.CreateBuilder<ImmutableArray<CaptionRun>>();
        var row = new List<CaptionRun>();
        var length = 0;
        foreach (var word in words)
        {
            if (row.Count > 0 && length + 1 + word.Text.Length > columns)
            {
                rows.Add(Runs(row));
                (row, length) = ([], 0);
            }
            length += (row.Count > 0 ? 1 : 0) + word.Text.Length;
            row.Add(word);
        }
        if (row.Count > 0)
            rows.Add(Runs(row));
        return rows.ToImmutable();
    }

    /// <summary>A row's words as runs: a space joins two words, and goes with the second's run, so a lit stretch starts on its word.</summary>
    private static ImmutableArray<CaptionRun> Runs(List<CaptionRun> words)
    {
        var runs = ImmutableArray.CreateBuilder<CaptionRun>();
        var text = new StringBuilder();
        bool? strong = null;
        for (var i = 0; i < words.Count; i++)
        {
            if (strong is { } lit && lit != words[i].Strong)
            {
                runs.Add(new CaptionRun(text.ToString(), lit));
                text.Clear();
            }
            strong = words[i].Strong;
            text.Append(i > 0 ? " " : "").Append(words[i].Text);
        }
        if (strong is { } last)
            runs.Add(new CaptionRun(text.ToString(), last));
        return runs.ToImmutable();
    }

    /// <summary>The words an apostrophe starts by standing for what's left out: 'tis, 'twas, 'em. Its mark is ’, as in "don’t".</summary>
    private static readonly string[] Elisions = ["tis", "twas", "twere", "twill", "em", "til", "cause", "neath", "n"];

    /// <summary>
    /// Straight quotes as the TV's font sets them: ‘ opening a quote (at the start, or after a space, a bracket, a dash or another quote),
    /// and ’ everywhere else: in a word, after one, or starting a word that leaves letters out ('tis, 'em, '90s).
    /// </summary>
    public static string Typeset(string text)
    {
        var set = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '\'')
            {
                set.Append(text[i]);
                continue;
            }
            var opens = i == 0 || text[i - 1] is ' ' or '(' or '[' or '"' or '“' or '—' or '–' or '-';
            set.Append(opens && !Elided(text, i + 1) ? '‘' : '’');
        }
        return set.ToString();
    }

    /// <summary>Whether the word at <paramref name="at"/> is one an apostrophe elides into: a known one, or a number ('90s).</summary>
    private static bool Elided(string text, int at)
    {
        if (at < text.Length && char.IsDigit(text[at]))
            return true;
        var end = at;
        while (end < text.Length && char.IsLetter(text[end]))
            end++;
        var word = text[at..end];
        return Elisions.Any(e => word.Equals(e, StringComparison.OrdinalIgnoreCase));
    }

    // ---- The vote ------------------------------------------------------------------------------------------------------------

    /// <summary>An option's label as the panel shows it: in quotes, as someone in the party says it, wrapped to <see cref="LabelColumns"/>.</summary>
    public static ImmutableArray<string> Label(string label, int columns = LabelColumns)
        => [.. Wrap(Typeset($"“{label}”").Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(w => new CaptionRun(w, false)), columns)
            .Select(row => string.Concat(row.Select(r => r.Text)))];

    private static VotePanel VoteOf(StoryAskView ask, string? clock)
    {
        var heading = ask.Tie ? "Tie-break" : ask.Party ? "Party vote" : "Leader's choice";
        ImmutableArray<VoteRow> rows = [];
        var scale = 1f;
        foreach (var size in VoteLayout.Scales)
        {
            (rows, scale) = ([.. ask.Options.Select((o, i) => new VoteRow(o.Id, Letter(i), Label(o.Label, VoteLayout.Columns(size)),
                o.Skill is { } skill && o.Dc is { } dc ? $"{SkillName(skill)} check · DC {dc.ToString(CultureInfo.InvariantCulture)}" : null,
                o.Voters.IsDefault ? [] : [.. o.Voters.Select(v => VoterPip.Of(v, ask.Colors?.GetValueOrDefault(v)))]))], size);
            if (VoteLayout.Height(rows, size) <= VoteLayout.MaxHeight)
                break;
        }
        return new VotePanel(heading, ask.Party, ask.Tie, ask.Deadline is null ? null : clock, rows, Tally(ask), ask.Party ? Away(ask.Away) : null, scale);
    }

    private static string Tally(StoryAskView ask)
    {
        if (ask.Deadline is null)
            return "Waiting for a phone to come back";
        if (!ask.Party && !ask.Tie)
            return ask.Decider is { } leader ? $"{leader} chooses for the party" : "The leader chooses for the party";
        var voted = string.Create(CultureInfo.InvariantCulture, $"{ask.Voted} of {ask.Of} voted");
        return ask.Tie ? $"{voted} · {Tie(ask.Options.Select(o => o.Voters.IsDefault ? 0 : o.Voters.Length), ask.Decider)}" : voted;
    }

    /// <summary>"2–2 tie, Sam decides": the tied counts, highest first, and who breaks it.</summary>
    private static string Tie(IEnumerable<int> counts, string? decider)
    {
        var tied = string.Join(EnDash, counts.OrderByDescending(c => c).Select(c => c.ToString(CultureInfo.InvariantCulture)));
        return decider is null ? $"{tied} tie" : $"{tied} tie, {decider} decides";
    }

    private static string? Away(ImmutableArray<string> away) => away.IsDefaultOrEmpty
        ? null
        : away.Length == 1 ? $"{away[0]} is away and doesn’t vote" : $"{List(away)} are away and don’t vote";

    private static string List(ImmutableArray<string> names) => $"{string.Join(", ", names[..^1])} and {names[^1]}";

    /// <summary>"A" for the first option, up to "Z".</summary>
    private static string Letter(int index) => ((char)('A' + Math.Min(index, 25))).ToString();

    /// <summary>A skill's id as a name: <c>sleight_of_hand</c> → "Sleight of Hand".</summary>
    public static string SkillName(string skill)
        => string.Join(" ", skill.Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Select((w, i) => i > 0 && w is "of" or "and" or "the" ? w : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(w)));

    // ---- The check -----------------------------------------------------------------------------------------------------------

    private static CheckPanel CheckOf(StoryCheckView check, string? clock, double revealed)
    {
        var cards = check.Cards.IsDefault ? [] : check.Cards;
        var chosen = cards.FirstOrDefault(c => c.Member == check.Chosen);
        var roll = !check.GroupCheck && check.Roll is { } result ? Reveal(result, chosen?.Name ?? check.Chosen ?? "", revealed, hidden: check.Dc is null) : null;
        string? footer = null;
        if (roll is null)
        {
            var votes = cards.Sum(c => c.Votes);
            footer = check.Tie
                ? Tie(cards.Where(c => c.Votes > 0 && c.Votes == cards.Max(m => m.Votes)).Select(c => c.Votes), check.Decider)
                : check.Deadline is null ? "Waiting for a phone to come back"
                : votes > 0 ? string.Create(CultureInfo.InvariantCulture, $"Vote for who rolls · {votes} voted") : "Vote for who rolls";
        }
        if (check.GroupCheck)
        {
            footer = check.Group.IsDefaultOrEmpty ? "Everyone present rolls. Half or more must succeed."
                : $"{check.Group.Count(r => r.Passed)} of {check.Group.Length} succeeded — {(check.Roll?.Success == true ? "Success" : "Failure")}";
        }
        if (check.Helper is { } helper) footer = $"{helper} helps · advantage" + (footer is null ? "" : $" · {footer}");
        return new CheckPanel(check.GroupCheck ? "Group check" : "Skill check", SkillName(check.Skill), check.Dc is { } dc ? string.Create(CultureInfo.InvariantCulture, $"DC {dc}") : HiddenDc,
            Typeset($"“{check.Title}”"),
            [.. cards.Select(c => new CheckCard(c.Member, c.Name, c.Player, c.Sprite, Signed(c.Bonus), !check.Group.IsDefaultOrEmpty && check.Group.FirstOrDefault(r => r.Member == c.Member) is { } g ? $"Rolled {g.Die} + {g.Bonus} = {g.Die + g.Bonus} — {(g.Passed ? "Success" : "Failure")}" : c.Note,
                c.Member == check.Chosen ? "Rolls for the party" : c.Here ? null : $"{c.Player} away", c.Member == check.Chosen, c.Here, c.Votes,
                CheckCard.WhoOf(c.Name, c.Player, c.Class), PlayerInk.Of(c.Color)))],
            roll is null && check.Deadline is not null ? clock : null, footer, roll);
    }

    /// <summary>
    /// The roll <paramref name="seconds"/> after it showed: the die tumbles through other faces for <see cref="Tumble"/>, then lands on its own,
    /// with the sum and the verdict.
    /// </summary>
    /// <remarks>
    /// With <paramref name="hidden"/> (an active Search, whose DC isn't shown) the sum has no "vs DC", and the verdict says what the search did:
    /// "Found something" or "Nothing found".
    /// </remarks>
    public static RollReveal Reveal(RollResult roll, string roller, double seconds, bool hidden = false)
    {
        var landed = seconds >= Tumble;
        var face = landed ? roll.Kept : (roll.Kept + 7 * (1 + (int)(Math.Max(0, seconds) * TumbleFaces))) % 20 + 1;
        var modifier = roll.Modifier < 0 ? $"{Minus} {-roll.Modifier}" : $"+ {roll.Modifier}";
        var sum = hidden
            ? string.Create(CultureInfo.InvariantCulture, $"{roll.Kept} {modifier} = {roll.Total}")
            : string.Create(CultureInfo.InvariantCulture, $"{roll.Kept} {modifier} = {roll.Total} vs DC {roll.Vs}");
        var verdict = hidden ? roll.Success ? "Found something" : "Nothing found" : roll.Success ? "Success" : "Failure";
        return new RollReveal(roller, face, landed, sum, verdict, roll.Success);
    }

    /// <summary>What a check whose DC isn't shown says in its place.</summary>
    public const string HiddenDc = "DC hidden";

    /// <summary>A bonus with its sign: "+5", "+0", "−1".</summary>
    private static string Signed(int bonus) => bonus < 0
        ? $"{Minus}{(-bonus).ToString(CultureInfo.InvariantCulture)}"
        : $"+{bonus.ToString(CultureInfo.InvariantCulture)}";
}
