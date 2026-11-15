using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SSMP.Networking.Packet;

namespace Silksong.CoopAssist
{
    public enum SharedMapKind:byte { Request, Offer, State }
    public sealed class SharedMapRoom
    {
        public byte Kind;
        public string Name="";
    }
    public sealed class SharedMapMessage:IPacketData
    {
        public const int MaxRooms=32;
        public SharedMapKind Kind;
        public string Token="",Epoch="";
        public uint Flags;
        public int Quill;
        public SharedMapRoom[] Rooms=Array.Empty<SharedMapRoom>();
        public bool IsReliable=>true;
        // Every batch contributes distinct rooms, so none may supersede another.
        public bool DropReliableDataIfNewerExists=>false;
        public bool Valid=>Enum.IsDefined(typeof(SharedMapKind),Kind) && Guid.TryParseExact(Token,"N",out _) &&
            (Guid.TryParseExact(Epoch,"N",out _) || Kind==SharedMapKind.Request && Epoch=="") &&
            (Flags&~SharedMapProgress.ValidFlags)==0 && Quill>=0 && Quill<=100 && Rooms!=null && Rooms.Length<=MaxRooms &&
            Rooms.All(x=>x!=null && x.Kind<3 && ValidRoom(x.Name)) &&
            (Kind!=SharedMapKind.Request || Flags==0 && Quill==0 && Rooms.Length==0);
        internal static bool ValidRoom(string room)=>!string.IsNullOrEmpty(room) && room.Length<=128 &&
            Encoding.UTF8.GetByteCount(room)<=256 && !room.Any(char.IsControl);
        public void WriteData(IPacket packet)
        {
            if(!Valid)throw new ArgumentException("Invalid shared map message");
            packet.Write((byte)Kind);packet.Write(Token);packet.Write(Epoch);packet.Write(Flags);packet.Write(Quill);
            packet.Write((byte)Rooms.Length);
            foreach(var room in Rooms){packet.Write(room.Kind);packet.Write(room.Name);}
        }
        public void ReadData(IPacket packet)
        {
            Kind=(SharedMapKind)packet.ReadByte();Token=packet.ReadString();Epoch=packet.ReadString();
            Flags=packet.ReadUInt();Quill=packet.ReadInt();int count=packet.ReadByte();
            if(count>MaxRooms)throw new ArgumentException("Shared map batch exceeds limit");
            Rooms=new SharedMapRoom[count];
            for(int i=0;i<count;i++)Rooms[i]=new SharedMapRoom{Kind=packet.ReadByte(),Name=packet.ReadString()};
            if(!Valid)throw new ArgumentException("Invalid shared map message");
        }
        public SharedMapProgress Progress()
        {
            if(!Valid || Kind==SharedMapKind.Request)throw new ArgumentException("No map progress");
            var state=new SharedMapProgress{Flags=Flags,Quill=Quill};
            foreach(var room in Rooms)state.Rooms[room.Kind].Add(room.Name);
            return state;
        }
        public static IEnumerable<SharedMapMessage> Batches(SharedMapKind kind,string token,string epoch,
            SharedMapProgress state,SharedMapProgress previous=null)
        {
            var rooms=new List<SharedMapRoom>(MaxRooms);
            var emitted=false;
            for(byte i=0;i<state.Rooms.Length;i++)
            {
                var missing = state.Rooms[i].Where(room => previous == null || !previous.Rooms[i].Contains(room));
                foreach (var room in missing.OrderBy(room => room, StringComparer.Ordinal))
                {
                    rooms.Add(new SharedMapRoom { Kind = i, Name = room });
                    if (rooms.Count < MaxRooms) continue;
                    yield return Batch();
                    emitted = true;
                    rooms.Clear();
                }
            }
            if (rooms.Count > 0 || !emitted) yield return Batch();

            SharedMapMessage Batch() => new SharedMapMessage {
                Kind = kind, Token = token, Epoch = epoch, Flags = state.Flags,
                Quill = state.Quill, Rooms = rooms.ToArray()
            };
        }
    }
}
