namespace ArcBox.Server.Storage;

/// <summary>Settings for the SQLite store; binds from <c>Storage</c>.</summary>
public sealed class StorageOptions
{
    public const string Section = "Storage";

    /// <summary>The database file. Relative paths resolve against the content root.</summary>
    public string Path { get; set; } = "arcbox.db";

    /// <summary>
    /// A full SQLite connection string, which overrides <see cref="Path"/>. Tests use a shared-cache in-memory database
    /// (<c>Data Source=file:&lt;guid&gt;?mode=memory&amp;cache=shared</c>).
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>Where daily backups go. Relative paths resolve against the content root.</summary>
    public string BackupDirectory { get; set; } = "backups";

    /// <summary>How many daily backups to keep.</summary>
    public int BackupDays { get; set; } = 14;
}
