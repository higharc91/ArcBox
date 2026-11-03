using System.Collections.Immutable;
using System.Text.Json;
using ArcBox.Sdk;
using ArcBox.Sdk.Campaigns;
using Tactics.Logic;

namespace Tactics.Game;

/// <summary>Where a session is (adapter spec §3).</summary>
public enum TacticsPhase { Muster, EncounterPick, Battle, Results, Resuming, Exploration, Story }

/// <summary>
/// ArcBox Tactics on the platform (adapter spec): a campaign game whose sessions muster the party, pick an encounter and
/// play it on <see cref="Battle"/>, with phones choosing for present players and the AI for everyone else. It does no I/O
/// and never reads the clock: time is <see cref="IGameContext"/>'s, and the campaign document is saved through the lease
/// after every command. Its story is read from <c>lore</c>, and without it no beat is ever on offer. A <c>seed</c> starts a campaign
/// with no place in the world yet part way through its story (Development only).
/// <para>
/// With a region, fights happen in the world, and the VIP's battle list ("Choose a battle" on the walk screen) is offered only with
/// <c>battleList</c>, a Development-only switch (the user's decision): it's for trying any fight without walking to it. A pack with no
/// region has no world to fight in, so its battle list is always there, as it was before the world.
/// </para>
/// </summary>
public sealed partial class TacticsGame(Lazy<RulesPack> pack, TacticsTiming? timing = null, Lazy<LoreBook>? lore = null, TacticsSeed? seed = null,
    bool battleList = false)
    : IGame, ICampaignGame, IResumableGame, IHostAware
{
    /// <summary>A game on a loaded pack (tests, tools).</summary>
    public TacticsGame(RulesPack rules, TacticsTiming? timing = null, LoreBook? lore = null, TacticsSeed? seed = null, bool battleList = false)
        : this(new Lazy<RulesPack>(rules), timing, lore is null ? null : new Lazy<LoreBook>(lore), seed, battleList)
    {
    }

    /// <summary>
    /// The rules, loaded on first use: the server's registry makes a game just to read its manifest, which mustn't need
    /// the content.
    /// </summary>
    private RulesPack Rules => pack.Value;

    /// <summary>The arcs the story is played from, loaded on first use as the rules are; <see cref="LoreBook.Empty"/> without any.</summary>
    public LoreBook Lore => lore?.Value ?? LoreBook.Empty;

    /// <summary>The game id in the manifest and on the phone (<c>web/play/src/games/tactics</c>).</summary>
    public const string Id = "tactics";

    /// <summary>The phone module's screens (adapter spec §8).</summary>
    public static class Screens
    {
        public const string PickClass = "pickClass", PickEncounter = "pickEncounter", Turn = "turn", React = "react",
            Status = "status", Results = "results", Walk = "walk", Dialogue = "dialogue", Vote = "vote", Check = "check";
    }

    /// <summary>The timer keys.</summary>
    public static class Timers
    {
        public const string Turn = "turn", React = "react", Pace = "pace", Step = "step", Lead = "lead", Save = "save", Line = "line", Vote = "vote";
    }

    private readonly TacticsTiming _timing = timing ?? TacticsTiming.Default;

    private IGameContext _ctx = null!;
    private ICampaignLease _lease = null!;
    private IReadOnlyList<Player> _players = [];
    private readonly HashSet<PlayerId> _online = [];
    private TacticsDocument _doc = TacticsDocument.New;
    private TacticsPhase _phase;
    private string? _notice;
    private readonly Dictionary<PlayerId, (int Count, string Text)> _toasts = [];

    // Results: who was in the battle, in party order, so lines match units whatever the seating is now.
    private BattleResult? _result;
    private string? _resultNote;
    private BattleView? _lastBattle;
    private string? _resultEncounter;
    private ImmutableArray<ProfileId> _resultMembers = [];

    // Resuming: the members who were playing before the restart and haven't come back yet.
    private readonly HashSet<ProfileId> _awaited = [];

    /// <inheritdoc />
    public GameManifest Manifest { get; } = new(Id, Brand.Name, Brand.Tagline, MinPlayers: 1,
        MaxPlayers: 6, UsesCampaigns: true, ResumesAfterRestart: true);

    /// <summary>
    /// Who picks the battle and moves the session on: the platform's VIP while they're here, else the first connected
    /// player. The platform's flag is fixed when the session starts (and is nobody's after a restart, when nobody is
    /// connected yet), so the game decides for itself rather than wait on someone who may never come back.
    /// </summary>
    private Player? Vip => _players.FirstOrDefault(p => p.IsVip && _online.Contains(p.Id))
                           ?? _players.FirstOrDefault(p => _online.Contains(p.Id));

    private bool IsVip(Player player) => Vip?.Id == player.Id;

    /// <summary>The phase the session is in.</summary>
    public TacticsPhase Phase => _phase;

    /// <summary>The campaign document as last saved.</summary>
    public TacticsDocument Document => _doc;

    // ---- Lifecycle ----------------------------------------------------------------------------------------------------

    /// <summary>A session outside a campaign (local play and tests): an in-memory document that nothing keeps.</summary>
    public void Start(IGameContext ctx, IReadOnlyList<Player> players)
        => Start(ctx, [.. players.Select((p, i) => p.Profile is null ? p with { Profile = new ProfileId($"pr_local{i + 1}") } : p)],
            new MemoryLease(TacticsDocument.New.ToJson()));

    /// <inheritdoc />
    public void Start(IGameContext ctx, IReadOnlyList<Player> players, ICampaignLease campaign)
    {
        (_ctx, _players, _lease) = (ctx, players, campaign);
        foreach (var p in players)
            _online.Add(p.Id);
        _doc = TacticsDocument.Read(campaign.Document) with { RulesHash = Rules.Hash };
        if (seed is not null)
            _doc = seed.SeedInto(_doc, Rules);
        _doc = Coloured(_doc);
        EnterWorld();
        ElectAtStart();
        KeepWorld();
        Save();
        if (_doc.Battle is { } unfinished)   // the last session stopped mid-battle: carry on with whoever is here
        {
            if (unfinished.State.ContentHash != Rules.Hash)
            {
                if (!RestartBattle(unfinished))
                    return;     // called off: the party is back where a battle's results would have sent it
            }
            ContinueBattle();
            return;
        }
        ToMusterOrPick();
    }

    /// <inheritdoc />
    public void Resume(IGameContext ctx, IReadOnlyList<Player> players, ICampaignLease? campaign)
    {
        (_ctx, _players, _lease) = (ctx, players, campaign ?? new MemoryLease(TacticsDocument.New.ToJson()));
        _doc = Coloured(TacticsDocument.Read(_lease.Document));
        EnterWorld();
        _sessionLead = _doc.World?.LastLeader;     // a restart isn't a new session, so the lead doesn't rotate
        SaveWorld();
        _awaited.Clear();
        if (_doc.Battle is not { } save)
        {
            // A scene under way waits for the party as a battle does (5d §8): nobody is connected yet, and a scene played to nobody would be
            // lost on them. A beat waiting at its fight, or no beat, has nothing to play, so the party is simply back in the world.
            if (_region is null || (_doc.World?.Shop is null && _doc.World?.Story is not { Stage: StorySave.Lead or StorySave.After }))
            {
                ToMusterOrPick();
                return;
            }
            foreach (var player in _players)
                if (player.Profile is { } member && _doc.For(member) is not null)
                    _awaited.Add(member);
        }
        else
        {
            if (save.State.ContentHash != Rules.Hash)
            {
                if (!RestartBattle(save))
                    return;
            }
            foreach (var unit in _doc.Battle!.State.Units.Values)
                if (unit.Controller is PlayerControlled { Owner: var owner } && _players.Any(p => p.Profile == owner))
                    _awaited.Add(owner);
        }
        _phase = TacticsPhase.Resuming;
        if (_awaited.Count == 0)
            CarryOn();
    }

    /// <summary>
    /// Out of Resuming, once everyone awaited is back or the VIP goes on without them: into the battle, or to the scene's saved line. Anyone
    /// here with no character picks one during the scene, as they can at any time in one, rather than hold it up at Muster.
    /// </summary>
    private void CarryOn()
    {
        _awaited.Clear();
        _toasts.Clear();
        if (_doc.Battle is not null)
            ContinueBattle();
        else
            ResumeStory();
    }

    /// <inheritdoc />
    public void OnPlayerConnectionChanged(PlayerId id, bool connected)
    {
        if (connected ? !_online.Add(id) : !_online.Remove(id))
            return;
        var player = _players.FirstOrDefault(p => p.Id == id);
        WalkerConnection(player, connected);
        PartyChanged();
        switch (_phase)
        {
            case TacticsPhase.Muster:
                if (!connected || player is null)
                    MusterDone();
                break;
            case TacticsPhase.Resuming:
                if (connected && player?.Profile is { } profile && _awaited.Remove(profile) && _awaited.Count == 0)
                    CarryOn();
                break;
            case TacticsPhase.Story:
                if (_doc.World?.Shop is not null)
                {
                    if (_online.Count == 0) CloseShop();
                    break;
                }
                StoryPresenceChanged();
                break;
            case TacticsPhase.Battle:
                if (player?.Profile is { } who)
                    SeatChanged(who, connected);
                break;
        }
    }

    /// <inheritdoc />
    public void OnInput(PlayerId from, PlayerInput input)
    {
        var player = _players.FirstOrDefault(p => p.Id == from);
        if (player?.Profile is not { } profile)
            return;
        CustomInput custom;
        try
        {
            custom = input.Read<CustomInput>();
        }
        catch (JsonException)
        {
            return;
        }
        if (custom.Action is null)
            return;
        HeardFrom(profile);
        switch (_phase)
        {
            case TacticsPhase.Muster when custom.Action == "pickClass":
                PickClass(player, profile, custom.Data);
                break;
            case TacticsPhase.Exploration when custom.Action == "walk":
                WalkInput(player, custom.Data);
                break;
            case TacticsPhase.Exploration when custom.Action == "pickClass":
                PickClass(player, profile, custom.Data);
                break;
            case TacticsPhase.Exploration when custom.Action == "interact":
                Interact(player, custom.Data);
                break;
            case TacticsPhase.Exploration when custom.Action == "search":
                Search(player);
                break;
            case TacticsPhase.Story when custom.Action == "pickClass":
                PickClass(player, profile, custom.Data);
                break;
            case TacticsPhase.Story when custom.Action == "endSession" && IsVip(player):
                EndSession();       // the cursor is saved on every line, so the next session starts on this one (review)
                break;
            case TacticsPhase.Story when _doc.World?.Shop is not null:
                ShopInput(player, profile, custom);
                break;
            case TacticsPhase.Story:
                StoryInput(player, profile, custom);
                break;
            case TacticsPhase.Exploration when custom.Action == "battles" && IsVip(player) && battleList:
                LeaveExploration();
                _toasts.Clear();
                _phase = TacticsPhase.EncounterPick;
                break;
            case TacticsPhase.Exploration when custom.Action == "endSession" && IsVip(player):
                EndSession();
                break;
            case TacticsPhase.EncounterPick when custom.Action == "pickEncounter" && IsVip(player):
                PickEncounter(player, custom.Data);
                break;
            case TacticsPhase.EncounterPick when custom.Action == "backToWalk" && IsVip(player) && _region is not null:
                ToExploration();
                break;
            case TacticsPhase.Battle when !IsWorldAction(custom.Action):
                BattleInput(player, profile, custom);
                break;
            case TacticsPhase.Results when IsVip(player) && custom.Action is "next":
                _result = null;
                ToMusterOrPick();
                break;
            case TacticsPhase.Results when IsVip(player) && custom.Action is "end":
                EndSession();
                break;
            case TacticsPhase.Resuming when IsVip(player) && custom.Action == "continue":
                CarryOn();
                break;
        }
    }

    /// <inheritdoc />
    public void OnTimer(string key)
    {
        switch (_phase)
        {
            case TacticsPhase.Battle:
                BattleTimer(key);
                break;
            case TacticsPhase.Exploration:
                WalkTimer(key);
                break;
            case TacticsPhase.Story when _doc.World?.Shop is null:
                StoryTimer(key);
                break;
        }
    }

    private void BattleTimer(string key)
    {
        switch (key)
        {
            case Timers.Pace:
                (_awaitShown, _formUp) = (null, null);
                Drive();
                break;
            case Timers.Turn:
                TurnTimedOut();
                break;
            case Timers.React:
                ReactionTimedOut();
                break;
        }
    }

    /// <inheritdoc />
    public void OnHostSignal(string kind, PlayerInput payload)
    {
        if (kind != "shown" || _phase != TacticsPhase.Battle || _awaitShown is not { } awaited)
            return;
        int seq;
        try
        {
            seq = payload.Payload.GetProperty("seq").GetInt32();
        }
        catch (Exception e) when (e is InvalidOperationException or KeyNotFoundException or FormatException)
        {
            return;
        }
        FormedUp(seq);
        if (seq < awaited)
            return;     // a stale signal
        _awaitShown = null;
        _ctx.CancelTimer(Timers.Pace);
        Drive();
    }

    // ---- Muster and the encounter pick --------------------------------------------------------------------------------

    private void ToMusterOrPick()
    {
        _toasts.Clear();
        _phase = TacticsPhase.Muster;
        MusterDone();
    }

    /// <summary>
    /// Muster ends once every connected member has a character (absent ones get theirs at battle start), in the world, or in the scene the
    /// last session or a restart left off in, on its saved line (overworld 5d §8).
    /// </summary>
    private void MusterDone()
    {
        if (_phase == TacticsPhase.Muster && _players.Where(p => _online.Contains(p.Id)).All(p => _doc.For(p.Profile!.Value) is not null))
        {
            _toasts.Clear();
            if (_region is not null && (_doc.World?.Story is not null || _doc.World?.Shop is not null))
                ResumeStory();
            else if (_region is not null)
                ToExploration();
            else
                _phase = TacticsPhase.EncounterPick;
        }
    }

    private void PickClass(Player player, ProfileId profile, JsonElement? data)
    {
        if (_doc.For(profile) is not null)
            return;
        var cls = Str(data, "class");
        if (cls is null || !Rules.Classes.ContainsKey(cls))
        {
            Toast(player.Id, "Pick one of the classes.");
            return;
        }
        SetMember(profile, new MemberRecord(NewCharacter(player.Name, cls, _doc.PartyLevel), Xp: 0));
        Save();
        MusterDone();
        if (_phase is TacticsPhase.Exploration or TacticsPhase.Story)
            ReconsiderLeader();
        PartyChanged();
        if (_phase == TacticsPhase.Story)
            StoryPresenceChanged();     // one more who can answer
    }

    private CharacterRecord NewCharacter(string name, string cls, int level)
        => SheetBuilder.DefaultCharacter(name, "human", cls, null, level, Rules);

    private void SetMember(ProfileId profile, MemberRecord record)
        => _doc = Coloured(_doc with { Members = _doc.Members.SetItem(profile.Value, record) });

    /// <summary>
    /// The document with every member's colour assigned (<see cref="PlayerColors.Assign"/>), kept from then on: in the campaign's join
    /// order, or, for a session outside a campaign, the order the players sat down in.
    /// </summary>
    private TacticsDocument Coloured(TacticsDocument doc)
    {
        var order = PlayerColors.JoinOrder(_lease.Info).ToList();
        if (order.Count == 0)
            order = [.. _players.Where(p => p.Profile is not null).Select(p => p.Profile!.Value.Value)];
        return doc with { Members = PlayerColors.Assign(doc.Members, order) };
    }

    /// <summary>
    /// Characters for members who have none and aren't here to pick: the class that best fills the party's missing role
    /// (healer, then defender, then striker, then control), so the party is always whole (adapter spec §3).
    /// </summary>
    private void FillAbsentCharacters()
    {
        foreach (var player in _players.Where(p => _doc.For(p.Profile!.Value) is null))
        {
            var cls = ClassForMissingRole(_doc.Members.Values.Select(m => m.Character.Class));
            SetMember(player.Profile!.Value, new MemberRecord(NewCharacter(player.Name, cls, _doc.PartyLevel), Xp: 0));
        }
        ReconsiderLeader();     // someone here may have just got the character that lets them lead
    }

    /// <summary>
    /// The class that best fills a party of <paramref name="classes"/>: the first missing role of healer, defender, striker
    /// and control (then round again), and the first class of that role in id order.
    /// </summary>
    private string ClassForMissingRole(IEnumerable<string> classes)
    {
        string[] roles = ["healer", "defender", "striker", "control"];
        var have = classes.Select(c => Rules.Classes.GetValueOrDefault(c)?.Role).ToArray();
        var role = roles.FirstOrDefault(r => !have.Contains(r)) ?? roles[have.Length % roles.Length];
        return Rules.Classes.Values.Where(c => c.Role == role).Select(c => c.Id).Order(StringComparer.Ordinal)
            .FirstOrDefault() ?? Rules.Classes.Keys.First();
    }

    /// <summary>
    /// AI companions to bring a party of the session's players up to <paramref name="min"/> (the encounter's smallest
    /// party), each of the class the party lacks most, at the party's level. Named for their class ("Rogue companion").
    /// </summary>
    private ImmutableArray<CharacterRecord> Companions(int min)
    {
        var classes = _players.Select(p => _doc.For(p.Profile!.Value)!.Character.Class).ToList();
        var companions = ImmutableArray.CreateBuilder<CharacterRecord>();
        while (classes.Count < min)
        {
            var cls = ClassForMissingRole(classes);
            classes.Add(cls);
            companions.Add(NewCharacter($"{Rules.Classes[cls].Name} companion", cls, _doc.PartyLevel));
        }
        return companions.ToImmutable();
    }

    private void PickEncounter(Player vip, JsonElement? data)
    {
        var id = Str(data, "encounter");
        if (id is null || !Rules.Encounters.TryGetValue(id, out var encounter))
        {
            Toast(vip.Id, "Pick one of the battles.");
            return;
        }
        if (_players.Count > encounter.Party.Max)
        {
            Toast(vip.Id, $"{encounter.Name} takes at most {encounter.Party.Max} heroes.");
            return;
        }
        FillAbsentCharacters();
        // A party smaller than the encounter needs is filled with AI companions.
        if (!StartBattle(id, (ulong)_ctx.Random.NextInt64(), [.. _players.Select(p => p.Profile!.Value)], Companions(encounter.Party.Min)))
            Toast(vip.Id, BattleWontStart);
    }

    // ---- Results ------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Ends the battle at Results. Every hero gains the XP the result pays (none on a loss, by the rules) and keeps what it used from the bag.
    /// A win or a flight keeps the fight's injuries; a loss doesn't (the user's rule, 2026-11-01): the party wakes with that fight's wounds
    /// tended, earlier injuries as they were, and the potions it drank gone (<see cref="LossNote"/>). HP isn't carried between fights.
    /// </summary>
    private void EndBattle()
    {
        var save = _doc.Battle!;
        var state = save.State;
        if (TryResult(state) is not { } result)
        {
            CallOffBattle();
            return;
        }
        _lastBattle = BattleViewOf();
        var members = _doc.Members;
        for (var i = 0; i < save.Members.Length; i++)
        {
            var unit = UnitFor(i);
            if (!members.TryGetValue(save.Members[i].Value, out var record) || !state.Units.TryGetValue(unit, out var u))
                continue;
            members = members.SetItem(save.Members[i].Value, record with
            {
                Xp = record.Xp + result.Xp.GetValueOrDefault(unit),
                Fought = true,
                Character = record.Character with
                {
                    Injuries = result.Outcome == BattleOutcome.Lost
                        ? record.Character.Injuries
                        : record.Character.Injuries.AddRange(result.Injuries.GetValueOrDefault(unit, [])),
                    Bag = u.Bag,
                },
            });
        }

        var level = TacticsDocument.LevelFor(members.Values.Select(m => m.Xp), Rules.Xp);
        if (level > _doc.PartyLevel)
            members = Ordinal.Map(members.Select(m => (m.Key, m.Value with { Character = LevelUp(m.Value.Character, level) })));
        var name = Rules.Encounters.GetValueOrDefault(save.Encounter)?.Name ?? save.Encounter;
        _doc = _doc with
        {
            Members = members, PartyLevel = Math.Max(level, _doc.PartyLevel),
            Completed = result.Outcome == BattleOutcome.Won ? _doc.Completed.Add(save.Encounter) : _doc.Completed,
            World = StoryAfter(save, result),
        };
        if (result.Outcome == BattleOutcome.Won)
            _doc = _doc.ChangeGold(Rules.Encounters[save.Encounter].Gold, save.Members.Select(m => m.Value));
        // The story first, then the place, so that the place keeps clear of a stamp the story has just armed; both before the checkpoint,
        // so a restart at Results resumes on the new tile with the beat done.
        Land(save, result.Outcome);
        _doc = _doc with { Battle = null };
        KeepWorld();
        _lease.Checkpoint(_doc.ToJson(), $"After {name}");
        (_result, _resultEncounter, _resultMembers, _phase) = (result, name, save.Members, TacticsPhase.Results);
        _resultNote = result.Outcome == BattleOutcome.Lost ? LossNote(woke: _arrived == Woke) : null;
        _toasts.Clear();
        CancelBattleTimers();
    }

    /// <summary>
    /// The story after a fight (5c §6, defaults 6 and 7; 5d §3): a beat won sets the flags the battle set and the beat's own, and goes on to
    /// its aftermath (<see cref="StorySave.After"/>), which plays once Results are over; the beat is done when that ends. A loss or a flight
    /// changes nothing, so the beat waits at its fight with its stamp armed, and a fight from the battle list is no beat. A beat the lore no
    /// longer has is simply done, as before 5d.
    /// </summary>
    private WorldSave? StoryAfter(BattleSave save, BattleResult result)
    {
        if (_doc.World is not { } world || result.Outcome != BattleOutcome.Won || save.Beat is not { } done || world.Beats.Contains(done))
            return _doc.World;
        var beat = BeatNamed(done);
        IEnumerable<string> set = [.. result.FlagsSet.IsDefault ? [] : result.FlagsSet, .. beat?.Sets ?? []];
        world = world with { Flags = [.. world.Flags.Concat(set).Distinct()] };
        return beat is null
            ? world with { Beats = world.Beats.Add(done), Story = null }
            : world with { Story = new StorySave(done, StorySave.After, null, null, 0, _storySeq) };
    }

    /// <summary>The character at the party's new level, as its player made it (<see cref="CharacterRules.LevelUp"/>).</summary>
    private CharacterRecord LevelUp(CharacterRecord c, int level) => CharacterRules.LevelUp(c, level, Rules);

    // ---- Helpers ------------------------------------------------------------------------------------------------------

    private void Save() => _lease.Save(_doc.ToJson());

    private void Toast(PlayerId player, string text)
        => _toasts[player] = (_toasts.GetValueOrDefault(player).Count + 1, text);

    /// <summary>A lease for sessions outside a campaign: it keeps the document in memory only.</summary>
    private sealed class MemoryLease(JsonElement document) : ICampaignLease
    {
        public CampaignInfo Info { get; } = new("local", "LOCL", Id, "Local", new ProfileId("pr_local1"), [], 0);
        public JsonElement Document { get; private set; } = document;
        public void Save(JsonElement document) => Document = document.Clone();
        public void Checkpoint(JsonElement document, string label) => Save(document);
    }
}
