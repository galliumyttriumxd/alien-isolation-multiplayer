using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using AlienIsolation.Server.Models;

namespace AlienIsolation.Server.Networking;

public static class PacketHandler
{
    public const int LengthPrefixSize = sizeof(int);
    public const int MaxPayloadLength = 1024 * 1024;

    public static async ValueTask<byte[]?> ReadFrameAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        var lengthBuffer = new byte[LengthPrefixSize];
        var bytesRead = await ReadExactlyOrEndAsync(stream, lengthBuffer, cancellationToken);
        if (bytesRead == 0)
        {
            return null;
        }

        if (bytesRead != LengthPrefixSize)
        {
            throw new InvalidDataException("The TCP frame length prefix was truncated.");
        }

        var payloadLength = BinaryPrimitives.ReadInt32LittleEndian(lengthBuffer);
        if (payloadLength <= 0 || payloadLength > MaxPayloadLength)
        {
            throw new InvalidDataException($"TCP frame length {payloadLength} is outside the valid range.");
        }

        var payload = new byte[payloadLength];
        await stream.ReadExactlyAsync(payload, cancellationToken);
        return payload;
    }

    public static async ValueTask WriteFrameAsync(
        Stream stream,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default)
    {
        if (payload.Length <= 0 || payload.Length > MaxPayloadLength)
        {
            throw new ArgumentOutOfRangeException(nameof(payload), "The payload length is invalid.");
        }

        var lengthBuffer = new byte[LengthPrefixSize];
        BinaryPrimitives.WriteInt32LittleEndian(lengthBuffer, payload.Length);
        await stream.WriteAsync(lengthBuffer, cancellationToken);
        await stream.WriteAsync(payload, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    public static byte[] SerializeConnect(ConnectPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        return WritePacket(PacketOpcode.Connect, writer =>
        {
            writer.Write(packet.Username);
            writer.Write(packet.SelectedMeshId);
        });
    }

    public static ConnectPacket DeserializeConnect(ReadOnlySpan<byte> payload)
    {
        using var reader = CreateReader(payload, PacketOpcode.Connect);
        var packet = new ConnectPacket(reader.ReadString(), reader.ReadString());
        EnsureEndOfPacket(reader);
        return packet;
    }

    public static byte[] SerializeConnectAck(ConnectAckPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        return WritePacket(PacketOpcode.ConnectAck, writer =>
        {
            writer.Write(packet.PlayerId);
            writer.Write(packet.UdpPort);
        });
    }

    public static ConnectAckPacket DeserializeConnectAck(ReadOnlySpan<byte> payload)
    {
        using var reader = CreateReader(payload, PacketOpcode.ConnectAck);
        var packet = new ConnectAckPacket(reader.ReadByte(), reader.ReadUInt16());
        EnsureEndOfPacket(reader);
        return packet;
    }

    public static byte[] SerializeUdpAssociate(UdpAssociatePacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        return WritePacket(PacketOpcode.UdpAssociate, writer => writer.Write(packet.PlayerId));
    }

    public static UdpAssociatePacket DeserializeUdpAssociate(ReadOnlySpan<byte> payload)
    {
        using var reader = CreateReader(payload, PacketOpcode.UdpAssociate);
        var packet = new UdpAssociatePacket(reader.ReadByte());
        EnsureEndOfPacket(reader);
        return packet;
    }

    public static TransformPacket DeserializeTransform(ReadOnlySpan<byte> payload)
    {
        using var reader = CreateReader(payload, PacketOpcode.Transform);
        var packet = new TransformPacket(
            reader.ReadByte(),
            ReadVector3(reader),
            ReadVector3(reader),
            reader.ReadByte());
        EnsureEndOfPacket(reader);
        return packet;
    }

    public static byte[] SerializeTransform(TransformPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        return WritePacket(PacketOpcode.Transform, writer =>
        {
            writer.Write(packet.PlayerId);
            WriteVector3(writer, packet.Position);
            WriteVector3(writer, packet.Rotation);
            writer.Write(packet.LocomotionState);
        });
    }

    public static byte[] SerializeRpc(RpcPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        return WritePacket(PacketOpcode.Rpc, writer =>
        {
            writer.Write(packet.EntityId);
            writer.Write(packet.ActionType);
            writer.Write(packet.Payload);
        });
    }

    public static RpcPacket DeserializeRpc(ReadOnlySpan<byte> payload)
    {
        using var reader = CreateReader(payload, PacketOpcode.Rpc);
        var packet = new RpcPacket(reader.ReadUInt32(), reader.ReadByte(), reader.ReadString());
        EnsureEndOfPacket(reader);
        return packet;
    }

    public static byte[] SerializeSave(SavePacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        return WritePacket(PacketOpcode.Save, writer =>
        {
            WriteVector3(writer, packet.HostPosition);
            WriteVector3(writer, packet.ClientPosition);
            writer.Write(packet.StoryFlags);
        });
    }

    public static SavePacket DeserializeSave(ReadOnlySpan<byte> payload)
    {
        using var reader = CreateReader(payload, PacketOpcode.Save);
        var packet = new SavePacket(ReadVector3(reader), ReadVector3(reader), reader.ReadString());
        EnsureEndOfPacket(reader);
        return packet;
    }

    public static PacketOpcode ReadOpcode(ReadOnlySpan<byte> payload)
    {
        if (payload.IsEmpty)
        {
            throw new InvalidDataException("The packet payload is empty.");
        }

        var opcode = (PacketOpcode)payload[0];
        if (!Enum.IsDefined(opcode))
        {
            throw new InvalidDataException($"Unsupported packet opcode 0x{payload[0]:X2}.");
        }

        return opcode;
    }

    private static BinaryReader CreateReader(ReadOnlySpan<byte> payload, PacketOpcode expectedOpcode)
    {
        if (ReadOpcode(payload) != expectedOpcode)
        {
            throw new InvalidDataException($"Expected packet opcode {expectedOpcode}.");
        }

        return new BinaryReader(new MemoryStream(payload[1..].ToArray()), Encoding.UTF8, leaveOpen: false);
    }

    private static byte[] WritePacket(PacketOpcode opcode, Action<BinaryWriter> writeBody)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((byte)opcode);
            writeBody(writer);
        }

        return stream.ToArray();
    }

    private static void WriteVector3(BinaryWriter writer, Vector3 vector)
    {
        writer.Write(vector.X);
        writer.Write(vector.Y);
        writer.Write(vector.Z);
    }

    private static Vector3 ReadVector3(BinaryReader reader) =>
        new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());

    private static async ValueTask<int> ReadExactlyOrEndAsync(
        Stream stream,
        Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        var totalRead = 0;
        while (totalRead < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer[totalRead..], cancellationToken);
            if (read == 0)
            {
                return totalRead;
            }

            totalRead += read;
        }

        return totalRead;
    }

    private static void EnsureEndOfPacket(BinaryReader reader)
    {
        if (reader.BaseStream.Position != reader.BaseStream.Length)
        {
            throw new InvalidDataException("The packet contains trailing data.");
        }
    }
}
