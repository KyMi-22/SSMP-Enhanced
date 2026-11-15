using System;
using System.Collections.Generic;
using SSMP.Networking.Packet;

namespace Silksong.CoopAssist
{
    public enum TravelKind : byte { Request, Destination, Unavailable }
    // Why a teammate could not offer a landing spot. Only these two are shown to the requester.
    public enum TravelUnavailableReason : byte { None, Moving, UnsafeGround }

    // Collections in BOTH directions: simultaneous requests/replies must not overwrite one another.
    public sealed class TeamTravelMessage : IPacketData
    {
        public TravelKind Kind;
        public TravelUnavailableReason Reason;
        public string Token = "", Scene = "", Gate = "";
        public ushort Peer;
        public float X, Y;
        public bool IsReliable => true;
        public bool DropReliableDataIfNewerExists => false;
        public void WriteData(IPacket p)
        { p.Write((byte)Kind); p.Write((byte)Reason); p.Write(Token); p.Write(Peer); p.Write(Scene); p.Write(Gate); p.Write(X); p.Write(Y); }
        public void ReadData(IPacket p)
        { Kind=(TravelKind)p.ReadByte(); Reason=(TravelUnavailableReason)p.ReadByte(); Token=p.ReadString(); Peer=p.ReadUShort(); Scene=p.ReadString(); Gate=p.ReadString(); X=p.ReadFloat(); Y=p.ReadFloat(); }
        public bool Valid => Enum.IsDefined(typeof(TravelKind),Kind) && Enum.IsDefined(typeof(TravelUnavailableReason),Reason) &&
            Token?.Length==32 && Guid.TryParseExact(Token,"N",out _) &&
            Scene!=null && Gate!=null && Scene.Length<128 && Gate.Length<128 &&
            !float.IsNaN(X) && !float.IsNaN(Y) && !float.IsInfinity(X) && !float.IsInfinity(Y) &&
            Math.Abs(X)<100000 && Math.Abs(Y)<100000 &&
            (Kind!=TravelKind.Destination || !string.IsNullOrWhiteSpace(Scene));
        public TeamTravelMessage Route(ushort peer) => new TeamTravelMessage {
            Kind=Kind,Reason=Reason,Token=Token,Peer=peer,Scene=Scene,Gate=Gate,X=X,Y=Y
        };
    }

    // One outstanding request per authenticated sender; replies are bound to its exact target and nonce.
    public sealed class TeamTravelLedger
    {
        sealed class Entry { public ushort Target; public string Token; public double Since; }
        readonly Dictionary<ushort,Entry> pending=new Dictionary<ushort,Entry>();
        readonly object gate=new object();
        public bool Relay(ushort authenticated,TeamTravelMessage data,double now,out ushort recipient,out TeamTravelMessage routed)
        {
            recipient=0; routed=null;
            if(data==null || !data.Valid || authenticated==data.Peer) return false;
            lock(gate)
            {
                if(data.Kind==TravelKind.Request)
                {
                    if(pending.TryGetValue(authenticated,out var old) && now-old.Since<1) return false;
                    pending[authenticated]=new Entry{Target=data.Peer,Token=data.Token,Since=now};
                }
                else
                {
                    if(!pending.TryGetValue(data.Peer,out var request) || request.Target!=authenticated ||
                        request.Token!=data.Token || now-request.Since<0 || now-request.Since>8) return false;
                    pending.Remove(data.Peer);
                }
                recipient=data.Peer; routed=data.Route(authenticated); return true;
            }
        }
        public void Remove(ushort id)
        {
            lock(gate)
            {
                pending.Remove(id);
                var remove=new List<ushort>();
                foreach(var item in pending) if(item.Value.Target==id) remove.Add(item.Key);
                foreach(var key in remove) pending.Remove(key);
            }
        }
        public void Clear(){lock(gate)pending.Clear();}
    }
}
