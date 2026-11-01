namespace Tactics.Presentation.World;

/// <summary>The world's numbers that only a person, or a measurement, can judge.</summary>
public static class WorldTuning
{
    /// <summary>The tiles along one side of a block, the unit the TV draws in. The data's unit stays the 32-tile chunk.</summary>
    public const int BlockSize = 16;

    /// <summary>How long a frame may spend recording blocks, in milliseconds.</summary>
    public const double BlockBudgetMs = 4;

    /// <summary>Layout pixels per art pixel: fixed, so a region isn't shrunk to fit as a board is.</summary>
    public const int Scale = 2;

    /// <summary>How far ahead of the party blocks are queued, in seconds of its walking.</summary>
    public const float QueueAhead = 2;
}
