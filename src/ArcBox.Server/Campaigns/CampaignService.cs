using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ArcBox.Sdk;
using ArcBox.Sdk.Campaigns;
using ArcBox.Server.Profiles;
using ArcBox.Server.Rooms;
using ArcBox.Server.Storage;
using Microsoft.Data.Sqlite;

namespace ArcBox.Server.Campaigns;

public sealed record SnapshotInfo(long Id, long Version, string Label, DateTimeOffset CreatedUtc);

public enum CommandStatus { Ok, NotMember, VersionConflict, SessionInProgress, Rejected }

/// <summary>
/// The result of a portal command. <see cref="View"/> is the member's view of the campaign as it is now, whatever the
/// status, except for <see cref="CommandStatus.NotMember"/>, where it is an empty object: the game is never asked to
/// show a campaign to someone who isn't in it.
/// </summary>
public sealed record CommandOutcome(CommandStatus Status, CampaignInfo Info, JsonElement View, string? Error);

/// <summary>
/// Every campaign, in memory (spec §3.4). Everything is loaded when the service is built, so a room or a portal request
/// reads and checks out campaigns without touching the disk; changes are queued on the <see cref="StorageWriter"/>,
/// never written on the caller's thread. One lock guards all state, and a campaign is an immutable record that a change
/// replaces under it. Rules code (<see cref="ICampaignRules"/>) runs under that lock, so it must be quick and do no I/O.
/// </summary>
/// <remarks>
/// Snapshots are kept in memory too, up to <see cref="MaxSnapshots"/> per campaign, which keeps <see cref="Rollback"/>
/// synchronous and lets snapshot ids be assigned here instead of by the database.
/// </remarks>
public sealed class CampaignService
{
    public const int MaxDocumentBytes = 2 * 1024 * 1024;
    public const int MaxSnapshots = 50;
    public const int MaxMembers = 8;
    public const int MaxOwnedPerProfile = 20;
    public const int NameMaxLength = 32;
    public static readonly TimeSpan AutosaveEvery = TimeSpan.FromMinutes(10);

    private static readonly JsonElement EmptyObject = JsonDocument.Parse("{}").RootElement.Clone();

    private readonly StorageWriter _writer;
    private readonly CampaignRulesRegistry _rules;
    private readonly TimeProvider _time;
    private readonly ILogger<CampaignService> _log;

    private readonly Lock _lock = new();
    private readonly Dictionary<string, Campaign> _byId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _idByCode = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<Snapshot>> _snapshots = new(StringComparer.Ordinal); // oldest first
    private readonly Dictionary<string, CampaignLease> _leases = new(StringComparer.Ordinal);
    private long _lastSnapshotId;

    public CampaignService(
        ArcDb db,
        StorageWriter writer,
        CampaignRulesRegistry rules,
        [FromKeyedServices(RoomManager.ClockKey)] TimeProvider time,
        ILogger<CampaignService> log)
    {
        _writer = writer;
        _rules = rules;
        _time = time;
        _log = log;
        Load(db);
    }

    /// <summary>The campaigns of a game, most recently changed first.</summary>
    public IReadOnlyList<CampaignSummary> ListForGame(string gameId)
    {
        lock (_lock)
        {
            return _byId.Values
                .Where(c => c.Info.GameId == gameId)
                .OrderByDescending(c => c.Updated)
                .ThenBy(c => c.Info.Name, StringComparer.OrdinalIgnoreCase)
                .Select(c => new CampaignSummary(c.Info.Id, c.Info.Code, c.Info.Name, c.Info.Members.Count, c.Updated))
                .ToList();
        }
    }

    /// <summary>The campaigns <paramref name="member"/> belongs to (as owner or player), most recently changed first.</summary>
    public IReadOnlyList<CampaignSummary> ForMember(ProfileId member)
    {
        lock (_lock)
        {
            return _byId.Values
                .Where(c => c.Info.Members.Any(m => m.Profile == member))
                .OrderByDescending(c => c.Updated)
                .ThenBy(c => c.Info.Name, StringComparer.OrdinalIgnoreCase)
                .Select(c => new CampaignSummary(c.Info.Id, c.Info.Code, c.Info.Name, c.Info.Members.Count, c.Updated))
                .ToList();
        }
    }

