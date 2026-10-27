using System.Net;
using System.Net.Sockets;
using System.Numerics;
using AlienIsolation.Server.Models;
using AlienIsolation.Server.Networking;

internal static class TestClient
{
    public static async Task RunAsync(string[] args)
    {
        var host = args.ElementAtOrDefault(0) ?? "127.0.0.1";
        var port = args.Length > 1 && int.TryParse(args[1], out var parsedPort) ? parsedPort : 7777;
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(host, port);

        var handshake = PacketHandler.SerializeConnect(new ConnectPacket("TestClient", "NinaTaylor"));
        await PacketHandler.WriteFrameAsync(tcp.GetStream(), handshake);
        var acknowledgementPayload = await PacketHandler.ReadFrameAsync(tcp.GetStream())
            ?? throw new InvalidDataException("The server closed the connection before CONNECT_ACK.");
        var acknowledgement = PacketHandler.DeserializeConnectAck(acknowledgementPayload);

        using var udp = new UdpClient(0);
        var serverEndpoint = new IPEndPoint((await Dns.GetHostAddressesAsync(host))
            .First(address => address.AddressFamily == AddressFamily.InterNetwork), acknowledgement.UdpPort);
        var association = PacketHandler.SerializeUdpAssociate(new UdpAssociatePacket(acknowledgement.PlayerId));
        await udp.SendAsync(association, serverEndpoint);

        var transform = PacketHandler.SerializeTransform(new TransformPacket(
            acknowledgement.PlayerId,
            new Vector3(1, 2, 3),
            Vector3.Zero,
            1));
        await udp.SendAsync(transform, serverEndpoint);
        Console.WriteLine(
            $"Connected to {host}:{port}; assigned player {acknowledgement.PlayerId}; sent UDP transform.");
        await Task.Delay(TimeSpan.FromSeconds(2));
    }

    public static async Task RunRelayAsync(string[] args)
    {
        var host = args.ElementAtOrDefault(0) ?? "127.0.0.1";
        var port = args.Length > 1 && int.TryParse(args[1], out var parsedPort) ? parsedPort : 7777;
        using var first = await ConnectAsync(host, port, "RelaySender");
        using var second = await ConnectAsync(host, port, "RelayReceiver");

        await AssociateAsync(first.Udp, first.Acknowledgement.PlayerId, first.ServerEndpoint);
        await AssociateAsync(second.Udp, second.Acknowledgement.PlayerId, second.ServerEndpoint);
        await Task.Delay(100);

        var expected = new TransformPacket(
            first.Acknowledgement.PlayerId,
            new Vector3(10, 20, 30),
            new Vector3(1, 2, 3),
            2);
        await first.Udp.SendAsync(PacketHandler.SerializeTransform(expected), first.ServerEndpoint);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var received = await second.Udp.ReceiveAsync(timeout.Token);
        var actual = PacketHandler.DeserializeTransform(received.Buffer);
        if (actual != expected)
        {
            throw new InvalidDataException("The relayed transform did not match the sent transform.");
        }

        Console.WriteLine($"Two-client UDP relay verified for player {actual.PlayerId}.");
    }

    private static async Task AssociateAsync(
        UdpClient udp,
        byte playerId,
        IPEndPoint serverEndpoint)
    {
        var association = PacketHandler.SerializeUdpAssociate(new UdpAssociatePacket(playerId));
        await udp.SendAsync(association, serverEndpoint);
    }

    private static async Task<TestConnection> ConnectAsync(
        string host,
        int port,
        string username)
    {
        var tcp = new TcpClient();
        await tcp.ConnectAsync(host, port);
        var handshake = PacketHandler.SerializeConnect(new ConnectPacket(username, "NinaTaylor"));
        await PacketHandler.WriteFrameAsync(tcp.GetStream(), handshake);
        var payload = await PacketHandler.ReadFrameAsync(tcp.GetStream())
            ?? throw new InvalidDataException("The server closed before CONNECT_ACK.");
        var acknowledgement = PacketHandler.DeserializeConnectAck(payload);
        var address = (await Dns.GetHostAddressesAsync(host))
            .First(address => address.AddressFamily == AddressFamily.InterNetwork);
        return new TestConnection(
            tcp,
            new UdpClient(0),
            acknowledgement,
            new IPEndPoint(address, acknowledgement.UdpPort));
    }

    private sealed class TestConnection(
        TcpClient tcp,
        UdpClient udp,
        ConnectAckPacket acknowledgement,
        IPEndPoint serverEndpoint) : IDisposable
    {
        public TcpClient Tcp { get; } = tcp;
        public UdpClient Udp { get; } = udp;
        public ConnectAckPacket Acknowledgement { get; } = acknowledgement;
        public IPEndPoint ServerEndpoint { get; } = serverEndpoint;

        public void Dispose()
        {
            Udp.Dispose();
            Tcp.Dispose();
        }
    }
}
