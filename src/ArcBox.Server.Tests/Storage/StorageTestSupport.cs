using ArcBox.Server.Storage;
using Microsoft.Extensions.Options;

namespace ArcBox.Server.Tests.Storage;

internal static class StorageTestSupport
{
    /// <summary>A private in-memory database that lives until the returned <see cref="ArcDb"/> is disposed.</summary>
    public static ArcDb NewMemoryDb() => new(Options.Create(new StorageOptions
    {
        ConnectionString = $"Data Source=file:{Guid.NewGuid():N}?mode=memory&cache=shared",
    }));

    public static StorageOptions FileOptions(string directory) => new()
    {
        Path = Path.Combine(directory, "arcbox.db"),
        BackupDirectory = Path.Combine(directory, "backups"),
    };

    public static ArcDb NewDb(StorageOptions options) => new(Options.Create(options));

    public static void Execute(this ArcDb db, string sql)
    {
        using var connection = db.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    public static T Scalar<T>(this ArcDb db, string sql)
    {
        using var connection = db.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (T)Convert.ChangeType(command.ExecuteScalar()!, typeof(T));
    }
}