    public CampaignInfo? Find(string id)
    {
        lock (_lock) return _byId.GetValueOrDefault(id)?.Info;
    }

    public CampaignInfo? FindByCode(string code)
    {
        lock (_lock) return Lookup(code)?.Info;
    }

    /// <exception cref="CampaignException">No such campaign.</exception>
    public JsonElement DocumentOf(string id)
    {
        lock (_lock) return Get(id).Document;
    }

    /// <summary>A new campaign with <paramref name="owner"/> as its first member and the game's new document.</summary>
    /// <exception cref="CampaignException">The game has no campaign rules, the name is bad, or the owner has too many campaigns.</exception>
    public CampaignInfo Create(string gameId, string name, ProfileRecord owner)
    {
        var rules = _rules.For(gameId) ?? throw new CampaignException("This game doesn't use campaigns.");
        name = name?.Trim() ?? string.Empty;
        if (name.Length is < 1 or > NameMaxLength) throw new CampaignException("Campaign names are 1–32 characters.");

        var now = _time.GetUtcNow();
        lock (_lock)
        {
            if (_byId.Values.Count(c => c.Info.Owner == owner.Id) >= MaxOwnedPerProfile)
                throw new CampaignException("You already own 20 campaigns.");

            var code = RoomCodes.Generate(_idByCode.ContainsKey);
            var info = new CampaignInfo(NewId(), code, gameId, name, owner.Id,
                [new CampaignMember(owner.Id, owner.DisplayName, CampaignRole.Owner, now)], 0);
            var campaign = new Campaign(info, rules.NewDocument(info).Clone(), now);
            _byId[info.Id] = campaign;
            _idByCode[code] = info.Id;
            Persist(campaign);
            return info;
        }
    }

    /// <summary>Adds <paramref name="member"/> as a player. Adding someone who is already a member changes nothing.</summary>
    /// <exception cref="CampaignException">No such campaign, it is full, or the display name is taken by another member.</exception>
    public CampaignInfo AddMember(string id, ProfileRecord member)
    {
        var now = _time.GetUtcNow();
        lock (_lock)
        {
            var campaign = Get(id);
            var members = campaign.Info.Members;
            if (members.Any(m => m.Profile == member.Id)) return campaign.Info;
            if (members.Count >= MaxMembers) throw new CampaignException("This campaign is full (8 members).");
            if (members.Any(m => string.Equals(m.DisplayName, member.DisplayName, StringComparison.OrdinalIgnoreCase)))
                throw new CampaignException("That name is taken.");

            return Replace(campaign with
            {
                Info = campaign.Info with { Members = [.. members, new CampaignMember(member.Id, member.DisplayName, CampaignRole.Player, now)] },
                Updated = now,
            }).Info;
        }
    }

    /// <summary>Removes a player. Removing someone who isn't a member changes nothing.</summary>
    /// <exception cref="CampaignException">No such campaign, or <paramref name="member"/> is its owner.</exception>
    public CampaignInfo RemoveMember(string id, ProfileId member)
    {
        var now = _time.GetUtcNow();
        lock (_lock)
        {
            var campaign = Get(id);
            if (campaign.Info.Owner == member) throw new CampaignException("The owner can't be removed.");
            if (campaign.Info.Members.All(m => m.Profile != member)) return campaign.Info;

            return Replace(campaign with
            {
                Info = campaign.Info with { Members = [.. campaign.Info.Members.Where(m => m.Profile != member)] },
                Updated = now,
            }).Info;
        }
    }

