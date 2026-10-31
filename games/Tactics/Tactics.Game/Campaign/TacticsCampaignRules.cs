using System.Text.Json;
using ArcBox.Sdk;
using ArcBox.Sdk.Campaigns;
using ArcBox.Sdk.Protocol;
using Tactics.Logic;

namespace Tactics.Game;

/// <summary>
/// The tactics campaign's rules outside a session (adapter spec §6): new documents, seat cards and a read-only portal view
/// of the party. Sub-project 4 adds the portal's commands (character creation and level-up).
/// </summary>
public sealed class TacticsCampaignRules(Lazy<RulesPack> pack) : ICampaignRules
{
    /// <summary>Rules on a loaded pack.</summary>
    public TacticsCampaignRules(RulesPack rules) : this(new Lazy<RulesPack>(rules))
    {
    }

    /// <inheritdoc />
    public string GameId => TacticsGame.Id;

    /// <inheritdoc />
    public JsonElement NewDocument(CampaignInfo info) => TacticsDocument.New.ToJson();

    /// <inheritdoc />
    public SeatCard CardFor(CampaignInfo info, JsonElement document, ProfileId member)
    {
        var name = info.Members.FirstOrDefault(m => m.Profile == member)?.DisplayName ?? "Adventurer";
        var doc = TacticsDocument.Read(document);
        return doc.For(member) is { } record
            ? new SeatCard(name, $"{Describe(record.Character)} · Lv {doc.PartyLevel}")
            : new SeatCard(name, "No character yet");
    }

    /// <inheritdoc />
    /// <remarks>
    /// Everyone sees the party. The member also sees their own character (scores, picks and what's waiting: a subclass to
    /// choose, increases to spend), the "choose" features their picks come from, and, while they have no character or it
    /// hasn't earned XP yet, the options to create one.
    /// </remarks>
    public JsonElement PortalView(CampaignInfo info, JsonElement document, ProfileId member)
    {
        var rules = pack.Value;
        var doc = TacticsDocument.Read(document);
        var party = info.Members.Select(m => doc.For(m.Profile) is { } r
            ? new { name = m.DisplayName, character = Describe(r.Character), xp = r.Xp, you = m.Profile == member }
            : new { name = m.DisplayName, character = "No character yet", xp = 0, you = m.Profile == member });
        var mine = doc.For(member);
        object? options = mine is null || mine.Xp == 0
            ? new
            {
                races = rules.Races.Values.Select(r => new { id = r.Id, name = r.Name }),
                classes = rules.Classes.Values.Select(c => new { id = c.Id, name = c.Name, role = c.Role }),
                backgrounds = rules.Backgrounds.Values.Select(b => new { id = b.Id, name = b.Name, boosts = b.Boosts }),
                pointBuy = CharacterRules.PointBuy,
            }
            : null;
        return JsonSerializer.SerializeToElement(new
        {
            level = doc.PartyLevel, completed = doc.Completed, party,
            me = mine is null ? null : Mine(mine, rules),
            options,
        }, ArcJson.Options);
    }

    private object Mine(MemberRecord record, RulesPack rules)
    {
        var c = record.Character;
        rules.Classes.TryGetValue(c.Class, out var cls);
        var subclass = cls?.Subclasses.FirstOrDefault(s => s.Id == c.Subclass);
        var boosts = rules.Backgrounds.GetValueOrDefault(c.Background)?.Boosts;
        var scores = Enum.GetValues<Stat>().ToDictionary(s => s,
            s => Math.Min(20, c.BaseScores.GetValueOrDefault(s, 10) + (boosts?.GetValueOrDefault(s) ?? 0) + c.Increases.GetValueOrDefault(s)));
        var picks = c.Picks.IsDefaultOrEmpty && cls is not null ? SheetBuilder.DefaultLoadout(cls, c.Level) : c.Picks;
        return new
        {
            name = c.Name, describe = Describe(c), race = c.Race, @class = c.Class, subclass = c.Subclass, background = c.Background,
            level = c.Level, xp = record.Xp, scores, picks,
            injuries = c.Injuries.Select(i => rules.Injuries.GetValueOrDefault(i)?.Name ?? i.Value),
            increasesLeft = CharacterRules.IncreasesLeft(c),
            subclasses = CharacterRules.NeedsSubclass(c) && cls is not null ? cls.Subclasses.Select(s => new { id = s.Id, name = s.Name }) : null,
            choices = cls is null ? [] : SheetBuilder.PickChoices(cls, subclass, c.Level).Select(ch => new
            {
                level = ch.Level,
                choose = ch.Feature.Choose,
                from = ch.Feature.From.Select(id => Option(id, c.Level, rules)),
            }).ToArray(),
        };
    }

