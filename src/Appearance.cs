using SSMP.Networking.Packet;

namespace Silksong.CoopAssist
{
    public sealed class Appearance : IPacketData
    {
        public ushort Id;
        public string Colour = "FFFFFFFF";
        public bool IsReliable => true;
        public bool DropReliableDataIfNewerExists => false;
        public void WriteData(IPacket p) { p.Write(Id); p.Write(Colour); }
        public void ReadData(IPacket p) { Id = p.ReadUShort(); Colour = p.ReadString(); }
        public static bool Valid(string value)
        {
            if (value == null || value.Length != 8) return false;
            foreach (var c in value) if (!((c >= '0' && c <= '9') || (c >= 'A' && c <= 'F') || (c >= 'a' && c <= 'f'))) return false;
            return true;
        }
    }
    // The font family/27pt policy is supplied by the package, never by a remote font path.
    public sealed class NameStyleMessage : IPacketData
    {
        public ushort Id;
        public byte Flags;
        public bool Bold=>Flags==1;
        public bool Valid=>Flags<=1;
        public bool IsReliable=>true;
        public bool DropReliableDataIfNewerExists=>false;
        public void WriteData(IPacket packet){packet.Write(Id);packet.Write(Flags);}
        public void ReadData(IPacket packet){Id=packet.ReadUShort();Flags=packet.ReadByte();}
    }
}
