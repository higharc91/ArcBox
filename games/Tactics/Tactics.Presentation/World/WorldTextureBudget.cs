using System.Collections.Immutable;

namespace Tactics.Presentation.World;

/// <summary>The owner's TV texture limit, measured with four distinct party heroes.</summary>
public static class WorldTextureBudget
{
    public const long LimitBytes = 512L * 1024 * 1024;
    public static ImmutableArray<string> Party { get; } =
        ["characters.human_fighter", "characters.human_cleric", "characters.human_rogue", "characters.human_wizard"];
    public static ImmutableArray<string> ChangedParty { get; } =
        ["characters.elf_fighter", "characters.elf_cleric", "characters.elf_rogue", "characters.elf_wizard"];

    public static bool Passes(double textureBytes, int members)
        => members == 4 && double.IsFinite(textureBytes) && textureBytes > 0 && textureBytes <= LimitBytes;
}
