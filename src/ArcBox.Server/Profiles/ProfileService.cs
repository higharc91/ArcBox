using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using ArcBox.Sdk;
using ArcBox.Sdk.Protocol;
using ArcBox.Server.Rooms;
using ArcBox.Server.Storage;
using Microsoft.Data.Sqlite;

namespace ArcBox.Server.Profiles;

public sealed record ProfileRecord(ProfileId Id, string DisplayName);

/// <summary>
/// People across rooms and campaigns, and the per-device keys that identify them (spec §3.1). Everything is held in
/// memory, loaded when the service is built, so a room can resolve a key without touching the disk; changes are queued
/// on the <see cref="StorageWriter"/>, never written on the caller's thread. A key is stored only as its
/// <see cref="Secrets.Hash"/>; the plaintext exists in the <see cref="ProfileKey"/> that issues it and nowhere else.
/// </summary>
public sealed class ProfileService
{
    /// <summary>Keys kept per profile; issuing another prunes the oldest.</summary>
    public const int MaxKeysPerProfile = 10;

    private readonly StorageWriter _writer;
    private readonly TimeProvider _time;
    private readonly object _lock = new();
    private readonly Dictionary<ProfileId, Profile> _profiles = [];
    private readonly Dictionary<string, KeyEntry> _keys = new(StringComparer.Ordinal); // by key hash
    private long _stamp; // breaks ties between keys issued or used within the same clock tick

    public ProfileService(ArcDb db, StorageWriter writer, [FromKeyedServices(RoomManager.ClockKey)] TimeProvider time)
    {
        _writer = writer;
        _time = time;
        Load(db);
    }

    public ProfileRecord Create(string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        var now = _time.GetUtcNow();
        Profile profile;
        lock (_lock)
        {
            profile = new Profile(NewId(), displayName.Trim(), now) { LastSeen = now };
            _profiles[profile.Id] = profile;
            QueueProfile(profile);
        }

        return profile.Record;
    }

    public ProfileRecord? Find(ProfileId id)
    {
        lock (_lock) return _profiles.TryGetValue(id, out var profile) ? profile.Record : null;
    }

    /// <summary>A new key for <paramref name="id"/>; the profile keeps its newest <see cref="MaxKeysPerProfile"/>.</summary>
    /// <exception cref="InvalidOperationException">No such profile.</exception>
    public ProfileKey IssueKey(ProfileId id)
    {
        var key = Secrets.NewToken();
        var now = _time.GetUtcNow();
        lock (_lock)
        {
            if (!_profiles.TryGetValue(id, out var profile))
                throw new InvalidOperationException($"No profile {id}.");

            var entry = new KeyEntry(Secrets.Hash(key), id, now) { LastUsed = now, Stamp = ++_stamp };
            _keys[entry.Hash] = entry;
            QueueKey(entry);

            var surplus = _keys.Values.Where(k => k.Profile == id).OrderBy(k => k.Stamp).SkipLast(MaxKeysPerProfile).ToList();
            foreach (var old in surplus)
            {
                _keys.Remove(old.Hash);
                QueueKeyDelete(old.Hash);
            }

            return new ProfileKey(id, key, profile.DisplayName);
        }
    }

    /// <summary>The profile a key belongs to, or null. Counts as a use of the key.</summary>
    public ProfileRecord? Resolve(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        var hash = Secrets.Hash(key);
        var now = _time.GetUtcNow();
        lock (_lock)
        {
            if (!_keys.TryGetValue(hash, out var entry)) return null;
            var profile = _profiles[entry.Profile];
            entry.LastUsed = now;
            entry.Stamp = ++_stamp;
            profile.LastSeen = now;
            QueueProfile(profile);
            QueueKey(entry);
            return profile.Record;
        }
    }

    /// <summary>Profiles for the given keys, most recently used first. Doesn't count as a use, so a peek changes nothing.</summary>
    public IReadOnlyList<ProfileRecord> ResolveAll(IEnumerable<string> keys)
    {
        var hashes = keys.Where(k => !string.IsNullOrEmpty(k)).Select(Secrets.Hash).Distinct().ToList();
        lock (_lock)
        {
            return hashes
                .Select(h => _keys.GetValueOrDefault(h))
                .OfType<KeyEntry>()
                .OrderByDescending(k => k.LastUsed).ThenByDescending(k => k.Stamp)
                .Select(k => k.Profile)
                .Distinct()
                .Select(id => _profiles[id].Record)
                .ToList();
        }
    }

