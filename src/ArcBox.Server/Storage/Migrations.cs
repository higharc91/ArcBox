using Microsoft.Data.Sqlite;

namespace ArcBox.Server.Storage;

/// <summary>
/// The schema as numbered SQL scripts, tracked with <c>PRAGMA user_version</c>. Never edit a script once it has shipped;
/// add a new one at the end.
/// </summary>
public static class Migrations
{
    /// <summary>Index <c>i</c> brings the database to <c>user_version</c> <c>i + 1</c>.</summary>
    public static IReadOnlyList<string> Scripts { get; } =
    [
        // 1: every table of the platform v2 design (§4). Text holds ids, JSON and ISO-8601 UTC timestamps;
        // integers hold versions and booleans.
        """
        CREATE TABLE profiles (
            id           TEXT PRIMARY KEY NOT NULL,
            display_name TEXT NOT NULL,
            created_utc  TEXT NOT NULL,
            last_seen_utc TEXT NOT NULL
        );

        CREATE TABLE profile_keys (
            key_hash      TEXT PRIMARY KEY NOT NULL,
            profile_id    TEXT NOT NULL REFERENCES profiles (id) ON DELETE CASCADE,
            issued_utc    TEXT NOT NULL,
            last_used_utc TEXT NOT NULL
        );
        CREATE INDEX ix_profile_keys_profile ON profile_keys (profile_id);

        CREATE TABLE rooms (
            code            TEXT PRIMARY KEY NOT NULL,
            host_token_hash TEXT NOT NULL,
            slots_json      TEXT NOT NULL,
            next_slot_id    INTEGER NOT NULL,
            selected_game   TEXT,
            campaign_id     TEXT,
            game_running    INTEGER NOT NULL DEFAULT 0,
            created_utc     TEXT NOT NULL,
            updated_utc     TEXT NOT NULL
        );

        CREATE TABLE campaigns (
            id           TEXT PRIMARY KEY NOT NULL,
            code         TEXT NOT NULL UNIQUE,
            game_id      TEXT NOT NULL,
            name         TEXT NOT NULL,
            owner        TEXT NOT NULL,
            members_json TEXT NOT NULL,
            document     TEXT NOT NULL,
            version      INTEGER NOT NULL,
            updated_utc  TEXT NOT NULL
        );

        CREATE TABLE campaign_snapshots (
            id          INTEGER PRIMARY KEY AUTOINCREMENT,
            campaign_id TEXT NOT NULL REFERENCES campaigns (id) ON DELETE CASCADE,
            version     INTEGER NOT NULL,
            label       TEXT,
            document    TEXT NOT NULL,
            created_utc TEXT NOT NULL
        );
        CREATE INDEX ix_campaign_snapshots_campaign ON campaign_snapshots (campaign_id, version);
        """,
    ];

    /// <summary>Runs every script the database hasn't had yet, each in its own transaction. Safe to call repeatedly.</summary>
    /// <exception cref="InvalidOperationException">The database was written by a newer server.</exception>
    public static void Apply(SqliteConnection connection)
    {
        var current = UserVersion(connection);
        if (current > Scripts.Count)
            throw new InvalidOperationException(
                $"The database is at schema version {current}, but this server only knows {Scripts.Count}. Run a newer server.");

        for (var version = current + 1; version <= Scripts.Count; version++)
        {
            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            // PRAGMA takes no parameters; the version is our own integer.
            command.CommandText = Scripts[version - 1] + $"\nPRAGMA user_version = {version};";
            command.ExecuteNonQuery();
            transaction.Commit();
        }
    }

    private static int UserVersion(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version";
        return Convert.ToInt32(command.ExecuteScalar());
    }
}
