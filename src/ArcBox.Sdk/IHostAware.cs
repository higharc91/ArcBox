namespace ArcBox.Sdk;

/// <summary>Opt-in: receives messages from the TV. The TV reports; the game stays authoritative.</summary>
public interface IHostAware
{
    /// <summary>A signal the TV sent; <paramref name="payload"/> is read with <c>Read&lt;T&gt;()</c>, as for player input.</summary>
    /// <param name="kind">The game's own name for it, 1–32 of <c>A–Z a–z 0–9 _ . -</c>.</param>
    void OnHostSignal(string kind, PlayerInput payload);
}
