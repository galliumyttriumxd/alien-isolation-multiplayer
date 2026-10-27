# AlienIsolation.Server networking tests

## Build and automated tests

From the repository root:

```powershell
dotnet build .\AlienIsolation.Server\AlienIsolation.Server.csproj
dotnet test .\AlienIsolation.Server.Tests\AlienIsolation.Server.Tests.csproj
```

The test project covers TCP length-prefix framing, packet round-trips, invalid opcodes, player-state registration and cleanup,
transform validation (identity, finite values, movement limits, and stale expiry), and two-client UDP transform relay.

The server keeps gameplay state separate from socket handling through `PlayerStateManager`. Transform datagrams are
validated before relay; invalid player IDs, non-finite values, excessive movement, excessive update frequency, stale
players, and unassociated endpoints are rejected and logged.

## Run the server

```powershell
dotnet run --project .\AlienIsolation.Server\AlienIsolation.Server.csproj
```

The server listens on TCP and UDP port `7777`.

## Run the two-client relay smoke test

The smoke test is a client and must run while a separate server process is already listening.

In terminal 1, start the server:

```powershell
dotnet run --project .\AlienIsolation.Server\AlienIsolation.Server.csproj
```

Wait for:

```text
AlienIsolation.Server listening on TCP/UDP port 7777.
```

Then, in terminal 2, run the test:

```powershell
dotnet run `
  --project .\AlienIsolation.Server\AlienIsolation.Server.csproj `
  --no-build `
  -- `
  --test-relay 127.0.0.1 7777
```

The test creates two TCP sessions, associates two UDP sockets, sends a transform from the first client, and verifies that the second client receives the same transform.

## Test over Hamachi

1. Start the server on the host computer.
2. Find the host's Hamachi IPv4 address, usually in the `25.x.x.x` range.
3. Allow both protocols through Windows Firewall. Run PowerShell as Administrator:

   ```powershell
   New-NetFirewallRule `
     -DisplayName "Alien Isolation Server TCP 7777" `
     -Direction Inbound `
     -Protocol TCP `
     -LocalPort 7777 `
     -Action Allow

   New-NetFirewallRule `
     -DisplayName "Alien Isolation Server UDP 7777" `
     -Direction Inbound `
     -Protocol UDP `
     -LocalPort 7777 `
     -Action Allow
   ```

4. On the remote computer, run:

   ```powershell
   dotnet run `
     --project .\AlienIsolation.Server\AlienIsolation.Server.csproj `
     --no-build `
     -- `
     --test-relay 25.x.x.x 7777
   ```

Replace `25.x.x.x` with the host's Hamachi address. The expected result is:

```text
Two-client UDP relay verified for player 1.
```

If TCP connectivity succeeds but the relay test times out, check that UDP `7777` is allowed and that both computers are joined to the same Hamachi network.
