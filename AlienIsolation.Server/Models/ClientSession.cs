using System.Net;
using System.Net.Sockets;

namespace AlienIsolation.Server.Models;

public sealed class ClientSession : IAsyncDisposable
{
    public ClientSession(byte playerId, TcpClient client, ConnectPacket handshake)
    {
        PlayerId = playerId;
        Client = client;
        Handshake = handshake;
    }

    public byte PlayerId { get; }

    public TcpClient Client { get; }

    public ConnectPacket Handshake { get; }

    public IPEndPoint? UdpEndpoint { get; internal set; }

    public NetworkStream Stream => Client.GetStream();

    public ValueTask DisposeAsync()
    {
        Client.Dispose();
        return ValueTask.CompletedTask;
    }
}