    private static object Option(string id, int level, RulesPack rules)
        => rules.Abilities.TryGetValue(new AbilityId(id), out var a)
            ? new { id, name = a.Name, tooltip = Tooltips.For(a, level, rules) }
            : rules.Passives.TryGetValue(id, out var p)
                ? new { id, name = Humanize(id.Split('.')[^1]), tooltip = Tooltips.For(p, level, rules) }
                : new { id, name = id, tooltip = "" };

    /// <inheritdoc />
    /// <remarks>
    /// <c>createCharacter</c> {race, class, background, scores}, while the member has no character or it has no XP yet;
    /// <c>setPicks</c> {picks}; <c>chooseSubclass</c> {subclass}; <c>spendIncrease</c> {stats: one or two}.
    /// </remarks>
    public CommandResult Handle(CampaignInfo info, JsonElement document, ProfileId member, string type, JsonElement data)
    {
        var rules = pack.Value;
        var doc = TacticsDocument.Read(document);
        var mine = doc.For(member);
        try
        {
            (CharacterRecord? Character, string? Problem) result;
            switch (type)
            {
                case "createCharacter":
                    if (mine is { Xp: > 0 })
                        return new(null, "Your character has fought already; it can't be remade.");
                    var name = info.Members.FirstOrDefault(m => m.Profile == member)?.DisplayName ?? "Adventurer";
                    var scores = new Dictionary<Stat, int>();
                    if (data.TryGetProperty("scores", out var s) && s.ValueKind == JsonValueKind.Object)
                        foreach (var p in s.EnumerateObject())
                            if (ParseStat(p.Name) is { } stat && p.Value.TryGetInt32(out var v))
                                scores[stat] = v;
                    result = CharacterRules.Create(name, Str(data, "race"), Str(data, "class"), Str(data, "background"), scores,
                        doc.PartyLevel, rules);
                    break;
                case "setPicks" when mine is not null:
                    var picks = data.TryGetProperty("picks", out var list) && list.ValueKind == JsonValueKind.Array
                        ? list.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => new AbilityId(e.GetString()!)).ToList()
                        : [];
                    result = CharacterRules.SetPicks(mine.Character, picks, rules);
                    break;
                case "chooseSubclass" when mine is not null:
                    result = CharacterRules.ChooseSubclass(mine.Character, Str(data, "subclass"), rules);
                    break;
                case "spendIncrease" when mine is not null:
                    var stats = data.TryGetProperty("stats", out var st) && st.ValueKind == JsonValueKind.Array
                        ? st.EnumerateArray().Select(e => e.ValueKind == JsonValueKind.String ? ParseStat(e.GetString()!) : null).ToList()
                        : [];
                    if (stats.Any(x => x is null))
                        return new(null, "Pick abilities to raise.");
                    result = CharacterRules.SpendIncrease(mine.Character, [.. stats.Select(x => x!.Value)], rules);
                    break;
                default:
                    return new(null, mine is null ? "Create your character first." : "That isn't something the portal can do.");
            }
            if (result.Character is not { } character)
                return new(null, result.Problem);
            var record = mine is null ? new MemberRecord(character, 0) : mine with { Character = character };
            var updated = doc with { Members = doc.Members.SetItem(member.Value, record) };
            return new(updated.ToJson(), null);
        }
        catch (InvalidOperationException)
        {
            return new(null, "That didn't make sense to the game.");
        }
    }

    private static string Str(JsonElement data, string name)
        => data.ValueKind == JsonValueKind.Object && data.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";

    /// <summary>"sneak_attack" → "Sneak attack".</summary>
    private static string Humanize(string id) => id.Length == 0 ? id : char.ToUpperInvariant(id[0]) + id[1..].Replace('_', ' ');

    private static Stat? ParseStat(string s) => Enum.TryParse<Stat>(s, ignoreCase: true, out var stat) ? stat : null;

    /// <summary>"Human Fighter", from the pack's race and class names.</summary>
    public string Describe(CharacterRecord character)
        => $"{pack.Value.Races.GetValueOrDefault(character.Race)?.Name ?? character.Race} {pack.Value.Classes.GetValueOrDefault(character.Class)?.Name ?? character.Class}";
}
