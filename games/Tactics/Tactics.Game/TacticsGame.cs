using System.Collections.Immutable;
using System.Text.Json;
using ArcBox.Sdk;
using ArcBox.Sdk.Campaigns;
using Tactics.Logic;

namespace Tactics.Game;

/// <summary>Where a session is (adapter spec §3).</summary>
public enum TacticsPhase { Muster, EncounterPick, Battle, Results, Resuming }

/// <summary>
/// ArcBox Tactics on the platform (adapter spec): a campaign game whose sessions muster the party, pick an encounter and
/// play it on <see cref="Battle"/>, with phones choosing for present players and the AI for everyone else. It does no I/O
/// and never reads the clock: time is <see cref="IGameContext"/>'s, and the campaign document is saved through the lease
/// after every command.
/// </summary>
public sealed partial class TacticsGame(Lazy<RulesPack> pack, TacticsTiming? timing = null)
    : IGame, ICampaignGame, IResumableGame, IHostAware
{
    /// <summary>A game on a loaded pack (tests, tools).</summary>
    public TacticsGame(RulesPack rules, TacticsTiming? timing = null) : this(new Lazy<RulesPack>(rules), timing)
    {
    }

    /// <summary>
    /// The rules, loaded on first use: the server's registry makes a game just to read its manifest, which mustn't need
    /// the content.
    /// </summary>
    private RulesPack Rules => pack.Value;

    /// <summary>The game id in the manifest and on the phone (<c>web/play/src/games/tactics</c>).</summary>
    public const string Id = "tactics";

    /// <summary>The phone module's screens (adapter spec §8).</summary>
    public static class Screens
    {
        public const string PickClass = "pickClass", PickEncounter = "pickEncounter", Turn = "turn", React = "react",
            Status = "status", Results = "results";
    }

    /// <summary>The timer keys.</summary>
    public static class Timers
    {
        public const string Turn = "turn", React = "react", Pace = "pace";
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
    private string? _resultEncounter;
    private ImmutableArray<ProfileId> _resultMembers = [];

    // Resuming: the members who were playing before the restart and haven't come back yet.
    private readonly HashSet<ProfileId> _awaited = [];

    /// <inheritdoc />
    public GameManifest Manifest { get; } = new(Id, "Tactics", "Turn-based co-op battles, played week after week", MinPlayers: 1,
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
        Save();
        if (_doc.Battle is { } unfinished)   // the last session stopped mid-battle: carry on with whoever is here
        {
            if (unfinished.State.ContentHash != Rules.Hash)
            {
                _notice = "The rules changed since this battle began, so it starts again.";
                RestartBattle(unfinished);
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
        _doc = TacticsDocument.Read(_lease.Document);
        if (_doc.Battle is not { } save)
        {
            ToMusterOrPick();
            return;
        }

        if (save.State.ContentHash != Rules.Hash)
        {
            _notice = "The rules changed since this battle began, so it starts again.";
            RestartBattle(save);
        }
        _awaited.Clear();
        foreach (var unit in _doc.Battle!.State.Units.Values)
            if (unit.Controller is PlayerControlled { Owner: var owner } && _players.Any(p => p.Profile == owner))
                _awaited.Add(owner);
        _phase = TacticsPhase.Resuming;
        if (_awaited.Count == 0)
            ContinueBattle();
    }

    /// <inheritdoc />
    public void OnPlayerConnectionChanged(PlayerId id, bool connected)
    {
        if (connected ? !_online.Add(id) : !_online.Remove(id))
            return;
        var player = _players.FirstOrDefault(p => p.Id == id);
        switch (_phase)
        {
            case TacticsPhase.Muster:
                if (!connected || player is null)
                    MusterDone();
                break;
            case TacticsPhase.Resuming:
                if (connected && player?.Profile is { } profile && _awaited.Remove(profile) && _awaited.Count == 0)
                    ContinueBattle();
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
        switch (_phase)
        {
            case TacticsPhase.Muster when custom.Action == "pickClass":
                PickClass(player, profile, custom.Data);
                break;
            case TacticsPhase.EncounterPick when custom.Action == "pickEncounter" && IsVip(player):
                PickEncounter(player, custom.Data);
                break;
            case TacticsPhase.Battle:
                BattleInput(player, profile, custom);
                break;
            case TacticsPhase.Results when IsVip(player) && custom.Action is "next":
                _result = null;
                ToMusterOrPick();
                break;
            case TacticsPhase.Results when IsVip(player) && custom.Action is "end":
                _ctx.EndGame(new GameResults([], $"The party is level {_doc.PartyLevel}; {_doc.Completed.Length} battles won."));
                break;
            case TacticsPhase.Resuming when IsVip(player) && custom.Action == "continue":
                _awaited.Clear();
                ContinueBattle();
                break;
        }
    }

    /// <inheritdoc />
    public void OnTimer(string key)
    {
        if (_phase != TacticsPhase.Battle)
            return;
        switch (key)
        {
            case Timers.Pace:
                _awaitShown = null;
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

    /// <summary>Muster ends once every connected member has a character (absent ones get theirs at battle start).</summary>
    private void MusterDone()
    {
        if (_phase == TacticsPhase.Muster && _players.Where(p => _online.Contains(p.Id)).All(p => _doc.For(p.Profile!.Value) is not null))
        {
            _toasts.Clear();
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
    }

    private CharacterRecord NewCharacter(string name, string cls, int level)
        => SheetBuilder.DefaultCharacter(name, "human", cls, null, level, Rules);

    private void SetMember(ProfileId profile, MemberRecord record)
        => _doc = _doc with { Members = _doc.Members.SetItem(profile.Value, record) };

    /// <summary>
    /// Characters for members who have none and aren't here to pick: the class that best fills the party's missing role
    /// (healer, then defender, then striker, then control), so the party is always whole (adapter spec §3).
    /// </summary>
    private void FillAbsentCharacters()
    {
        string[] roles = ["healer", "defender", "striker", "control"];
        foreach (var player in _players.Where(p => _doc.For(p.Profile!.Value) is null))
        {
            var have = _doc.Members.Values.Select(m => Rules.Classes.GetValueOrDefault(m.Character.Class)?.Role).ToArray();
            var role = roles.FirstOrDefault(r => !have.Contains(r)) ?? roles[have.Length % roles.Length];
            var cls = Rules.Classes.Values.Where(c => c.Role == role).Select(c => c.Id).Order(StringComparer.Ordinal)
                .FirstOrDefault() ?? Rules.Classes.Keys.First();
            SetMember(player.Profile!.Value, new MemberRecord(NewCharacter(player.Name, cls, _doc.PartyLevel), Xp: 0));
        }
    }

    private void PickEncounter(Player vip, JsonElement? data)
    {
        var id = Str(data, "encounter");
        if (id is null || !Rules.Encounters.TryGetValue(id, out var encounter))
        {
            Toast(vip.Id, "Pick one of the battles.");
            return;
        }
        if (_players.Count < encounter.Party.Min || _players.Count > encounter.Party.Max)
        {
            Toast(vip.Id, $"{encounter.Name} needs a party of {encounter.Party.Min}–{encounter.Party.Max}.");
            return;
        }
        FillAbsentCharacters();
        StartBattle(id, (ulong)_ctx.Random.NextInt64(), [.. _players.Select(p => p.Profile!.Value)]);
    }

    // ---- Results ------------------------------------------------------------------------------------------------------

    private void EndBattle()
    {
        var save = _doc.Battle!;
        var state = save.State;
        var result = Battle.Result(state, Rules)!;
        var members = _doc.Members;
        for (var i = 0; i < save.Members.Length; i++)
        {
            var unit = UnitFor(i);
            if (!members.TryGetValue(save.Members[i].Value, out var record) || !state.Units.TryGetValue(unit, out var u))
                continue;
            var injuries = result.Injuries.GetValueOrDefault(unit, []);
            members = members.SetItem(save.Members[i].Value, record with
            {
                Xp = record.Xp + result.Xp.GetValueOrDefault(unit),
                Character = record.Character with { Injuries = record.Character.Injuries.AddRange(injuries), Bag = u.Bag },
            });
        }

        var level = TacticsDocument.LevelFor(members.Values.Select(m => m.Xp), Rules.Xp);
        if (level > _doc.PartyLevel)
            members = Ordinal.Map(members.Select(m => (m.Key, m.Value with { Character = LevelUp(m.Value.Character, level) })));
        var name = Rules.Encounters.GetValueOrDefault(save.Encounter)?.Name ?? save.Encounter;
        _doc = _doc with
        {
            Members = members, PartyLevel = Math.Max(level, _doc.PartyLevel), Battle = null,
            Completed = result.Outcome == BattleOutcome.Won ? _doc.Completed.Add(save.Encounter) : _doc.Completed,
        };
        _lease.Checkpoint(_doc.ToJson(), $"After {name}");
        (_result, _resultEncounter, _resultMembers, _phase) = (result, name, save.Members, TacticsPhase.Results);
        _toasts.Clear();
        CancelBattleTimers();
    }

    /// <summary>The character rebuilt at <paramref name="level"/> with its default picks, keeping its injuries and bag.</summary>
    private CharacterRecord LevelUp(CharacterRecord c, int level)
        => SheetBuilder.DefaultCharacter(c.Name, c.Race, c.Class, c.Subclass, level, Rules) with { Injuries = c.Injuries, Bag = c.Bag };

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
