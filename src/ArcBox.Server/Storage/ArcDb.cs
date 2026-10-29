using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace ArcBox.Server.Storage;

/// <summary>
/// The SQLite database. Applies <see cref="Migrations"/> when constructed and hands out connections; there is no
/// connection pool, so each <see cref="Open"/> is its own connection and closing it really closes it.
/// </summary>
/// <remarks>
/// An in-memory database lives only while some connection to it is open, so <see cref="ArcDb"/> keeps one open until it
/// is disposed. That ties the database's lifetime to the service container's, which is what lets a test build a second
/// server on the same database while the first one is still around.
/// </remarks>
public sealed class ArcDb : IDisposable
{
    private readonly string _connectionString;
    private readonly SqliteConnection? _keepAlive;

    public ArcDb(IOptions<StorageOptions> options, IHostEnvironment? environment = null)
    {
        var o = options.Value;
        var builder = string.IsNullOrWhiteSpace(o.ConnectionString)
            ? new SqliteConnectionStringBuilder { DataSource = FilePath(o, environment) }
            : new SqliteConnectionStringBuilder(o.ConnectionString);
        builder.Pooling = false;
        _connectionString = builder.ConnectionString;

        IsInMemory = builder.Mode == SqliteOpenMode.Memory
            || builder.DataSource.Equals(":memory:", StringComparison.OrdinalIgnoreCase)
            || builder.DataSource.Contains("mode=memory", StringComparison.OrdinalIgnoreCase);

        if (!IsInMemory && Path.GetDirectoryName(builder.DataSource) is { Length: > 0 } directory)
            Directory.CreateDirectory(directory);

        var connection = Open();
        try
        {
            Migrations.Apply(connection);
        }
        catch
        {
            connection.Dispose();
            throw;
        }

        if (IsInMemory) _keepAlive = connection;
        else connection.Dispose();
    }

    /// <summary>True for an in-memory database, which has no file to back up.</summary>
    public bool IsInMemory { get; }

    /// <summary>A new open connection with foreign keys on, and write-ahead logging for a file database. Dispose it.</summary>
    public SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        try
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = IsInMemory
                ? "PRAGMA foreign_keys = ON;"
                : "PRAGMA foreign_keys = ON; PRAGMA journal_mode = WAL;";
            command.ExecuteNonQuery();
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    public void Dispose() => _keepAlive?.Dispose();

    /// <summary>Resolves a configured path against the content root, as the speech cache does.</summary>
    internal static string ResolvePath(string path, IHostEnvironment? environment) =>
        Path.GetFullPath(Path.Combine(environment?.ContentRootPath ?? AppContext.BaseDirectory, path));

    private static string FilePath(StorageOptions o, IHostEnvironment? environment) => ResolvePath(o.Path, environment);
}
