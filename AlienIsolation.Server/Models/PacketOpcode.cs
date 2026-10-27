namespace AlienIsolation.Server.Models;

public enum PacketOpcode : byte
{
    Connect = 0x01,
    Transform = 0x02,
    Rpc = 0x03,
    Save = 0x04,
    ConnectAck = 0x05,
    UdpAssociate = 0x06
}
