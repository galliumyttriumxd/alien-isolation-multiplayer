using System.Net;
using System.Numerics;

namespace AlienIsolation.Server.Models;

public enum PlayerConnectionStatus
{
    Connected,
    Associated,
    Stale
}

public sealed class PlayerState
{
    internal PlayerState(byte playerId, string username, string selectedMeshId)
    {
        PlayerId = playerId;
        Username = username;
        SelectedMeshId = selectedMeshId;
        Status = PlayerConnectionStatus.Connected;
    }

    public byte PlayerId { get; }
    public string Username { get; }
    public string SelectedMeshId { get; }
    public Vector3 Position { get; internal set; }
    public Vector3 Rotation { get; internal set; }
    public byte LocomotionState { get; internal set; }
    public DateTimeOffset LastUpdate { get; internal set; }
    public IPEndPoint? LastUdpEndpoint { get; internal set; }
    public PlayerConnectionStatus Status { get; internal set; }

    internal PlayerState Snapshot() => new(PlayerId, Username, SelectedMeshId)
    {
        Position = Position,
        Rotation = Rotation,
        LocomotionState = LocomotionState,
        LastUpdate = LastUpdate,
        LastUdpEndpoint = LastUdpEndpoint is null
            ? null
            : new IPEndPoint(LastUdpEndpoint.Address, LastUdpEndpoint.Port),
        Status = Status
    };
}
