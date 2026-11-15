using SSMP.Networking.Packet;

namespace Silksong.CoopAssist
{
    public enum AssistPacket { HostAnchor, Appearance, TeamTravel, HostSnapshot, NameStyle, SharedMaps, Rosaries, Equipment }
    public sealed class HostAnchor : IPacketData
    {
        public string Session = "", Scene = "", Gate = "";
        public ulong Sequence;
        public ushort Host;
        public float X, Y;
        public bool IsReliable => true;
        public bool DropReliableDataIfNewerExists => true;
        public void WriteData(IPacket p)
        { p.Write(Session); p.Write(Sequence); p.Write(Host); p.Write(Scene); p.Write(Gate); p.Write(X); p.Write(Y); }
        public void ReadData(IPacket p)
        { Session = p.ReadString(); Sequence = p.ReadULong(); Host = p.ReadUShort(); Scene = p.ReadString(); Gate = p.ReadString(); X = p.ReadFloat(); Y = p.ReadFloat(); }
    }
}
