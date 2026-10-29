using System.Globalization;
using System.Text.Json;
using ArcBox.Sdk;
using ArcBox.Server.Storage;
using Microsoft.Data.Sqlite;

namespace ArcBox.Server.Rooms;

/// <summary>
/// The <c>rooms</c> table (spec §3.5). Writes are queued on the <see cref="StorageWriter"/> under <c>room:{code}</c>, so
/// the newest save or delete of a room wins; reads go to the database directly.
/// </summary>
/// <remarks>
/// <para>
/// A queued write isn't in the database yet, and a room can be looked up straight after it is saved (a room parked
/// a moment ago, or created less than a flush ago). So the store also remembers the newest record, or the deletion, it
/// has queued for each code, and <see cref="Load"/> and <see cref="Exists"/> answer from that before the database.
/// <see cref="DeleteOlderThan"/> forgets those entries with the rows, so they never outlive <c>RetainFor</c> by much.
/// </para>
/// <para>
/// <c>slots_json</c> is the array of seats, <c>[{id, name, secretHash, joinOrder, profile}]</c>. A parked room's seats
/// are wrapped as <c>{"slots": [...], "parked": true}</c>, which needs no new column.
/// </para>
/// </remarks>
public sealed class RoomStore
{
    private readonly ArcDb _db;
    private readonly StorageWriter _writer;
    private readonly TimeProvider _time;

    private readonly Lock _lock = new();
    private readonly Dictionary<string, Queued> _queued = new(StringComparer.Ordinal);

    public RoomStore(ArcDb db, StorageWriter writer, [FromKeyedServices(RoomManager.ClockKey)] TimeProvider time)
    {
        _db = db;
        _writer = writer;
        _time = time;
    }

    /// <summary>Queues the whole record; a newer save or delete of the same room replaces it while it waits.</summary>
    public void Save(RoomRecord record)
    {
        var (code, hostHash, slots, nextSlot) = (record.Code, record.HostTokenHash, SlotsJson(record), record.NextSlotId);
        var (game, campaign, running) = (record.SelectedGame, record.CampaignId, record.GameRunning ? 1 : 0);
        var (created, updated) = (Iso(record.CreatedUtc), Iso(record.UpdatedUtc));

        // Under the lock, so the remembered record and the writer's queue agree on which save is the newest.
        lock (_lock)
        {
            _queued[code] = new Queued(record, record.UpdatedUtc);
            _writer.Write(Key(code), connection => Run(connection,
                """
                INSERT INTO rooms (code, host_token_hash, slots_json, next_slot_id, selected_game, campaign_id, game_running, created_utc, updated_utc)
                VALUES ($code, $host, $slots, $next, $game, $campaign, $running, $created, $updated)
                ON CONFLICT (code) DO UPDATE SET host_token_hash = $host, slots_json = $slots, next_slot_id = $next,
                    selected_game = $game, campaign_id = $campaign, game_running = $running, updated_utc = $updated
                """,
                ("$code", code), ("$host", hostHash), ("$slots", slots), ("$next", nextSlot), ("$game", game),
                ("$campaign", campaign), ("$running", running), ("$created", created), ("$updated", updated)));
        }
    }

    /// <summary>The room's newest record, whatever its age, or null.</summary>
    public RoomRecord? Load(string code)
    {
        lock (_lock)
        {
            if (_queued.TryGetValue(code, out var queued))
            {
                return queued.Record;
            }
        }

        using var connection = _db.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT code, host_token_hash, slots_json, next_slot_id, selected_game, campaign_id, game_running, created_utc, updated_utc
            FROM rooms WHERE code = $code
            """;
        command.Parameters.AddWithValue("$code", code);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        var (slots, parked) = ParseSlots(reader.GetString(2));
        return new RoomRecord(reader.GetString(0), reader.GetString(1), slots, reader.GetInt32(3),
            reader.IsDBNull(4) ? null : reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.GetInt64(6) != 0, Parse(reader.GetString(7)), Parse(reader.GetString(8)), parked);
    }

    /// <summary>Queues the room's deletion; a save still waiting for the same room is dropped.</summary>
    public void Delete(string code)
    {
        lock (_lock)
        {
            _queued[code] = new Queued(null, _time.GetUtcNow());
            _writer.Write(Key(code), connection => Run(connection, "DELETE FROM rooms WHERE code = $code", ("$code", code)));
        }
    }

    /// <summary>Whether a room with this code is stored, however old. New room codes skip these.</summary>
    public bool Exists(string code) => Load(code) is not null;

    /// <summary>
    /// Deletes every room last saved before <paramref name="cutoff"/> and returns how many rows went. A maintenance
    /// sweep, so it deletes on the caller's thread rather than waiting for the writer, and then queues the same delete
    /// behind any save still waiting there, which would otherwise write an old row back.
    /// </summary>
    public int DeleteOlderThan(DateTimeOffset cutoff)
    {
        var before = Iso(cutoff);
        lock (_lock)
        {
            foreach (var (code, queued) in _queued.Where(q => q.Value.At < cutoff).ToList())
            {
                _queued.Remove(code);
            }

            _writer.Write("rooms:older-than", connection =>
                Run(connection, "DELETE FROM rooms WHERE updated_utc < $cutoff", ("$cutoff", before)));
        }

        using var direct = _db.Open();
        return Run(direct, "DELETE FROM rooms WHERE updated_utc < $cutoff", ("$cutoff", before));
    }

    private static string Key(string code) => $"room:{code}";

    private static string SlotsJson(RoomRecord record) => record.Parked
        ? JsonSerializer.Serialize(new ParkedSlots(record.Slots, Parked: true), ArcJson.Options)
        : JsonSerializer.Serialize(record.Slots, ArcJson.Options);

    private static (IReadOnlyList<SlotRecord> Slots, bool Parked) ParseSlots(string json)
    {
        if (json.TrimStart().StartsWith('{'))
        {
            var parked = JsonSerializer.Deserialize<ParkedSlots>(json, ArcJson.Options)!;
            return (parked.Slots ?? [], parked.Parked);
        }

        return (JsonSerializer.Deserialize<List<SlotRecord>>(json, ArcJson.Options) ?? [], false);
    }

    private static int Run(SqliteConnection connection, string sql, params (string Name, object? Value)[] args)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in args)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }
        return command.ExecuteNonQuery();
    }

    // The format sorts as text, which DeleteOlderThan relies on.
    private static string Iso(DateTimeOffset t) => t.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset Parse(string s) =>
        DateTimeOffset.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

    /// <summary>The newest record queued for a code (null once deleted), and when, for forgetting it.</summary>
    private sealed record Queued(RoomRecord? Record, DateTimeOffset At);

    private sealed record ParkedSlots(IReadOnlyList<SlotRecord>? Slots, bool Parked);
}