    /// <summary>Revokes every key of <paramref name="id"/>; the profile itself stays.</summary>
    public void ForgetKeys(ProfileId id)
    {
        lock (_lock)
        {
            foreach (var entry in _keys.Values.Where(k => k.Profile == id).ToList())
            {
                _keys.Remove(entry.Hash);
                QueueKeyDelete(entry.Hash);
            }
        }
    }

    // Each queued operation carries the full state of its row and is keyed by it, so the writer's coalescing is safe
    // and a profile (queued first) is always written before the keys that reference it.
    private void QueueProfile(Profile p)
    {
        var (id, name, created, seen) = (p.Id.Value, p.DisplayName, Iso(p.Created), Iso(p.LastSeen));
        _writer.Write($"profile:{id}", c => Run(c,
            """
            INSERT INTO profiles (id, display_name, created_utc, last_seen_utc) VALUES ($id, $name, $created, $seen)
            ON CONFLICT (id) DO UPDATE SET display_name = $name, last_seen_utc = $seen
            """,
            ("$id", id), ("$name", name), ("$created", created), ("$seen", seen)));
    }

    private void QueueKey(KeyEntry k)
    {
        var (hash, profile, issued, used) = (k.Hash, k.Profile.Value, Iso(k.Issued), Iso(k.LastUsed));
        _writer.Write($"profile-key:{hash}", c => Run(c,
            """
            INSERT INTO profile_keys (key_hash, profile_id, issued_utc, last_used_utc) VALUES ($hash, $profile, $issued, $used)
            ON CONFLICT (key_hash) DO UPDATE SET last_used_utc = $used
            """,
            ("$hash", hash), ("$profile", profile), ("$issued", issued), ("$used", used)));
    }

    private void QueueKeyDelete(string hash) =>
        _writer.Write($"profile-key:{hash}", c => Run(c, "DELETE FROM profile_keys WHERE key_hash = $hash", ("$hash", hash)));

    private static void Run(SqliteConnection connection, string sql, params (string Name, string Value)[] args)
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
            command.CommandText = "SELECT id, display_name, created_utc, last_seen_utc FROM profiles";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var id = new ProfileId(reader.GetString(0));
                _profiles[id] = new Profile(id, reader.GetString(1), Parse(reader.GetString(2))) { LastSeen = Parse(reader.GetString(3)) };
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT key_hash, profile_id, issued_utc, last_used_utc FROM profile_keys ORDER BY issued_utc, rowid";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var entry = new KeyEntry(reader.GetString(0), new ProfileId(reader.GetString(1)), Parse(reader.GetString(2)))
                {
                    LastUsed = Parse(reader.GetString(3)),
                    Stamp = ++_stamp,
                };
                _keys[entry.Hash] = entry;
            }
        }
    }

    private static ProfileId NewId()
    {
        Span<byte> bytes = stackalloc byte[12]; // 12 bytes encode to exactly 16 base64url characters
        RandomNumberGenerator.Fill(bytes);
        return new ProfileId("pr_" + Base64Url.EncodeToString(bytes));
    }

    private static string Iso(DateTimeOffset t) => t.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);

    // A row with an unreadable timestamp must not stop the server from starting; it only costs the key its place in the order.
    private static DateTimeOffset Parse(string s) =>
        DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var t)
            ? t
            : DateTimeOffset.UnixEpoch;

    private sealed class Profile(ProfileId id, string displayName, DateTimeOffset created)
    {
        public ProfileId Id { get; } = id;
        public string DisplayName { get; } = displayName;
        public DateTimeOffset Created { get; } = created;
        public DateTimeOffset LastSeen { get; set; }
        public ProfileRecord Record { get; } = new(id, displayName);
    }

    private sealed class KeyEntry(string hash, ProfileId profile, DateTimeOffset issued)
    {
        public string Hash { get; } = hash;
        public ProfileId Profile { get; } = profile;
        public DateTimeOffset Issued { get; } = issued;
        public DateTimeOffset LastUsed { get; set; }
        public long Stamp { get; set; }
    }
}
