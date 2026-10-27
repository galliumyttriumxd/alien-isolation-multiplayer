using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using AlienIsolation.Server.Models;
using AlienIsolation.Server.State;

namespace AlienIsolation.Server.Networking;

public sealed class ServerListener : IAsyncDisposable
{
    private readonly TcpListener _tcpListener;
    private readonly UdpListener _udpListener;
    private readonly ConcurrentDictionary<byte, ClientSession> _sessions = new();
    private readonly PlayerStateManager _playerStateManager;
    private readonly TransformRelay _transformRelay;
    private int _nextPlayerId;

    public ServerListener(
        int tcpPort = 7777,
        int udpPort = 7777,
        PlayerStateManager? playerStateManager = null)
    {
        _tcpListener = new TcpListener(IPAddress.Any, tcpPort);
        _udpListener = new UdpListener(udpPort);
        _playerStateManager = playerStateManager ?? new PlayerStateManager();
        _transformRelay = new TransformRelay(
            _playerStateManager,
            (playerId, payload) => _udpListener.SendToMappedPlayersAsync(playerId, payload));
        _udpListener.DatagramReceived += HandleDatagramAsync;
    }

    public IReadOnlyCollection<ClientSession> Sessions => _sessions.Values.ToArray();

    public PlayerStateManager PlayerStates => _playerStateManager;

    public int UdpPort => _udpListener.LocalPort;

    public int TcpPort => _tcpListener.LocalEndpoint is IPEndPoint endpoint ? endpoint.Port : 0;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        _tcpListener.Start();
        var udpTask = _udpListener.ListenAsync(cancellationToken);
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var client = await _tcpListener.AcceptTcpClientAsync(cancellationToken);
                _ = HandleClientAsync(client, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            _tcpListener.Stop();
            await udpTask;
        }
    }

    public void MapUdpEndpoint(byte playerId, IPEndPoint endpoint) =>
        _udpListener.MapEndpoint(playerId, endpoint);

    public async ValueTask DisposeAsync()
    {
        _tcpListener.Stop();
        foreach (var session in _sessions.Values)
        {
            await session.DisposeAsync();
        }

        await _udpListener.DisposeAsync();
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        ClientSession? session = null;
        try
        {
            var payload = await PacketHandler.ReadFrameAsync(client.GetStream(), cancellationToken)
                ?? throw new InvalidDataException("The client disconnected before sending a handshake.");
            var handshake = PacketHandler.DeserializeConnect(payload);
            var playerId = AllocatePlayerId();
            session = new ClientSession(playerId, client, handshake);
            if (!_sessions.TryAdd(playerId, session))
            {
                throw new InvalidOperationException($"Player ID {playerId} is already in use.");
            }

            if (!_playerStateManager.Register(session))
            {
                throw new InvalidOperationException($"Player state {playerId} is already in use.");
            }

            var acknowledgement = PacketHandler.SerializeConnectAck(
                new ConnectAckPacket(playerId, checked((ushort)UdpPort)));
            await PacketHandler.WriteFrameAsync(session.Stream, acknowledgement, cancellationToken);

            while (await PacketHandler.ReadFrameAsync(session.Stream, cancellationToken) is not null)
            {
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (IOException exception)
        {
            Console.Error.WriteLine($"TCP client connection failed: {exception.Message}");
        }
        catch (InvalidDataException exception)
        {
            Console.Error.WriteLine($"TCP protocol error: {exception.Message}");
        }
        catch (InvalidOperationException exception)
        {
            Console.Error.WriteLine($"TCP client was rejected: {exception.Message}");
        }
        finally
        {
            if (session is not null)
            {
                _sessions.TryRemove(session.PlayerId, out _);
                _playerStateManager.Remove(session.PlayerId);
                if (session.UdpEndpoint is not null)
                {
                    _udpListener.RemoveEndpoint(session.UdpEndpoint);
                }

                await session.DisposeAsync();
            }
            else
            {
                client.Dispose();
            }
        }
    }

    private async Task HandleDatagramAsync(IPEndPoint endpoint, ReadOnlyMemory<byte> payload)
    {
        try
        {
            var opcode = PacketHandler.ReadOpcode(payload.Span);
            if (opcode == PacketOpcode.UdpAssociate)
            {
                var association = PacketHandler.DeserializeUdpAssociate(payload.Span);
                if (!_sessions.TryGetValue(association.PlayerId, out var session) ||
                    !IsSameAddress(session.Client, endpoint.Address))
                {
                    throw new InvalidDataException("UDP association does not match an active TCP session.");
                }

                if (session.UdpEndpoint is not null)
                {
                    _udpListener.RemoveEndpoint(session.UdpEndpoint);
                }

                session.UdpEndpoint = endpoint;
                _udpListener.MapEndpoint(association.PlayerId, endpoint);
                _playerStateManager.Associate(association.PlayerId, endpoint);
                Console.WriteLine($"Mapped UDP endpoint {endpoint} to player {association.PlayerId}.");
                return;
            }

            if (!_udpListener.TryGetPlayerId(endpoint, out var playerId))
            {
                throw new InvalidDataException($"Received UDP data from unmapped endpoint {endpoint}.");
            }

            if (opcode is not (PacketOpcode.Transform or PacketOpcode.Rpc or PacketOpcode.Save))
            {
                throw new InvalidDataException($"UDP opcode {opcode} is not relayable.");
            }

            if (opcode == PacketOpcode.Transform)
            {
                await _transformRelay.ProcessAsync(playerId, endpoint, payload);
                return;
            }

            await _udpListener.SendToMappedPlayersAsync(playerId, payload);
        }
        catch (InvalidDataException exception)
        {
            Console.Error.WriteLine($"UDP protocol error: {exception.Message}");
        }
    }

    private static bool IsSameAddress(TcpClient client, IPAddress address) =>
        ((IPEndPoint)client.Client.RemoteEndPoint!).Address.Equals(address);

    private byte AllocatePlayerId()
    {
        for (var attempt = 0; attempt < byte.MaxValue; attempt++)
        {
            var candidate = unchecked((byte)Interlocked.Increment(ref _nextPlayerId));
            if (candidate == 0)
            {
                continue;
            }

            if (!_sessions.ContainsKey(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("The server has no available player IDs.");
    }
}
