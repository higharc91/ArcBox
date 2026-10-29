namespace ArcBox.Host.Shell;

/// <summary>
/// A scene that tells its game something the TV saw (spec §3.7), e.g. that an animation chain finished:
/// <c>Signal("done", new { seq = 41 })</c>. The shell sets <see cref="Signal"/> when it creates the scene (also when it
/// warms up behind the lobby). Calls are fire-and-forget: they go through <c>HostClient.SignalAsync</c> to games that
/// implement <c>IHostAware</c>, and failures (no connection, "Invalid signal.") are only logged. A signal may only
/// shorten a wait the game already has a fallback timer for, and should carry the sequence number it acknowledges.
/// </summary>
public interface ISignalingScene
{
    /// <summary><c>(kind, payload)</c>: kind is 1–32 of <c>[A-Za-z0-9_.-]</c>; the payload is serialized to at most 4096 bytes of JSON.</summary>
    Action<string, object> Signal { set; }
}
