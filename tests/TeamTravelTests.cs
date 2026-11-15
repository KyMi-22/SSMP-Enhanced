using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using SSMP.Networking.Packet;
using SSMP.Networking.Packet.Data;
using Silksong.CoopAssist;

static class TeamTravelTests
{
    static int checks;
    static void Check(bool value,string why){checks++;if(!value)throw new Exception(why);}
    static TeamTravelMessage Request(ushort target)=>new TeamTravelMessage{Kind=TravelKind.Request,Peer=target,Token=Guid.NewGuid().ToString("N")};
    static TeamTravelMessage Reply(TeamTravelMessage routed)=>new TeamTravelMessage{Kind=TravelKind.Destination,Peer=routed.Peer,Token=routed.Token,Scene="Bone_11",Gate="left1",X=12,Y=4};
    static TeamTravelMessage Roundtrip(TeamTravelMessage message)
    {
        var native=typeof(IPacket).Assembly.GetType("SSMP.Networking.Packet.Packet",true);
        var writer=(IPacket)Activator.CreateInstance(native);message.WriteData(writer);
        var bytes=(byte[])native.GetMethod("ToArray").Invoke(writer,null);
        var read=new TeamTravelMessage();read.ReadData((IPacket)Activator.CreateInstance(native,new object[]{bytes}));
        return read;
    }
    static MethodInfo Method(Type t,string name)
    {for(;t!=null;t=t.BaseType){var m=t.GetMethod(name,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static|BindingFlags.DeclaredOnly);if(m!=null)return m;}throw new Exception(name);}
    static void Envelope(string direction)
    {
        var native=typeof(IPacket).Assembly;
        var packet=native.GetType("SSMP.Networking.Packet.Packet",true);
        var addon=native.GetType("SSMP.Networking.Packet.AddonPacketData",true);
        var info=native.GetType("SSMP.Networking.Packet.AddonPacketInfo",true);
        var shell=native.GetType("SSMP.Networking.Packet.Update."+direction,true);
        var writer=(IPacket)Activator.CreateInstance(packet);
        var group=new PacketDataCollection<TeamTravelMessage>();
        group.DataInstances.Add(Request(0));group.DataInstances.Add(Request(2));
        var add=Activator.CreateInstance(addon,new object[]{(byte)3});
        ((IDictionary)addon.GetProperty("PacketData").GetValue(add)).Add((byte)AssistPacket.TeamTravel,group);
        var dictionary=typeof(System.Collections.Generic.Dictionary<,>).MakeGenericType(typeof(byte),addon);
        var input=(IDictionary)Activator.CreateInstance(dictionary);input.Add((byte)0,add);
        var instance=Activator.CreateInstance(shell);
        Method(shell,"WriteAddonDataDict").Invoke(instance,new object[]{writer,input});
        var bytes=(byte[])packet.GetMethod("ToArray").Invoke(writer,null);
        var reader=Activator.CreateInstance(packet,new object[]{bytes});
        Func<byte,IPacketData> factory=id=>direction=="ServerUpdatePacket"?AppearanceWire.ServerData((AssistPacket)id):AppearanceWire.ClientData((AssistPacket)id);
        ((IDictionary)Method(shell,"get_AddonPacketInfoDict").Invoke(null,null))[(byte)0]=Activator.CreateInstance(info,new object[]{factory,(byte)3});
        var output=(IDictionary)Activator.CreateInstance(dictionary);
        Method(shell,"ReadAddonDataDict").Invoke(instance,new object[]{reader,output});
        var result=(IDictionary)addon.GetProperty("PacketData").GetValue(output[(byte)0]);
        var messages=((RawPacketDataCollection)result[(byte)AssistPacket.TeamTravel]).DataInstances.Cast<TeamTravelMessage>().ToArray();
        Check(messages.Length==2,"concurrent travel messages survive "+direction);
        for(int i=0;i<2;i++)Check(messages[i].Token==((TeamTravelMessage)group.DataInstances[i]).Token && messages[i].Peer==((TeamTravelMessage)group.DataInstances[i]).Peer,"exact nonce/target preserved");
    }
    static int Main()
    {
        try{Run();return 0;}catch(Exception error){Console.Error.WriteLine(error);return 1;}
    }
    static void Run()
    {
        Envelope("ServerUpdatePacket");Envelope("ClientUpdatePacket");
        for(ushort source=0;source<3;source++)for(ushort target=0;target<3;target++)
        {
            var ledger=new TeamTravelLedger();var request=Request(target);
            var accepted=ledger.Relay(source,request,10,out var recipient,out var routed);
            Check(accepted==(source!=target),"all host/client directions and self rejection");
            if(source==target)continue;
            Check(recipient==target && routed.Peer==source && request.Peer==target,"authenticated sender, routing and no incoming mutation");
            var reply=Reply(routed);
            Check(!ledger.Relay((ushort)(3-source-target),reply,10.1,out _,out _),"third player cannot forge target reply");
            var bad=Reply(routed);bad.Token=Guid.NewGuid().ToString("N");
            Check(!ledger.Relay(target,bad,10.2,out _,out _),"wrong nonce cannot consume pending request");
            Check(ledger.Relay(target,reply,10.3,out recipient,out var destination) && recipient==source && destination.Peer==target,"reply delivered only to requester");
            Check(!ledger.Relay(target,reply,10.4,out _,out _),"replayed reply rejected");
        }
        var concurrent=new TeamTravelLedger();
        var a=Request(1);var b=Request(1);
        Check(concurrent.Relay(0,a,20,out _,out var toA),"first request");
        Check(concurrent.Relay(2,b,20,out _,out var toB),"second simultaneous request to same teammate");
        Check(concurrent.Relay(1,Reply(toB),21,out var to,out _) && to==2,"out of order reply B");
        Check(concurrent.Relay(1,Reply(toA),21,out to,out _) && to==0,"out of order reply A");
        Check(concurrent.Relay(0,Request(1),30,out _,out var timeout),"timeout setup");
        Check(!concurrent.Relay(1,Reply(timeout),38.1,out _,out _),"expired reply rejected");
        Check(!concurrent.Relay(1,Reply(timeout),29,out _,out _),"backward time rejected");
        Check(concurrent.Relay(0,Request(1),40,out _,out var removed),"disconnect setup");
        concurrent.Remove(1);Check(!concurrent.Relay(1,Reply(removed),41,out _,out _),"target departure cancels routing");
        Check(concurrent.Relay(0,Request(1),50,out _,out var cleared),"shutdown setup");
        concurrent.Clear();Check(!concurrent.Relay(1,Reply(cleared),51,out _,out _),"shutdown cancels routing");
        var invalid=Request(1);invalid.Token="bad";Check(!invalid.Valid,"invalid token");
        invalid=Request(1);invalid.X=float.NaN;Check(!invalid.Valid,"NaN rejected");
        invalid=Request(1);invalid.Kind=(TravelKind)255;Check(!invalid.Valid,"unknown operation rejected");
        invalid=Reply(Request(1));invalid.Scene="";Check(!invalid.Valid,"destination needs a scene");
        invalid=Reply(Request(1));invalid.Gate=new string('x',128);Check(!invalid.Valid,"gate bound");
        invalid=Reply(Request(1));invalid.Reason=(TravelUnavailableReason)255;Check(!invalid.Valid,"unknown refusal reason rejected");
        var refused=Request(1);refused.Kind=TravelKind.Unavailable;refused.Reason=TravelUnavailableReason.UnsafeGround;
        var refusal=Roundtrip(refused);
        Check(refusal.Valid && refusal.Kind==TravelKind.Unavailable && refusal.Reason==TravelUnavailableReason.UnsafeGround,
            "refusal reason survives the wire");
        var moved=refused.Route(4);Check(moved.Reason==TravelUnavailableReason.UnsafeGround && moved.Peer==4,"relay keeps the refusal reason");
        var rate=new TeamTravelLedger();var first=Request(1);
        Check(rate.Relay(0,first,100,out _,out var original),"rate setup");
        Check(!rate.Relay(0,Request(2),100.5,out _,out _),"new request cannot override within rate interval");
        Check(rate.Relay(1,Reply(original),100.6,out _,out _),"old reply stays valid after denied override");
        for(int i=0;i<500;i++)
        {
            var msg=Reply(Request(1));msg.X=i;msg.Y=-i;msg.Reason=(TravelUnavailableReason)(i%3);
            var native=typeof(IPacket).Assembly.GetType("SSMP.Networking.Packet.Packet",true);
            var writer=(IPacket)Activator.CreateInstance(native);msg.WriteData(writer);
            var bytes=(byte[])native.GetMethod("ToArray").Invoke(writer,null);
            var read=new TeamTravelMessage();read.ReadData((IPacket)Activator.CreateInstance(native,new object[]{bytes}));
            Check(read.Valid && read.Kind==msg.Kind && read.Reason==msg.Reason && read.Token==msg.Token && read.Peer==msg.Peer && read.Scene==msg.Scene && read.Gate==msg.Gate && read.X==msg.X && read.Y==msg.Y,"real packet codec round trip");
        }
        Console.WriteLine("Passed "+checks+" travel routing/ownership/replay/disconnect/codec checks. Native addon envelopes, no sockets or Unity runtime.");
    }
}
