using System.Collections.Concurrent;
using ArcBox.Server.Hubs;

namespace ArcBox.Server.Tests.Support;

public sealed record SentMessage(string ConnectionId, string Method, object?[] Args);

public sealed class RecordingSender : IClientSender
{
    private readonly ConcurrentQueue<SentMessage> _messages = new();

    public void Send(string connectionId, string method, params object?[] args)
    {
        _messages.Enqueue(new SentMessage(connectionId, method, args));
    }

    public IReadOnlyList<SentMessage> Of(string connectionId, string method)
    {
        return _messages.Where(m => m.ConnectionId == connectionId && m.Method == method).ToList();
    }
}
