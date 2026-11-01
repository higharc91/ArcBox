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

    /// <summary>How long the opening fade takes, in seconds. Everything that can't wait a frame's budget happens behind it.</summary>
    public const double FadeSeconds = 0.4;

    // ---- The playhead (WalkTrack) --------------------------------------------------------------------------------------

    /// <summary>The jitter buffer, in seconds of path: how far behind the server the party is drawn once it has got going.</summary>
    public const double TargetLag = 0.08;

    /// <summary>The slowest the playhead runs while walking, as a share of the server's speed. Only the stop's settle goes below it.</summary>
    public const double RateLeast = 0.75;

    /// <summary>The fastest the playhead ever runs, as a share of the server's speed.</summary>
    public const double RateMost = 2;

    /// <summary>How hard the rate leans on lag: rate = 1 + gain × (lag − <see cref="TargetLag"/>).</summary>
    public const double RateGain = 2;

    /// <summary>Lag over this jumps the playhead along the path, in seconds.</summary>
    public const double SnapLag = 1.2;

    /// <summary>The speed a walk starts at, so that the first step shows on the very next frame.</summary>
    public const double StartSpeed = 0.5;

    /// <summary>How long the speed takes to go from nothing to the server's, in seconds. It also smooths the rate's wobble.</summary>
    public const double RateRamp = 0.15;

    /// <summary>How long a stop takes to brake from walking pace, in seconds: the settle.</summary>
    public const double StopSettle = 0.2;

    /// <summary>The same, with a direction held and the path running out. Short, so that the jitter buffer doesn't touch it.</summary>
    public const double HeldSettle = 0.1;

    /// <summary>The least the brake lets the speed fall to, so a stop arrives.</summary>
    public const double BrakeLeast = 0.15;

    /// <summary>How long the walk animation runs with a direction held and no path, in seconds: over the server's lease, so only a dead server ends it.</summary>
    public const double HeldPatience = 1;

    /// <summary>How far from a path's corner the rounding starts, in tiles. Under half a tile, so it stays on the corner's tile.</summary>
    public const float CornerRadius = 0.25f;

    /// <summary>The seconds of walk animation one tile plays: the feet are tied to distance, so they can't slide.</summary>
    public const double StrideSeconds = 0.3;

    /// <summary>A step down of more than this many height steps is a drop, not a step: the same number that starts adding time on the server.</summary>
    public const float LongDrop = 2;

    /// <summary>The share of a drop's leg walked before the feet leave the ground: the tile's edge.</summary>
    public const float DropEdge = 0.5f;

    /// <summary>The tiles between one foot coming down and the next: half of the walk's stride, which is 0.667 s of animation.</summary>
    public const double FootstepTiles = 1.11;

    /// <summary>How far under a battle's footsteps walking's are, in dB: a battle's are a few at a time, and these are heard for hours.</summary>
    public const float FootstepDb = -8;

    // ---- Followers ------------------------------------------------------------------------------------------------------

    /// <summary>Between one walker and the next, in tiles along the path.</summary>
    public const float Spacing = 0.9f;

    /// <summary>The most path a corner can use up between two walkers, in tiles: <see cref="Spacing"/> × √2 is a right angle's.</summary>
    public const float SpacingReach = 1.3f;

    /// <summary>How briskly followers fan out at a stop, as a critically damped spring's rate per second.</summary>
    public const float FanRate = 4.5f;

    /// <summary>How briskly they come back to the trail when the party moves off.</summary>
    public const float RegroupRate = 7;

    /// <summary>The fastest a follower goes along the trail, in tiles a second: quicker than the leader, to catch up, and enough for a corner's chord.</summary>
    public const double FollowerMost = 5;

    /// <summary>How fast a follower picks up pace along the trail, in tiles a second a second.</summary>
    public const double FollowerPick = 15;

    /// <summary>The fastest a follower goes off the trail, in tiles a second, however far it has to go.</summary>
    public const float FanMost = 4;

    /// <summary>Under this many tiles a second a follower is standing, not walking.</summary>
    public const float StandingSpeed = 0.2f;

    /// <summary>How many steps away from the leader a formation's tile can be.</summary>
    public const int FanReach = 4;

    /// <summary>How far a follower stands from the middle of its tile, at most, in tiles, so the formation doesn't look ruled.</summary>
    public const float FanScatter = 0.14f;

    // ---- The camera (WorldCamera) ---------------------------------------------------------------------------------------

    /// <summary>The follow's time constant, in seconds.</summary>
    public const double CameraTau = 0.12;

    /// <summary>The follow's time constant across a sustained walk's line, in seconds. A stairstep road swings from side to side a tile at a time, and this keeps it out of the picture.</summary>
    public const double CameraRoadTau = 0.8;

    /// <summary>The furthest the camera looks ahead, in tiles.</summary>
    public const float LookAhead = 1.5f;

    /// <summary>The look-ahead's time constant, in seconds.</summary>
    public const double LookTau = 0.5;

    /// <summary>The height's time constant, in seconds.</summary>
    public const double HeightTau = 0.3;

    /// <summary>The camera keeps this many tiles from each edge of the region.</summary>
    public const float EdgeMargin = 16;

    /// <summary>
    /// The farthest the party gets from the middle of the view, in tiles along either of the screen's axes: about 770 px across and 380 down at scale 2, clear of the
    /// panels. It outranks <see cref="EdgeMargin"/>. A tile is 128 layout pixels wide, so the margin alone would let the party walk off the screen at any edge of a region.
    /// </summary>
    public const float PartyReach = 12;

    /// <summary>How many tiles either side of the margin the camera eases over.</summary>
    public const float EdgeEase = 3;

    /// <summary>How long the clamp takes to come back after a <see cref="WorldCamera.Hold"/>, in seconds.</summary>
    public const double EdgeReturnTau = 0.5;

    /// <summary>The party's heading is the line over this many tiles of path: even, so a two-step stairstep cancels out.</summary>
    public const float HeadingChord = 4;

    /// <summary>Tiles walked since a rest before the heading starts to count, so a tap is nothing.</summary>
    public const float HeadingFrom = 1.5f;

    /// <summary>Tiles walked since a rest by which the heading counts in full.</summary>
    public const float HeadingFull = 3;
}
