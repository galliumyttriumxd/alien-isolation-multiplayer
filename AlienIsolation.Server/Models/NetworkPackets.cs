using System.Numerics;

namespace AlienIsolation.Server.Models;

public sealed record ConnectPacket(string Username, string SelectedMeshId);

public sealed record ConnectAckPacket(byte PlayerId, ushort UdpPort);

public sealed record UdpAssociatePacket(byte PlayerId);

public sealed record TransformPacket(
    byte PlayerId,
    Vector3 Position,
    Vector3 Rotation,
    byte LocomotionState);

public sealed record RpcPacket(uint EntityId, byte ActionType, string Payload);

public sealed record SavePacket(
    Vector3 HostPosition,
    Vector3 ClientPosition,
    string StoryFlags);
