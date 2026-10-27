using System.Collections.Concurrent;
using System.Net;
using System.Numerics;
using AlienIsolation.Server.Models;

namespace AlienIsolation.Server.State;

public sealed class PlayerStateManager
{
    private readonly ConcurrentDictionary<byte, PlayerState> _players = new();
    private readonly object _sync = new();
    private readonly TimeSpan _staleTimeout;
    private readonly double _maximumMovementDelta;
    private readonly TimeSpan _minimumUpdateInterval;

    public PlayerStateManager(
        TimeSpan? staleTimeout = null,
        double maximumMovementDelta = 100,
        TimeSpan? minimumUpdateInterval = null)
    {
        if (maximumMovementDelta <= 0 || double.IsNaN(maximumMovementDelta) ||
            double.IsInfinity(maximumMovementDelta))
        {
            throw new ArgumentOutOfRangeException(nameof(maximumMovementDelta));
        }

        _staleTimeout = staleTimeout ?? TimeSpan.FromSeconds(5);
        _maximumMovementDelta = maximumMovementDelta;
        _minimumUpdateInterval = minimumUpdateInterval ?? TimeSpan.FromMilliseconds(20);
    }

    public IReadOnlyCollection<PlayerState> Snapshot(DateTimeOffset? now = null)
    {
        MarkStale(now ?? DateTimeOffset.UtcNow);
        return _players.Values.Select(player => player.Snapshot()).ToArray();
    }

    public bool Register(ClientSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return _players.TryAdd(
            session.PlayerId,
            new PlayerState(
                session.PlayerId,
                session.Handshake.Username,
                session.Handshake.SelectedMeshId));
    }

    public bool Remove(byte playerId) => _players.TryRemove(playerId, out _);

    public bool Associate(byte playerId, IPEndPoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (!_players.TryGetValue(playerId, out var player))
        {
            return false;
        }

        lock (_sync)
        {
            player.LastUdpEndpoint = new IPEndPoint(endpoint.Address, endpoint.Port);
            player.Status = PlayerConnectionStatus.Associated;
        }

        return true;
    }

    public bool TryApplyTransform(
        byte associatedPlayerId,
        TransformPacket packet,
        IPEndPoint endpoint,
        DateTimeOffset now,
        out string rejectionReason)
    {
        ArgumentNullException.ThrowIfNull(packet);
        ArgumentNullException.ThrowIfNull(endpoint);

        if (packet.PlayerId != associatedPlayerId)
        {
            rejectionReason = "UDP transform player ID does not match its endpoint.";
            return false;
        }

        if (!IsFinite(packet.Position) || !IsFinite(packet.Rotation))
        {
            rejectionReason = "UDP transform contains a non-finite position or rotation.";
            return false;
        }

        if (!_players.TryGetValue(associatedPlayerId, out var player))
        {
            rejectionReason = $"Player {associatedPlayerId} is not registered.";
            return false;
        }

        lock (_sync)
        {
            if (player.Status == PlayerConnectionStatus.Stale)
            {
                rejectionReason = $"Player {associatedPlayerId} is stale.";
                return false;
            }

            if (player.LastUdpEndpoint is null ||
                !player.LastUdpEndpoint.Equals(endpoint))
            {
                rejectionReason = "UDP transform endpoint is not associated with the player.";
                return false;
            }

            if (player.LastUpdate != default &&
                now - player.LastUpdate < _minimumUpdateInterval)
            {
                rejectionReason = "UDP transform update frequency exceeds the configured limit.";
                return false;
            }

            if (player.LastUpdate != default &&
                Vector3.Distance(player.Position, packet.Position) > _maximumMovementDelta)
            {
                rejectionReason = "UDP transform movement exceeds the configured maximum.";
                return false;
            }

            player.Position = packet.Position;
            player.Rotation = packet.Rotation;
            player.LocomotionState = packet.LocomotionState;
            player.LastUpdate = now;
        }

        rejectionReason = string.Empty;
        return true;
    }

    public int MarkStale(DateTimeOffset now)
    {
        var marked = 0;
        foreach (var player in _players.Values)
        {
            lock (_sync)
            {
                if (player.Status != PlayerConnectionStatus.Stale &&
                    player.LastUpdate != default &&
                    now - player.LastUpdate > _staleTimeout)
                {
                    player.Status = PlayerConnectionStatus.Stale;
                    marked++;
                }
            }
        }

        return marked;
    }

    private static bool IsFinite(Vector3 vector) =>
        float.IsFinite(vector.X) && float.IsFinite(vector.Y) && float.IsFinite(vector.Z);
}
