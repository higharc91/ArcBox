using System.Collections.Immutable;

namespace Tactics.Presentation.World;

/// <summary>Only the current region/party's requested sheets; native uploads are drained by the host's frame budget.</summary>
public sealed class UnitTextureQueue
{
    private readonly Dictionary<string, bool> _wanted = new(StringComparer.Ordinal);
    private readonly Queue<string> _pending = new();
    public int Pending { get; private set; }

    /// <summary>Returns sheets no longer used, so the host can release their textures.</summary>
    public ImmutableArray<string> Retain(IEnumerable<string> paths)
    {
        var next = paths.ToHashSet(StringComparer.Ordinal);
        var removed = _wanted.Keys.Where(path => !next.Contains(path)).ToImmutableArray();
        foreach (var path in removed)
        {
            if (!_wanted[path]) Pending--;
            _wanted.Remove(path);
        }
        foreach (var path in next)
            if (_wanted.TryAdd(path, false)) { _pending.Enqueue(path); Pending++; }
        return removed;
    }

    /// <summary>One attempted upload. Missing files stay attempted until removed/re-added, avoiding an endless retry loop.</summary>
    public bool TryTake(out string path)
    {
        while (_pending.TryDequeue(out path!))
            if (_wanted.TryGetValue(path, out var attempted) && !attempted)
            {
                _wanted[path] = true;
                Pending--;
                return true;
            }
        path = "";
        return false;
    }
}
