using System;
using System.Collections.Generic;
using System.Linq;
using SSMP.Networking.Packet;
using SSMP.Networking.Packet.Data;

namespace Silksong.CoopAssist
{
    public static class AppearanceWire
    {
        // Addon envelopes do not encode a collection marker. Each direction's factory must match its writer.
        public static IPacketData ServerData(AssistPacket id) => id switch {
            AssistPacket.HostSnapshot=>new HostSnapshotMessage(),
            AssistPacket.TeamTravel=>new PacketDataCollection<TeamTravelMessage>(),
            AssistPacket.HostAnchor=>new HostAnchor(),
            AssistPacket.Appearance=>new Appearance(),
            AssistPacket.NameStyle=>new NameStyleMessage(),
            AssistPacket.SharedMaps=>new PacketDataCollection<SharedMapMessage>(),
            AssistPacket.Rosaries=>new PacketDataCollection<RosaryMessage>(),
            AssistPacket.Equipment=>new PacketDataCollection<EquipmentMessage>(),
            _=>throw new ArgumentOutOfRangeException(nameof(id))
        };
        public static IPacketData ClientData(AssistPacket id) => id switch {
            AssistPacket.HostSnapshot=>new HostSnapshotMessage(),
            AssistPacket.TeamTravel=>new PacketDataCollection<TeamTravelMessage>(),
            AssistPacket.HostAnchor=>new HostAnchor(),
            AssistPacket.Appearance=>new PacketDataCollection<Appearance>(),
            AssistPacket.NameStyle=>new PacketDataCollection<NameStyleMessage>(),
            AssistPacket.SharedMaps=>new PacketDataCollection<SharedMapMessage>(),
            AssistPacket.Rosaries=>new PacketDataCollection<RosaryMessage>(),
            AssistPacket.Equipment=>new PacketDataCollection<EquipmentMessage>(),
            _=>throw new ArgumentOutOfRangeException(nameof(id))
        };
        public static bool CanSend(bool connected, object addonId) => connected && addonId is byte;
    }
    public sealed class AppearanceLedger
    {
        readonly Dictionary<ushort,string> latest=new Dictionary<ushort,string>();
        readonly object gate=new object();
        public Appearance Publish(ushort authenticatedId,string colour)
        {
            if(!Appearance.Valid(colour))throw new ArgumentException("Invalid colour");
            lock(gate)latest[authenticatedId]=colour;
            return new Appearance{Id=authenticatedId,Colour=colour};
        }
        public Appearance[] Snapshot(){lock(gate)return latest.Select(p=>new Appearance{Id=p.Key,Colour=p.Value}).ToArray();}
        public void Remove(ushort id){lock(gate)latest.Remove(id);}
        public void Clear(){lock(gate)latest.Clear();}
    }
    public sealed class NameStyleLedger
    {
        readonly Dictionary<ushort,byte> latest=new Dictionary<ushort,byte>();
        readonly object gate=new object();
        public NameStyleMessage Publish(ushort authenticatedId,byte flags)
        {
            if(flags>1)throw new ArgumentException("Unsupported name style");
            lock(gate)latest[authenticatedId]=flags;
            return new NameStyleMessage{Id=authenticatedId,Flags=flags};
        }
        public NameStyleMessage[] Snapshot(){lock(gate)return latest.Select(p=>new NameStyleMessage{Id=p.Key,Flags=p.Value}).ToArray();}
        public void Remove(ushort id){lock(gate)latest.Remove(id);}
        public void Clear(){lock(gate)latest.Clear();}
    }
}
