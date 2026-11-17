using System.Globalization;
using System.Text;
using System.Text.Json;
using ArcBox.Server.Storage;
using Microsoft.Data.Sqlite;

namespace ArcBox.Server.Rooms;

/// <summary>
/// The <c>room_game_state</c> table: each room's newest game snapshot (<see cref="ArcBox.Sdk.ISnapshotGame"/>; Meridian
/// spec §5.6). Like <see cref="RoomStore"/>, writes queue on the <see cref="StorageWriter"/> under one key per room, so the
/// newest save or delete wins, and the newest queued entry answers reads before the database does. The store never
/// reads inside a snapshot.
/// </summary>
public sealed class GameStateStore
{
    /// <summary>The largest snapshot stored, in UTF-8 bytes. A larger one isn't: the room keeps its previous one.</summary>
    public const int MaxBytes = 256 * 1024;

    private readonly ArcDb _db;
    private readonly StorageWriter _writer;
    private readonly TimeProvider _time;
    private readonly ILogger<GameStateStore> _log;

    private readonly Lock _lock = new();
    // The newest write queued for each room and not yet run: a snapshot, or null for a deletion.
    private readonly Dictionary<string, Queued> _queued = new(StringComparer.Ordinal);

    public GameStateStore(ArcDb db, StorageWriter writer, [FromKeyedServices(RoomManager.ClockKey)] TimeProvider time,
                          ILogger<GameStateStore> log)
    {
        _db = db;
        _writer = writer;
        _time = time;
        _log = log;
    }

    /// <summary>Queues the room's snapshot. Returns false, with a warning, if it is over <see cref="MaxBytes"/>.</summary>
    public bool Save(string code, string gameId, JsonElement snapshot)
    {
        var json = snapshot.GetRawText();
        var bytes = Encoding.UTF8.GetByteCount(json);
        if (bytes > MaxBytes)
        {
            _log.LogWarning("Room {Code}: a {GameId} snapshot of {Bytes} bytes is over {Max}; the previous one stands",
                            code, gameId, bytes, MaxBytes);
            return false;
        }

        var saved = Iso(_time.GetUtcNow());
        lock (_lock)
        {
            var queued = new Queued(gameId, json);
            _queued[code] = queued;
            _writer.Write(Key(code), connection =>
            {
                Run(connection,
                    """
                    INSERT INTO room_game_state (code, game_id, snapshot, saved_utc) VALUES ($code, $game, $snapshot, $saved)
                    ON CONFLICT (code) DO UPDATE SET game_id = $game, snapshot = $snapshot, saved_utc = $saved
                    """,
                    ("$code", code), ("$game", gameId), ("$snapshot", json), ("$saved", saved));
                Written(code, queued);
            });
        }
        return true;
    }

    /// <summary>The room's newest snapshot for <paramref name="gameId"/>, or null (none, deleted, or another game's).</summary>
    public JsonElement? Load(string code, string gameId)
    {
        lock (_lock)
        {
            if (_queued.TryGetValue(code, out var queued))
            {
                return queued.GameId == gameId && queued.Json is { } json ? Parse(json) : null;
            }
        }

        using var connection = _db.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT game_id, snapshot FROM room_game_state WHERE code = $code";
        command.Parameters.AddWithValue("$code", code);
        using var reader = command.ExecuteReader();
        return reader.Read() && reader.GetString(0) == gameId ? Parse(reader.GetString(1)) : null;
    }

    /// <summary>Queues the room's snapshot's deletion; a save still waiting is dropped.</summary>
    public void Delete(string code)
    {
        lock (_lock)
        {
            var queued = new Queued(null, null);
            _queued[code] = queued;
            _writer.Write(Key(code), connection =>
            {
                Run(connection, "DELETE FROM room_game_state WHERE code = $code", ("$code", code));
                Written(code, queued);
            });
        }
    }

    /// <summary>Deletes snapshots whose room is gone (after <see cref="RoomStore.DeleteOlderThan"/>). Returns how many.</summary>
    public int DeleteOrphans()
    {
        const string Sql = "DELETE FROM room_game_state WHERE code NOT IN (SELECT code FROM rooms)";
        _writer.Write("game:orphans", connection => Run(connection, Sql));
        using var direct = _db.Open();
        return Run(direct, Sql);
    }

    /// <summary>The database has the write: forget it, unless a newer one has been queued since.</summary>
    private void Written(string code, Queued write)
    {
        lock (_lock)
        {
            if (_queued.TryGetValue(code, out var newest) && ReferenceEquals(newest, write))
            {
                _queued.Remove(code);
            }
        }
    }

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static string Key(string code) => $"game:{code}";

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

    private static string Iso(DateTimeOffset t) => t.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);

    private sealed record Queued(string? GameId, string? Json);
}
