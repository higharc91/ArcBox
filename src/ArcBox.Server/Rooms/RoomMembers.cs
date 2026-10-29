using ArcBox.Sdk;
using ArcBox.Sdk.Protocol;

namespace ArcBox.Server.Rooms;

public sealed class RoomMembers(RoomOptions options)
{
    private readonly List<PlayerSlot> _slots = [];
    private int _nextId = 1;

    public IReadOnlyList<PlayerSlot> All => _slots;

    public PlayerId? VipId => _slots.FirstOrDefault(s => s.Connected)?.Id;

    /// <summary>The seats nobody is sitting in, in join order.</summary>
    public IEnumerable<PlayerSlot> Offline => _slots.Where(s => !s.Connected);

    public bool AnyConnected => _slots.Any(s => s.Connected);

    public bool TryAdd(string rawName, string connectionId, out PlayerSlot? slot, out string? secret, out string? error)
    {
        error = CheckNew(rawName, out var name);
        if (error is not null)
        {
            slot = null;
            secret = null;
            return false;
        }

        slot = Add(name, connectionId, profile: null, out secret);
        return true;
    }

    /// <summary>
    /// Why a new seat named <paramref name="rawName"/> can't be added, or null; <paramref name="name"/> is it trimmed.
    /// <paramref name="replacing"/> is a seat that goes when the new one is added, so it counts against neither the name
    /// nor the room size.
    /// </summary>
    public string? CheckNew(string rawName, out string name, PlayerSlot? replacing = null)
    {
        if (CheckName(rawName, out name, replacing) is { } error)
        {
            return error;
        }

        if (_slots.Count(s => s != replacing) >= options.MaxPlayers)
        {
            return "Room is full.";
        }

        return null;
    }

    /// <summary>
    /// Why a seat can't be called <paramref name="rawName"/>, or null; <paramref name="name"/> is it trimmed. The name
    /// rule and no other seat having it, except the seats in <paramref name="except"/> (the one being renamed, one that goes).
    /// </summary>
    public string? CheckName(string rawName, out string name, params PlayerSlot?[] except)
    {
        name = rawName?.Trim() ?? string.Empty;

        if (name.Length < 1 || name.Length > options.NameMaxLength)
        {
            return $"Name must be 1–{options.NameMaxLength} characters.";
        }

        var trimmed = name;
        if (_slots.Any(s => !except.Contains(s) && string.Equals(s.Name, trimmed, StringComparison.OrdinalIgnoreCase)))
        {
            return "That name is taken.";
        }

        return null;
    }

    /// <summary>
    /// Adds a seat without checking the name or the room size (<see cref="CheckNew"/> does that). With a null
    /// <paramref name="connectionId"/> the seat starts offline.
    /// </summary>
    public PlayerSlot Add(string name, string? connectionId, ProfileId? profile, out string secret)
    {
        secret = Secrets.NewToken();
        var slot = new PlayerSlot
        {
            Id = new PlayerId($"p{_nextId++}"),
            Name = name,
            SecretHash = Secrets.Hash(secret),
            JoinOrder = _slots.Count + 1,
            ConnectionId = connectionId,
            Profile = profile
        };
        _slots.Add(slot);
        return slot;
    }

    /// <summary>Issues a new secret for the slot, stores only its hash and returns the plaintext.</summary>
    public string RotateSecret(PlayerSlot slot)
    {
        var secret = Secrets.NewToken();
        slot.SecretHash = Secrets.Hash(secret);
        return secret;
    }

    public PlayerSlot? FindBySecret(PlayerId id, string secret)
    {
        var slot = Find(id);
        if (slot != null && Secrets.Matches(slot.SecretHash, secret))
        {
            return slot;
        }
        return null;
    }

    public PlayerSlot? FindByConnection(string connectionId)
    {
        if (connectionId == null)
        {
            return null;
        }
        return _slots.FirstOrDefault(s => s.ConnectionId == connectionId);
    }

    /// <summary>The seat of the person behind <paramref name="profile"/>, if they have one in this room.</summary>
    public PlayerSlot? FindByProfile(ProfileId profile) => _slots.FirstOrDefault(s => s.Profile == profile);

    public PlayerSlot? Find(PlayerId id)
    {
        return _slots.FirstOrDefault(s => s.Id == id);
    }

    public void Bind(PlayerSlot slot, string connectionId)
    {
        slot.ConnectionId = connectionId;
    }

    public PlayerSlot? Unbind(string connectionId)
    {
        if (connectionId == null)
        {
            return null;
        }

        var slot = _slots.FirstOrDefault(s => s.ConnectionId == connectionId);
        if (slot != null)
        {
            slot.ConnectionId = null;
            return slot;
        }

        return null;
    }

    public bool Remove(PlayerId id)
    {
        var index = _slots.FindIndex(s => s.Id == id);
        if (index >= 0)
        {
            _slots.RemoveAt(index);
            return true;
        }
        return false;
    }

    public IReadOnlyList<LobbyPlayer> ToLobby()
    {
        var vip = VipId;
        return _slots
            .Select(s => new LobbyPlayer(s.Id, s.Name, IsVip: s.Id == vip, Connected: s.Connected, s.Profile))
            .ToList();
    }
}
