using SSMP.Networking.Packet;

namespace Silksong.CoopAssist
{
    public sealed class EquipmentMessage : IPacketData
    {
        public ushort Peer;
        public string Name = "";
        public byte Kind;
        public int Value;
        public bool IsReliable => true;
        public bool DropReliableDataIfNewerExists => false;
        public bool Valid => Kind <= 6 && Name != null && Name.Length <= 128 &&
            (Kind < 2 || Kind == 5 ? Name.Length > 0 && Value == 1 :
                Name.Length == 0 && Value >= 0 && Value <= 32);

        public void WriteData(IPacket packet)
        {
            packet.Write(Peer); packet.Write(Name); packet.Write(Kind); packet.Write(Value);
        }

        public void ReadData(IPacket packet)
        {
            Peer = packet.ReadUShort(); Name = packet.ReadString(); Kind = packet.ReadByte(); Value = packet.ReadInt();
        }
    }
}