    /// <summary>
    /// Takes the campaign for <paramref name="roomCode"/>. The same room asking again gets the same lease; another room
    /// gets null while it is held. Leases live only in memory.
    /// </summary>
    /// <exception cref="CampaignException">No such campaign.</exception>
    public CampaignLease? CheckOut(string id, string roomCode)
    {
        lock (_lock)
        {
            Get(id);
            if (_leases.TryGetValue(id, out var held)) return held.RoomCode == roomCode ? held : null;
            return _leases[id] = new CampaignLease(this, id, roomCode);
        }
    }

    /// <summary>Gives the campaign back, if <paramref name="roomCode"/> holds it. The released lease stops saving.</summary>
    public void Release(string id, string roomCode)
    {
        lock (_lock)
        {
            if (_leases.TryGetValue(id, out var held) && held.RoomCode == roomCode) _leases.Remove(id);
        }
    }

    /// <summary>The code of the room that holds the campaign, or null.</summary>
    public string? LeasedBy(string id)
    {
        lock (_lock) return _leases.GetValueOrDefault(id)?.RoomCode;
    }

    /// <summary>The game's portal view for a member (reads work while a session runs).</summary>
    /// <exception cref="CampaignException">No such campaign, or <paramref name="member"/> isn't in it. Both read "Campaign not found.".</exception>
    public JsonElement PortalView(string id, ProfileId member)
    {
        lock (_lock)
        {
            var campaign = Get(id);
            if (campaign.Info.Members.All(m => m.Profile != member)) throw new CampaignException("Campaign not found.");
            return ViewOf(campaign, member);
        }
    }

    /// <summary>
    /// Runs a portal command from <paramref name="member"/>. The checks and the change happen in one step under the lock
    /// that <see cref="CheckOut"/> takes, so a command can't slip into a session that has just started. Returns null
    /// when no campaign has <paramref name="code"/> (the caller's 404).
    /// </summary>
    /// <remarks>
    /// The order is: membership, lease, version, then the game's rules. A command that succeeds bumps the version and
    /// queues a write. If the rules accept it without returning a new document, nothing changes and the status is Ok.
    /// A result over <see cref="MaxDocumentBytes"/> is logged and refused like an error.
    /// </remarks>
    /// <exception cref="CampaignException">The campaign's game has no rules registered.</exception>
    public CommandOutcome? RunCommand(string code, ProfileId member, string type, JsonElement data, long version)
    {
        var now = _time.GetUtcNow();
        lock (_lock)
        {
            if (Lookup(code) is not { } campaign) return null;
            var rules = _rules.For(campaign.Info.GameId) ?? throw new CampaignException("This game doesn't use campaigns.");

            if (campaign.Info.Members.All(m => m.Profile != member))
                return new CommandOutcome(CommandStatus.NotMember, campaign.Info, EmptyObject, null);
            if (_leases.ContainsKey(campaign.Info.Id))
                return Outcome(CommandStatus.SessionInProgress, campaign, member, null);
            if (version != campaign.Info.Version)
                return Outcome(CommandStatus.VersionConflict, campaign, member, null);

            var result = rules.Handle(campaign.Info, campaign.Document, member, type, data);
            if (result.Error is { } error) return Outcome(CommandStatus.Rejected, campaign, member, error);
            if (result.Document is not { } document) return Outcome(CommandStatus.Ok, campaign, member, null);

            var text = document.GetRawText();
            if (TooBig(campaign.Info, text))
                return Outcome(CommandStatus.Rejected, campaign, member, "The campaign document is too big.");

            var changed = Replace(campaign with
            {
                Info = campaign.Info with { Version = campaign.Info.Version + 1 },
                Document = document.Clone(),
                Updated = now,
            });
            return Outcome(CommandStatus.Ok, changed, member, null);
        }
    }

    /// <summary>The campaign's snapshots, newest first (at most <see cref="MaxSnapshots"/>).</summary>
    public IReadOnlyList<SnapshotInfo> Snapshots(string id)
    {
        lock (_lock)
        {
            return _snapshots.TryGetValue(id, out var list)
                ? [.. list.AsEnumerable().Reverse().Select(s => new SnapshotInfo(s.Id, s.Version, s.Label, s.Created))]
                : [];
        }
    }

