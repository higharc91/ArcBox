namespace ArcBox.Sdk;

/// <summary>Opt-in: receives messages from the TV. The TV reports; the game stays authoritative.</summary>
public interface IHostAware
{
    void OnHostSignal(string kind, PlayerInput payload);       // payload.Read<T>() as for player input
}
