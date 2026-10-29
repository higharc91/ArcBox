namespace ArcBox.Server.Content;

/// <summary>Where game content packs live; binds from <c>Content</c>.</summary>
public sealed class ContentOptions
{
    public const string Section = "Content";

    /// <summary>
    /// The directory holding <c>&lt;gameId&gt;/&lt;packId&gt;/…</c>. Relative paths resolve against the content root. A
    /// directory that doesn't exist is an empty registry, not an error.
    /// </summary>
    public string Root { get; set; } = "content";
}