    /// <summary>
    /// Restores a snapshot's document as a new version (so phones holding an older one conflict). The snapshot stays.
    /// Returns false while a room holds the campaign, or when there is no such campaign or snapshot.
    /// </summary>
    public bool Rollback(string id, long snapshotId)
    {
        var now = _time.GetUtcNow();
        lock (_lock)
        {
            if (!_byId.TryGetValue(id, out var campaign) || _leases.ContainsKey(id)) return false;
            var snapshot = _snapshots.GetValueOrDefault(id)?.FirstOrDefault(s => s.Id == snapshotId);
            if (snapshot is null) return false;

            Replace(campaign with
            {
                Info = campaign.Info with { Version = campaign.Info.Version + 1 },
                Document = JsonDocument.Parse(snapshot.Text).RootElement.Clone(),
                Updated = now,
            });
            return true;
        }
    }

    /// <summary>
    /// A lease's Save or Checkpoint (<paramref name="label"/> set). Ignored once the lease is no longer the holder, or
    /// when the document is over <see cref="MaxDocumentBytes"/>, which is logged at Error and leaves the campaign as it was.
    /// </summary>
    internal void Save(CampaignLease lease, string id, JsonElement document, string? label)
    {
        var text = document.GetRawText(); // outside the lock: it can be megabytes
        var now = _time.GetUtcNow();
        lock (_lock)
        {
            if (!_leases.TryGetValue(id, out var held) || !ReferenceEquals(held, lease)) return;
            var campaign = _byId[id];
            if (TooBig(campaign.Info, text)) return;

            var changed = Replace(campaign with
            {
                Info = campaign.Info with { Version = campaign.Info.Version + 1 },
                Document = document.Clone(),
                Updated = now,
            });

            var list = _snapshots.GetValueOrDefault(id);
            if (label is not null) AddSnapshot(changed, text, label, now);
            else if (list is not { Count: > 0 } || now - list[^1].Created >= AutosaveEvery) AddSnapshot(changed, text, "Autosave", now);
        }
    }

    private Campaign? Lookup(string code) =>
        _idByCode.TryGetValue(RoomCodes.Normalize(code ?? string.Empty), out var id) ? _byId[id] : null;

    private Campaign Get(string id) =>
        _byId.GetValueOrDefault(id) ?? throw new CampaignException("Campaign not found.");

    private bool TooBig(CampaignInfo info, string text)
    {
        var bytes = Encoding.UTF8.GetByteCount(text);
        if (bytes <= MaxDocumentBytes) return false;
        _log.LogError("Campaign {Id} ({Name}): a document of {Bytes} bytes is over the 2 MB limit and was ignored",
            info.Id, info.Name, bytes);
        return true;
    }

    private JsonElement ViewOf(Campaign campaign, ProfileId member) =>
        _rules.For(campaign.Info.GameId) is { } rules
            ? rules.PortalView(campaign.Info, campaign.Document, member)
            : throw new CampaignException("This game doesn't use campaigns.");

    private CommandOutcome Outcome(CommandStatus status, Campaign campaign, ProfileId member, string? error) =>
        new(status, campaign.Info, ViewOf(campaign, member), error);

    private Campaign Replace(Campaign campaign)
    {
        _byId[campaign.Info.Id] = campaign;
        Persist(campaign);
        return campaign;
    }

