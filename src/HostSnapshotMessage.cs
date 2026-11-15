using System;
using System.Text;
using SSMP.Networking.Packet;

namespace Silksong.CoopAssist
{
    public enum SnapshotKind:byte { Request, Snapshot, Unavailable }
    public sealed class HostSnapshotMessage:IPacketData
    {
        public const int MaxBytes=4*1024*1024;
        public SnapshotKind Kind;
        public string Token="";
        public byte[] Payload=Array.Empty<byte>();
        public bool IsReliable=>true;
        public bool DropReliableDataIfNewerExists=>false;
        public bool Valid=>Enum.IsDefined(typeof(SnapshotKind),Kind) && Guid.TryParseExact(Token,"N",out _) &&
            Payload!=null && Payload.Length<=MaxBytes && (Kind==SnapshotKind.Snapshot?Payload.Length>0:Payload.Length==0);
        public void WriteData(IPacket packet)
        {
            if(!Valid)throw new ArgumentException("Invalid host snapshot message");
            packet.Write((byte)Kind);packet.Write(Token);packet.Write(Payload.Length);packet.Write(Payload);
        }
        public void ReadData(IPacket packet)
        {
            Kind=(SnapshotKind)packet.ReadByte();Token=packet.ReadString();var size=packet.ReadInt();
            if(size<0 || size>MaxBytes)throw new ArgumentException("Host snapshot exceeds limit");
            Payload=packet.ReadBytes(size);
            if(!Valid)throw new ArgumentException("Invalid host snapshot message");
        }
        public static HostSnapshotMessage FromJson(string token,string json)
        {
            var bytes=Encoding.UTF8.GetBytes(json);
            if(bytes.Length>MaxBytes)throw new ArgumentException("Save exceeds snapshot limit");
            // SSMP's chunk API requires >65535 bytes. JSON permits trailing spaces; pad
            // small saves to take that one reliable transport, with no second framing path.
            if(bytes.Length<65536)
            {
                var padded=new byte[65536];Array.Copy(bytes,padded,bytes.Length);
                for(var i=bytes.Length;i<padded.Length;i++)padded[i]=32;
                bytes=padded;
            }
            return new HostSnapshotMessage{Kind=SnapshotKind.Snapshot,Token=token,Payload=bytes};
        }
        public string Json()=>new UTF8Encoding(false,true).GetString(Payload);
    }
}
