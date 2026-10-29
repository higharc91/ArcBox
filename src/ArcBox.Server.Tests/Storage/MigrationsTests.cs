using ArcBox.Server.Storage;
using Microsoft.Data.Sqlite;

namespace ArcBox.Server.Tests.Storage;

public class MigrationsTests
{
    private static SqliteConnection OpenMemory()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        return connection;
    }

    private static long Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private static List<string> Strings(SqliteConnection connection, string sql, int column = 0)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        var list = new List<string>();
        while (reader.Read()) list.Add(reader.GetString(column));
        return list;
    }

    [Fact]
    public void Apply_on_empty_db_sets_user_version_and_creates_tables()
    {
        using var connection = OpenMemory();

        Migrations.Apply(connection);

        Assert.Equal(Migrations.Scripts.Count, Scalar(connection, "PRAGMA user_version"));
        var tables = Strings(connection, "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name");
        Assert.Equal(["campaign_snapshots", "campaigns", "profile_keys", "profiles", "rooms"], tables);
    }

    [Fact]
    public void Apply_twice_is_a_no_op()
    {
        using var connection = OpenMemory();
        Migrations.Apply(connection);
        using (var insert = connection.CreateCommand())
        {
            insert.CommandText = "INSERT INTO profiles (id, display_name, created_utc, last_seen_utc) VALUES ('pr_a', 'Ann', 't', 't')";
            insert.ExecuteNonQuery();
        }

        Migrations.Apply(connection);

        Assert.Equal(Migrations.Scripts.Count, Scalar(connection, "PRAGMA user_version"));
        Assert.Equal(1, Scalar(connection, "SELECT COUNT(*) FROM profiles"));
    }

    [Fact]
    public void Apply_refuses_a_database_from_a_newer_server()
    {
        using var connection = OpenMemory();
        using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = $"PRAGMA user_version = {Migrations.Scripts.Count + 1}";
            pragma.ExecuteNonQuery();
        }

        Assert.Throws<InvalidOperationException>(() => Migrations.Apply(connection));
    }

    [Theory]
    [InlineData("profiles", "id,display_name,created_utc,last_seen_utc")]
    [InlineData("profile_keys", "key_hash,profile_id,issued_utc,last_used_utc")]
    [InlineData("rooms", "code,host_token_hash,slots_json,next_slot_id,selected_game,campaign_id,game_running,created_utc,updated_utc")]
    [InlineData("campaigns", "id,code,game_id,name,owner,members_json,document,version,updated_utc")]
    [InlineData("campaign_snapshots", "id,campaign_id,version,label,document,created_utc")]
    public void Tables_have_the_columns_from_the_spec(string table, string columns)
    {
        using var connection = OpenMemory();
        Migrations.Apply(connection);

        Assert.Equal(columns.Split(','), Strings(connection, $"SELECT name FROM pragma_table_info('{table}') ORDER BY cid"));
    }

    [Fact]
    public void Campaign_codes_are_unique()
    {
        using var connection = OpenMemory();
        Migrations.Apply(connection);
        const string insert = "INSERT INTO campaigns (id, code, game_id, name, owner, members_json, document, version, updated_utc) VALUES ($id, 'BCDF', 'g', 'n', 'pr_a', '[]', '{}', 1, 't')";
        using (var first = connection.CreateCommand())
        {
            first.CommandText = insert;
            first.Parameters.AddWithValue("$id", "c1");
            first.ExecuteNonQuery();
        }

        using var second = connection.CreateCommand();
        second.CommandText = insert;
        second.Parameters.AddWithValue("$id", "c2");
        Assert.Throws<SqliteException>(() => second.ExecuteNonQuery());
    }
}
