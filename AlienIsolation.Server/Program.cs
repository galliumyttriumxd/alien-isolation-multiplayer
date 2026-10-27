using System.Net.Sockets;
using AlienIsolation.Server.Networking;

try
{
    if (args.Length > 0 && args[0].Equals("--test-client", StringComparison.OrdinalIgnoreCase))
    {
        await TestClient.RunAsync(args.Skip(1).ToArray());
        return;
    }

    if (args.Length > 0 && args[0].Equals("--test-relay", StringComparison.OrdinalIgnoreCase))
    {
        await TestClient.RunRelayAsync(args.Skip(1).ToArray());
        return;
    }

    using var shutdown = new CancellationTokenSource();
    Console.CancelKeyPress += (_, eventArgs) =>
    {
        eventArgs.Cancel = true;
        shutdown.Cancel();
    };

    await using var server = new ServerListener();
    Console.WriteLine("AlienIsolation.Server listening on TCP/UDP port 7777.");
    await server.StartAsync(shutdown.Token);
}
catch (SocketException exception) when (exception.SocketErrorCode == SocketError.ConnectionRefused)
{
    Console.Error.WriteLine(
        "Could not connect to the server. Start AlienIsolation.Server first and verify the host and TCP port 7777.");
    Environment.ExitCode = 1;
}
