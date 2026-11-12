using System.Collections.Immutable;
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
    /// can still be remade (freely before its first battle, then once more), the options to create one.
    /// </remarks>
    public JsonElement PortalView(CampaignInfo info, JsonElement document, ProfileId member)
    {
        try
        {
            return View(info, document, member);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            // Never fail the portal (the platform builds this view for every command reply too): show the party's names.
            return JsonSerializer.SerializeToElement(new
            {
                level = 1, completed = Array.Empty<string>(),
                party = info.Members.Select(m => new { name = m.DisplayName, character = "", xp = 0, you = m.Profile == member }),
                me = (object?)null, options = (object?)null, problem = "This campaign's saved data couldn't be read.",
            }, ArcJson.Options);
        }
    }

    private JsonElement View(CampaignInfo info, JsonElement document, ProfileId member)
    {
        var rules = pack.Value;
        var doc = Coloured(TacticsDocument.Read(document), info);
        var party = info.Members.Select(m => doc.For(m.Profile) is { } r
            ? new { name = m.DisplayName, character = Describe(r.Character), xp = r.Xp, you = m.Profile == member, color = r.Color }
            : new { name = m.DisplayName, character = "No character yet", xp = 0, you = m.Profile == member, color = (string?)null });
        var mine = doc.For(member);
        var creating = mine is null || mine.CanRemake;
        object? options = creating
            ? new
            {
                races = rules.Races.Values.Select(r => new { id = r.Id, name = r.Name }),
                classes = rules.Classes.Values.Select(c => new { id = c.Id, name = c.Name, role = c.Role }),
                backgrounds = rules.Backgrounds.Values.Select(b => new { id = b.Id, name = b.Name, boosts = b.Boosts }),
                pointBuy = CharacterRules.PointBuy,
                colors = Colors(mine?.Color ?? NewMemberColor(doc, info, member), info.Members
                    .Where(m => m.Profile != member && doc.For(m.Profile)?.Color is not null)
                    .Select(m => new { hex = doc.For(m.Profile)!.Color!, name = m.DisplayName })),
            }
            : null;
        // What the details sheets explain: everything on offer while creating, else the member's own race, class and background.
        var own = mine?.Character;
        var details = CharacterGuide.For(
            creating ? rules.Races.Values : rules.Races.Values.Where(r => r.Id == own!.Race),
            creating ? rules.Classes.Values : rules.Classes.Values.Where(c => c.Id == own!.Class),
            creating ? rules.Backgrounds.Values : rules.Backgrounds.Values.Where(b => b.Id == own!.Background),
            own?.Level ?? doc.PartyLevel, rules, own?.Class);
        var view = JsonSerializer.SerializeToNode(new
        {
            level = doc.PartyLevel, completed = doc.Completed, party,
            me = mine is null ? null : Mine(mine, rules),
            options,
            details,
        }, ArcJson.Options)!.AsObject();
        // The party's key ring (USER DECISION 8, decided 2026-11-03), beside the party: only once it holds a key, so a portal with none is as it was.
        if (doc.World is { } world && KeyRing.Of(rules, world.Flags, world.Things) is { IsEmpty: false } keys)
            view["keys"] = JsonSerializer.SerializeToNode(keys, ArcJson.Options);
        return JsonSerializer.SerializeToElement(view, ArcJson.Options);
    }

    /// <summary>
    /// The colours on offer, the grounds they're checked against and the contrast they need, and the colour the member has now (their
    /// pick, else the default they'll be given), which creation starts on, and the colours other members have, which the picker marks: a
    /// player may share one, but should know.
    /// </summary>
    private static object Colors(string current, IEnumerable<object> taken) => new
    {
        palette = PlayerColors.Palette, grounds = PlayerColors.Grounds, minContrast = PlayerColors.MinContrast, current, taken,
    };

    /// <summary>The document with every member's colour assigned (<see cref="PlayerColors.Assign"/>), in the campaign's join order.</summary>
    private static TacticsDocument Coloured(TacticsDocument doc, CampaignInfo info)
        => doc with { Members = PlayerColors.Assign(doc.Members, PlayerColors.JoinOrder(info)) };

    /// <summary>The colour a member without a character will be given: the first free one from their place in join order.</summary>
    private static string NewMemberColor(TacticsDocument doc, CampaignInfo info, ProfileId member)
    {
        var index = info.Members.Select(m => m.Profile).ToList().IndexOf(member);
        return PlayerColors.FreeDefault(index < 0 ? info.Members.Count : index, doc.Members.Values.Select(r => r.Color));
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
        var choices = cls is null ? [] : SheetBuilder.PickChoices(cls, subclass, c.Level);
        var placed = SheetBuilder.AssignPicks(picks, choices);
        var sheet = Sheet(c, rules);
        var view = JsonSerializer.SerializeToNode(new
        {
            name = c.Name, describe = Describe(c), race = c.Race, @class = c.Class, subclass = c.Subclass, background = c.Background,
            level = c.Level, xp = record.Xp, scores, picks, freeRemake = record.RemakeIsTheFreeOne,
            injuries = c.Injuries.Select(i => rules.Injuries.GetValueOrDefault(i)?.Name ?? i.Value),
            increasesLeft = CharacterRules.IncreasesLeft(c),
            subclasses = CharacterRules.NeedsSubclass(c) && cls is not null ? cls.Subclasses.Select(s => new { id = s.Id, name = s.Name }) : null,
            choices = choices.Select((ch, i) => new
            {
                level = ch.Level,
                choose = ch.Feature.Choose,
                from = ch.Feature.From.Select(id => Option(id, c.Level, rules)),
                picked = placed[i],
            }).ToArray(),
            color = record.Color,
            skills = sheet?.Skills.ToImmutableArray() ?? [],
            saves = sheet?.SaveProficiencies ?? [],
            // Each skill's bonus in a story check, as SkillCheck rolls it.
            bonuses = sheet is null ? null : SkillCheck.Skills.Keys.ToImmutableSortedDictionary(s => s, s => SkillCheck.Bonus(sheet, s), StringComparer.Ordinal),
        }, ArcJson.Options)!.AsObject();
        if (c.Trinket is { } id && rules.Trinkets.TryGetValue(id, out var trinket))
            view["trinket"] = JsonSerializer.SerializeToNode(new
            {
                id, trinket.Name, trinket.Text, trinket.Rarity, @class = trinket.Class,
                className = rules.Classes[trinket.Class].Name, active = trinket.ActiveFor(c),
            }, ArcJson.Options);
        if (CompanionNames.Monster(c, rules) is { } monster)
            view["companion"] = JsonSerializer.SerializeToNode(new
            {
                monster, name = c.CompanionName ?? rules.Monsters[monster].Name,
                defaultName = rules.Monsters[monster].Name, suggestions = CompanionNames.Suggestions(monster),
            }, ArcJson.Options);
        return view;
    }

    /// <summary>The character's battle sheet, for its skills and saves; null for a record the pack can't build (an old or damaged one).</summary>
    private static UnitSheet? Sheet(CharacterRecord c, RulesPack rules)
    {
        try
        {
            return SheetBuilder.ForCharacter(c, rules);
        }
        catch (ContentException)
        {
            return null;
        }
    }

    private static object Option(string id, int level, RulesPack rules)
        => rules.Abilities.TryGetValue(new AbilityId(id), out var a)
            ? new { id, name = a.Name, tooltip = Tooltips.For(a, level, rules) }
            : rules.Passives.TryGetValue(id, out var p)
                ? new { id, name = Humanize(id.Split('.')[^1]), tooltip = Tooltips.For(p, level, rules) }
                : new { id, name = id, tooltip = "" };

    /// <inheritdoc />
    /// <remarks>
    /// <c>createCharacter</c> {race, class, background, scores}, while the member has no character or may remake it;
    /// <c>setPicks</c> {picks}; <c>chooseSubclass</c> {subclass}; <c>spendIncrease</c> {stats: one or two}; <c>setColor</c> {color}.
    /// <c>createCharacter</c> may also carry a <c>color</c>. Any colour is made readable on the TV's grounds (<see cref="PlayerColors.Readable"/>)
    /// before it's kept, and a remake without one keeps the colour the member had.
    /// </remarks>
    public CommandResult Handle(CampaignInfo info, JsonElement document, ProfileId member, string type, JsonElement data)
    {
        try
        {
            var rules = pack.Value;
            // Any command keeps the colours of members who had none (PlayerColors.Assign), so a default never shifts later.
            var doc = Coloured(TacticsDocument.Read(document), info);
            var mine = doc.For(member);
            (CharacterRecord? Character, string? Problem) result;
            string? color = null;
            switch (type)
            {
                case "createCharacter":
                    if (mine is { CanRemake: false })
                        return new(null, "You've used your free remake; this character is yours now.");
                    var name = info.Members.FirstOrDefault(m => m.Profile == member)?.DisplayName ?? "Adventurer";
                    var scores = new Dictionary<Stat, int>();
                    if (data.TryGetProperty("scores", out var s) && s.ValueKind == JsonValueKind.Object)
                        foreach (var p in s.EnumerateObject())
                            if (ParseStat(p.Name) is { } stat && p.Value.TryGetInt32(out var v))
                                scores[stat] = v;
                    result = CharacterRules.Create(name, Str(data, "race"), Str(data, "class"), Str(data, "background"), scores,
                        doc.PartyLevel, rules);
                    if (data.TryGetProperty("color", out _))
                    {
                        if (PlayerColors.Readable(Str(data, "color")) is not { } picked)
                            return new(null, "Pick a colour.");
                        color = picked;
                    }
                    break;
                case "setColor" when mine is not null:
                    if (PlayerColors.Readable(Str(data, "color")) is not { } chosen)
                        return new(null, "Pick a colour.");
                    var coloured = doc with { Members = doc.Members.SetItem(member.Value, mine with { Color = chosen }) };
                    return new(coloured.ToJson(), null);
                case "setPicks" when mine is not null:
                    var picks = data.TryGetProperty("picks", out var list) && list.ValueKind == JsonValueKind.Array
                        ? list.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => new AbilityId(e.GetString()!)).ToList()
                        : [];
                    result = CharacterRules.SetPicks(mine.Character, picks, rules);
                    break;
                case "chooseSubclass" when mine is not null:
                    result = CharacterRules.ChooseSubclass(mine.Character, Str(data, "subclass"), rules);
                    if (result.Character is { } chosenCharacter)
                    {
                        var named = CompanionNames.Normalize(Str(data, "companionName"));
                        if (named.Problem is not null) return new(null, named.Problem);
                        result = (chosenCharacter with { CompanionName = CompanionNames.Monster(chosenCharacter, rules) is null ? null : named.Name }, null);
                    }
                    break;
                case "nameCompanion" when mine is not null:
                    if (doc.Battle is not null) return new(null, "Name your companion outside a battle.");
                    if (data.TryGetProperty("member", out var target) && target.GetString() != member.Value)
                        return new(null, "Only you can name your companion.");
                    if (CompanionNames.Monster(mine.Character, rules) is null) return new(null, "Your subclass has no companion.");
                    var normalized = CompanionNames.Normalize(Str(data, "name"));
                    if (normalized.Problem is not null) return new(null, normalized.Problem);
                    result = (mine.Character with { CompanionName = normalized.Name }, null);
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
            var record = mine is null ? new MemberRecord(character, doc.StartingXpForNewMember(rules.Xp), Color: color)
                : type == "createCharacter"
                    // Every remake keeps the existing inventory, injuries and purse. Starting kits are granted only
                    // on first creation, so selling a kit then remaking cannot refill it. Only post-battle remakes use the free one.
                    ? mine with { Character = character with { Injuries = mine.Character.Injuries, Bag = mine.Character.Bag, Trinket = mine.Character.Trinket }, Remade = mine.Remade || mine.HasFought }
                    : mine with { Character = character };
            if (color is not null)
                record = record with { Color = color };
            var updated = Coloured(doc with { Members = doc.Members.SetItem(member.Value, record) }, info);
            return new(updated.ToJson(), null);
        }
        catch (Exception e) when (e is InvalidOperationException or FormatException or JsonException)
        {
            return new(null, "That didn't make sense to the game.");
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            // Handle never throws (the platform turns an exception into a 500): an odd document is refused, not fatal.
            return new(null, "Your character couldn't be changed: the saved campaign looks damaged.");
        }
    }

    private static string Str(JsonElement data, string name)
        => data.ValueKind == JsonValueKind.Object && data.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";

    /// <summary>"sneak_attack" → "Sneak attack".</summary>
    private static string Humanize(string id) => id.Length == 0 ? id : char.ToUpperInvariant(id[0]) + id[1..].Replace('_', ' ');

    /// <summary>"str" (or "Str"): one of the six names, never a number or a list ("7", "str,dex").</summary>
    private static Stat? ParseStat(string s)
        => Enum.GetValues<Stat>().Cast<Stat?>().FirstOrDefault(st => string.Equals(st.ToString(), s, StringComparison.OrdinalIgnoreCase));

    /// <summary>"Human Fighter", from the pack's race and class names.</summary>
    public string Describe(CharacterRecord character)
        => $"{pack.Value.Races.GetValueOrDefault(character.Race)?.Name ?? character.Race} {pack.Value.Classes.GetValueOrDefault(character.Class)?.Name ?? character.Class}";
}
