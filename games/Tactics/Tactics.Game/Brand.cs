namespace Tactics.Game;

/// <summary>
/// The game's player-facing name. The code, ids and content paths keep the working name "tactics": renaming them would
/// break saved campaigns and cached content packs for no player benefit. Everything players see takes its name from here.
/// </summary>
public static class Brand
{
    /// <summary>The game's title, shown on the lobby card and the TV.</summary>
    public const string Name = "Wardenflame";

    /// <summary>One line under the title on the lobby card.</summary>
    public const string Tagline = "Co-op tactics to keep the last fire burning, week after week";
}