    // Each queued operation carries the whole row and is keyed by the campaign, so the writer's coalescing keeps only
    // the newest. The first one for a campaign is queued before any of its snapshots, which reference it.
    private void Persist(Campaign c)
    {
        var (id, code, game, name, owner) = (c.Info.Id, c.Info.Code, c.Info.GameId, c.Info.Name, c.Info.Owner.Value);
        var members = JsonSerializer.Serialize(c.Info.Members, ArcJson.Options);
        var document = c.Document.GetRawText();
        var (version, updated) = (c.Info.Version, Iso(c.Updated));
        _writer.Write($"campaign:{id}", connection => Run(connection,
            """
            INSERT INTO campaigns (id, code, game_id, name, owner, members_json, document, version, updated_utc)
            VALUES ($id, $code, $game, $name, $owner, $members, $document, $version, $updated)
            ON CONFLICT (id) DO UPDATE SET members_json = $members, document = $document, version = $version, updated_utc = $updated
            """,
            ("$id", id), ("$code", code), ("$game", game), ("$name", name), ("$owner", owner),
            ("$members", members), ("$document", document), ("$version", version), ("$updated", updated)));
    }

    private void AddSnapshot(Campaign c, string text, string label, DateTimeOffset now)
    {
        if (!_snapshots.TryGetValue(c.Info.Id, out var list)) _snapshots[c.Info.Id] = list = [];
        var snapshot = new Snapshot(++_lastSnapshotId, c.Info.Version, label, text, now);
        list.Add(snapshot);
        if (list.Count > MaxSnapshots) list.RemoveRange(0, list.Count - MaxSnapshots);

        var campaign = c.Info.Id;
        _writer.Write($"campaign-snapshot:{snapshot.Id}", connection =>
        {
            Run(connection,
                """
                INSERT INTO campaign_snapshots (id, campaign_id, version, label, document, created_utc)
                VALUES ($id, $campaign, $version, $label, $document, $created)
                """,
                ("$id", snapshot.Id), ("$campaign", campaign), ("$version", snapshot.Version), ("$label", label),
                ("$document", text), ("$created", Iso(now)));
            Run(connection,
                """
                DELETE FROM campaign_snapshots WHERE campaign_id = $campaign
                AND id NOT IN (SELECT id FROM campaign_snapshots WHERE campaign_id = $campaign ORDER BY id DESC LIMIT $keep)
                """,
                ("$campaign", campaign), ("$keep", MaxSnapshots));
        });
    }

    private static void Run(SqliteConnection connection, string sql, params (string Name, object Value)[] args)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in args) command.Parameters.AddWithValue(name, value);
        command.ExecuteNonQuery();
    }

    private void Load(ArcDb db)
    {
        using var connection = db.Open();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT id, code, game_id, name, owner, members_json, document, version, updated_utc FROM campaigns";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var members = JsonSerializer.Deserialize<List<CampaignMember>>(reader.GetString(5), ArcJson.Options) ?? [];
                var info = new CampaignInfo(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                    new ProfileId(reader.GetString(4)), members, reader.GetInt64(7));
                _byId[info.Id] = new Campaign(info, JsonDocument.Parse(reader.GetString(6)).RootElement.Clone(), Parse(reader.GetString(8)));
                _idByCode[info.Code] = info.Id;
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT id, campaign_id, version, label, document, created_utc FROM campaign_snapshots ORDER BY id";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var snapshot = new Snapshot(reader.GetInt64(0), reader.GetInt64(2), reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                    reader.GetString(4), Parse(reader.GetString(5)));
                _lastSnapshotId = Math.Max(_lastSnapshotId, snapshot.Id);
                var campaign = reader.GetString(1);
                if (!_snapshots.TryGetValue(campaign, out var list)) _snapshots[campaign] = list = [];
                list.Add(snapshot);
                if (list.Count > MaxSnapshots) list.RemoveAt(0);
            }
        }
    }

    private static string NewId()
    {
        Span<byte> bytes = stackalloc byte[12]; // 12 bytes encode to exactly 16 base64url characters
        RandomNumberGenerator.Fill(bytes);
        return "cm_" + Base64Url.EncodeToString(bytes);
    }

    private static string Iso(DateTimeOffset t) => t.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset Parse(string s) =>
        DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var t)
            ? t
            : DateTimeOffset.UnixEpoch;

    private sealed record Campaign(CampaignInfo Info, JsonElement Document, DateTimeOffset Updated);

    private sealed record Snapshot(long Id, long Version, string Label, string Text, DateTimeOffset Created);
}
