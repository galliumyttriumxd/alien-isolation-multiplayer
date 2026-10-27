using System.Net;
using AlienIsolation.Server.State;

namespace AlienIsolation.Server.Networking;

public sealed class TransformRelay(
    PlayerStateManager stateManager,
    Func<byte, ReadOnlyMemory<byte>, Task> relay)
{
    public async Task ProcessAsync(
        byte playerId,
        IPEndPoint endpoint,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default)
    {
        var packet = PacketHandler.DeserializeTransform(payload.Span);
        if (!stateManager.TryApplyTransform(
                playerId,
                packet,
                endpoint,
                DateTimeOffset.UtcNow,
                out var rejectionReason))
        {
            throw new InvalidDataException(rejectionReason);
        }

        cancellationToken.ThrowIfCancellationRequested();
        await relay(playerId, payload);
    }
}
