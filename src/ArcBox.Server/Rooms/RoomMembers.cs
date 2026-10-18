using ArcBox.Sdk;
using ArcBox.Sdk.Protocol;

namespace ArcBox.Server.Rooms;

public sealed class RoomMembers(RoomOptions options)
{
    private readonly List<PlayerSlot> _slots = [];
    private int _nextId = 1;

    public IReadOnlyList<PlayerSlot> All => _slots;

    public PlayerId? VipId => _slots.FirstOrDefault(s => s.Connected)?.Id;

    public bool AnyConnected => _slots.Any(s => s.Connected);

    public bool TryAdd(string rawName, string connectionId, out PlayerSlot? slot, out string? error)
    {
        var name = rawName?.Trim() ?? string.Empty;

        if (name.Length < 1 || name.Length > options.NameMaxLength)
        {
            error = $"Name must be 1–{options.NameMaxLength} characters.";
            slot = null;
            return false;
        }

        if (_slots.Any(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            error = "That name is taken.";
            slot = null;
            return false;
        }

        if (_slots.Count >= options.MaxPlayers)
        {
            error = "Room is full.";
            slot = null;
            return false;
        }

        slot = new PlayerSlot
        {
            Id = new PlayerId($"p{_nextId++}"),
            Name = name,
            Secret = Secrets.NewToken(),
            JoinOrder = _slots.Count + 1,
            ConnectionId = connectionId
        };
        _slots.Add(slot);
        error = null;
        return true;
    }

    public PlayerSlot? FindBySecret(PlayerId id, string secret)
    {
        var slot = Find(id);
        if (slot != null && Secrets.Matches(slot.Secret, secret))
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
            .Select(s => new LobbyPlayer(s.Id, s.Name, IsVip: s.Id == vip, Connected: s.Connected))
            .ToList();
    }
}
