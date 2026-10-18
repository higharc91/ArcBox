namespace ArcBox.Server.Hubs;

public interface IClientSender
{
    void Send(string connectionId, string method, params object?[] args);
}
