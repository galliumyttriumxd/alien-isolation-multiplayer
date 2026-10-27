using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace AlienIsolation.Server.Networking;

public sealed class UdpListener : IAsyncDisposable
{
    private readonly UdpClient _client;
    private readonly ConcurrentDictionary<string, (IPEndPoint Endpoint, byte PlayerId)> _endpointToPlayer = new();

    public UdpListener(int port)
    {
        _client = new UdpClient(port);
        LocalPort = ((IPEndPoint)_client.Client.LocalEndPoint!).Port;
    }

    public int LocalPort { get; }

    public event Func<IPEndPoint, ReadOnlyMemory<byte>, Task>? DatagramReceived;

    public void MapEndpoint(byte playerId, IPEndPoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        _endpointToPlayer[GetEndpointKey(endpoint)] = (endpoint, playerId);
    }

    public bool TryGetPlayerId(IPEndPoint endpoint, out byte playerId) =>
        TryGetMapping(endpoint, out playerId);

    public bool RemoveEndpoint(IPEndPoint endpoint) =>
        _endpointToPlayer.TryRemove(GetEndpointKey(endpoint), out _);

    public async Task SendToMappedPlayersAsync(
        byte sourcePlayerId,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default)
    {
        var endpoints = _endpointToPlayer
            .Where(pair => pair.Value.PlayerId != sourcePlayerId)
            .Select(pair => pair.Value.Endpoint)
            .ToArray();

        foreach (var endpoint in endpoints)
        {
            await _client.SendAsync(payload.ToArray(), endpoint, cancellationToken);
        }
    }

    public async Task ListenAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.Register(_client.Close);
        try
        {
            while (true)
            {
                var result = await _client.ReceiveAsync(cancellationToken);
                var handler = DatagramReceived;
                if (handler is not null)
                {
                    await handler(result.RemoteEndPoint, result.Buffer);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    public ValueTask DisposeAsync()
    {
        _client.Dispose();
        return ValueTask.CompletedTask;
    }

    private bool TryGetMapping(IPEndPoint endpoint, out byte playerId)
    {
        if (_endpointToPlayer.TryGetValue(GetEndpointKey(endpoint), out var mapping))
        {
            playerId = mapping.PlayerId;
            return true;
        }

        playerId = default;
        return false;
    }

    private static string GetEndpointKey(IPEndPoint endpoint) =>
        $"{endpoint.Address}:{endpoint.Port}";
}
