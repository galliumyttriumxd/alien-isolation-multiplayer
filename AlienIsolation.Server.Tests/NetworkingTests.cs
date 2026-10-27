using System.Net;
using System.Net.Sockets;
using System.Numerics;
using AlienIsolation.Server.Models;
using AlienIsolation.Server.Networking;
using AlienIsolation.Server.State;
using Xunit;

namespace AlienIsolation.Server.Tests;

public sealed class NetworkingTests
{
    [Fact]
    public async Task LengthPrefixedFrameRoundTrips()
    {
        var payload = PacketHandler.SerializeConnect(new ConnectPacket("Adam", "NinaTaylor"));
        await using var stream = new MemoryStream();

        await PacketHandler.WriteFrameAsync(stream, payload);
        stream.Position = 0;

        var result = await PacketHandler.ReadFrameAsync(stream);

        Assert.NotNull(result);
        Assert.Equal(payload, result);
    }

    [Fact]
    public void PacketRoundTripsPreserveTransform()
    {
        var expected = new TransformPacket(
            7,
            new Vector3(1, 2, 3),
            new Vector3(4, 5, 6),
            2);

        var actual = PacketHandler.DeserializeTransform(PacketHandler.SerializeTransform(expected));

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void UnknownOpcodeIsRejected()
    {
        var exception = Assert.Throws<InvalidDataException>(
            () => PacketHandler.ReadOpcode(new byte[] { 0xFF }));

        Assert.Contains("Unsupported packet opcode", exception.Message);
    }

    [Fact]
    public async Task PlayerStateRegistrationAndCleanup()
    {
        var manager = new PlayerStateManager();
        await using var session = new ClientSession(
            1,
            new TcpClient(),
            new ConnectPacket("First", "NinaTaylor"));

        Assert.True(manager.Register(session));
        Assert.Equal("First", Assert.Single(manager.Snapshot()).Username);
        Assert.True(manager.Remove(session.PlayerId));
        Assert.Empty(manager.Snapshot());
    }

    [Fact]
    public async Task PlayerStateAcceptsValidTransform()
    {
        var manager = new PlayerStateManager(minimumUpdateInterval: TimeSpan.Zero);
        await using var session = new ClientSession(
            1,
            new TcpClient(),
            new ConnectPacket("First", "NinaTaylor"));
        var endpoint = new IPEndPoint(IPAddress.Loopback, 4000);
        manager.Register(session);
        manager.Associate(session.PlayerId, endpoint);

        var accepted = manager.TryApplyTransform(
            1,
            new TransformPacket(1, new Vector3(1, 2, 3), Vector3.Zero, 1),
            endpoint,
            DateTimeOffset.UtcNow,
            out var reason);

        Assert.True(accepted, reason);
        var state = Assert.Single(manager.Snapshot());
        Assert.Equal(new Vector3(1, 2, 3), state.Position);
        Assert.Equal(PlayerConnectionStatus.Associated, state.Status);
    }

    [Fact]
    public async Task PlayerStateRejectsMismatchedAndNonFiniteTransforms()
    {
        var manager = new PlayerStateManager(minimumUpdateInterval: TimeSpan.Zero);
        await using var session = new ClientSession(
            1,
            new TcpClient(),
            new ConnectPacket("First", "NinaTaylor"));
        var endpoint = new IPEndPoint(IPAddress.Loopback, 4000);
        manager.Register(session);
        manager.Associate(session.PlayerId, endpoint);

        Assert.False(manager.TryApplyTransform(
            1,
            new TransformPacket(2, Vector3.Zero, Vector3.Zero, 0),
            endpoint,
            DateTimeOffset.UtcNow,
            out var mismatch));
        Assert.Contains("does not match", mismatch);

        Assert.False(manager.TryApplyTransform(
            1,
            new TransformPacket(1, new Vector3(float.NaN, 0, 0), Vector3.Zero, 0),
            endpoint,
            DateTimeOffset.UtcNow,
            out var nonFinite));
        Assert.Contains("non-finite", nonFinite);
    }

    [Fact]
    public async Task PlayerStateRejectsExcessiveMovementAndExpires()
    {
        var manager = new PlayerStateManager(
            staleTimeout: TimeSpan.FromSeconds(1),
            maximumMovementDelta: 5,
            minimumUpdateInterval: TimeSpan.Zero);
        await using var session = new ClientSession(
            1,
            new TcpClient(),
            new ConnectPacket("First", "NinaTaylor"));
        var endpoint = new IPEndPoint(IPAddress.Loopback, 4000);
        var firstUpdate = DateTimeOffset.UtcNow;
        manager.Register(session);
        manager.Associate(session.PlayerId, endpoint);
        Assert.True(manager.TryApplyTransform(
            1,
            new TransformPacket(1, Vector3.Zero, Vector3.Zero, 0),
            endpoint,
            firstUpdate,
            out _));

        Assert.False(manager.TryApplyTransform(
            1,
            new TransformPacket(1, new Vector3(6, 0, 0), Vector3.Zero, 0),
            endpoint,
            firstUpdate.AddMilliseconds(25),
            out var movement));
        Assert.Contains("maximum", movement);

        Assert.Equal(1, manager.MarkStale(firstUpdate.AddSeconds(2)));
        Assert.Equal(PlayerConnectionStatus.Stale, Assert.Single(manager.Snapshot()).Status);
    }

    [Fact]
    public async Task TwoClientsReceiveRelayedTransform()
    {
        await using var server = new ServerListener(0, 0);
        using var cancellation = new CancellationTokenSource();
        var serverTask = server.StartAsync(cancellation.Token);
        await WaitForPortAsync(() => server.TcpPort, cancellation.Token);

        using var first = await ConnectAsync(server.TcpPort, "First");
        using var second = await ConnectAsync(server.TcpPort, "Second");
        await AssociateAsync(first, first.Acknowledgement.PlayerId);
        await AssociateAsync(second, second.Acknowledgement.PlayerId);
        await Task.Delay(100, cancellation.Token);

        var expected = new TransformPacket(
            first.Acknowledgement.PlayerId,
            new Vector3(10, 20, 30),
            Vector3.Zero,
            1);
        await first.Udp.SendAsync(
            PacketHandler.SerializeTransform(expected),
            new IPEndPoint(IPAddress.Loopback, first.Acknowledgement.UdpPort));

        using var receiveTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var datagram = await second.Udp.ReceiveAsync(receiveTimeout.Token);
        var actual = PacketHandler.DeserializeTransform(datagram.Buffer);

        Assert.Equal(expected, actual);
        Assert.Equal(
            expected.Position,
            Assert.Single(
                server.PlayerStates.Snapshot(),
                state => state.PlayerId == expected.PlayerId).Position);

        cancellation.Cancel();
        await serverTask;
    }

    private static async Task<TestConnection> ConnectAsync(int port, string username)
    {
        var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, port);
        await PacketHandler.WriteFrameAsync(
            tcp.GetStream(),
            PacketHandler.SerializeConnect(new ConnectPacket(username, "NinaTaylor")));
        var payload = await PacketHandler.ReadFrameAsync(tcp.GetStream())
            ?? throw new InvalidDataException("CONNECT_ACK was not received.");
        return new TestConnection(
            tcp,
            new UdpClient(0),
            PacketHandler.DeserializeConnectAck(payload));
    }

    private static async Task AssociateAsync(TestConnection connection, byte playerId)
    {
        await connection.Udp.SendAsync(
            PacketHandler.SerializeUdpAssociate(new UdpAssociatePacket(playerId)),
            new IPEndPoint(IPAddress.Loopback, connection.Acknowledgement.UdpPort));
    }

    private static async Task WaitForPortAsync(
        Func<int> portProvider,
        CancellationToken cancellationToken)
    {
        while (portProvider() == 0)
        {
            await Task.Delay(10, cancellationToken);
        }
    }

    private sealed class TestConnection(
        TcpClient tcp,
        UdpClient udp,
        ConnectAckPacket acknowledgement) : IDisposable
    {
        public TcpClient Tcp { get; } = tcp;
        public UdpClient Udp { get; } = udp;
        public ConnectAckPacket Acknowledgement { get; } = acknowledgement;

        public void Dispose()
        {
            Udp.Dispose();
            Tcp.Dispose();
        }
    }
}
