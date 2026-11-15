using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using SSMP.Networking.Packet;
using Silksong.CoopAssist;

static class SessionRecoveryTests
{
    static int checks;
    static void Check(bool value,string why){checks++;if(!value)throw new Exception(why);}
    static void Refused(Action action,string why)
    {try{action();}catch(ArgumentException){checks++;return;}throw new Exception(why);}
    static IPacket Packet(byte[] bytes=null)
    {
        var type=typeof(IPacket).Assembly.GetType("SSMP.Networking.Packet.Packet",true);
        return (IPacket)Activator.CreateInstance(type,bytes==null?Array.Empty<object>():new object[]{bytes});
    }
    static byte[] Bytes(IPacket packet)=>(byte[])packet.GetType().GetMethod("ToArray").Invoke(packet,null);
    static JObject Save(params JObject[] items)=>new JObject{["playerData"]=new JObject{
        ["Collectables"]=new JObject{["savedData"]=new JArray(items)},["geo"]=9,["maxHealth"]=5,["health"]=4},
        ["sceneData"]=new JObject{["persistentBools"]=new JObject{["serializedList"]=new JArray()}}};
    static JObject Item(string name,int amount)=>new JObject{["Name"]=name,["Data"]=new JObject{["Amount"]=amount}};
    static JObject World(string id,bool value)=>new JObject{["SceneName"]="Mosstown_01",["ID"]=id,["Value"]=value,["Mutator"]=0};
    static void Main()
    {
        var schedule=new CheckpointSchedule();
        Check(!schedule.TryBegin(0,false,out _),"unsafe state cannot save");
        Check(schedule.TryBegin(0,true,out var ticket),"first aligned load saves immediately");
        Check(!schedule.TryBegin(100,true,out _),"only one writer even after deadline");
        schedule.Complete(ticket,1,false);
        Check(!schedule.TryBegin(5,true,out _),"retry is bounded");
        Check(schedule.TryBegin(6,true,out ticket),"failed write retries");
        schedule.Complete(ticket,7,true);
        Check(!schedule.TryBegin(36,true,out _),"success intervals from completion");
        Check(schedule.TryBegin(37,true,out ticket),"next checkpoint");
        schedule.Reset();var old=ticket;
        Check(schedule.TryBegin(38,true,out ticket),"new profile starts anew");
        schedule.Complete(old,39,true);
        Check(schedule.Saving,"stale callback cannot acknowledge new profile");
        schedule.Complete(ticket,40,true);schedule.Request();
        Check(schedule.TryBegin(40,true,out _),"explicit leaving flush bypasses interval");
        schedule.Reset();
        Check(schedule.TryBegin(41,true,out ticket),"checkpoint write begins before remote progress arrives");
        schedule.Request();
        schedule.Complete(ticket,42,true);
        Check(schedule.TryBegin(42,true,out ticket),"remote progress arriving during a write gets another checkpoint");
        schedule.Request();
        schedule.Complete(ticket,43,false);
        Check(!schedule.TryBegin(44,true,out _) && schedule.TryBegin(48,true,out _),"failed checkpoint still uses bounded retry");
        var point=new Waypoint{Scene="room",Gate="left1",X=1,Y=2};
        Check(point.Valid,"ordinary saved waypoint");
        Check(!new Waypoint{Scene="room",Gate="",X=1,Y=2}.Valid,"unverified entrance refused");
        Check(!new Waypoint{Scene="room",Gate="left1",X=float.NaN}.Valid,"invalid coordinate refused");
        var trip=new WaypointTrip("save-a","bench",point,10);
        Check(trip.CanContinue("save-a","bench",11,false),"explicit request waits for control at its origin");
        Check(!trip.CanContinue("save-a",null,11,true),"unrelated transition before travel cancels");
        Check(!trip.CanContinue("save-a","room",11,false),"walking into the target does not finish another request");
        Check(!trip.CanContinue("save-a","bench",16,false),"waiting to resume is bounded");
        trip.BeginTransition(12);
        Check(trip.CanContinue("save-a",null,20,true),"own transition may load before validation");
        Check(trip.CanContinue("save-a","room",20,false),"only the expected destination may finish travel");
        Check(!trip.CanContinue("save-a","other",20,false),"wrong destination cancels");
        Check(!trip.CanContinue("save-b","room",20,false),"changing save cancels");
        Check(!trip.CanContinue("save-a","room",37,false),"scene load has its own timeout");
        var migrated=Newtonsoft.Json.JsonConvert.DeserializeObject<Waypoint>(
            "{\"Scene\":\"room\",\"Gate\":\"left1\",\"X\":1,\"Y\":2,\"Valid\":true}");
        Check(migrated.Valid && migrated.X==point.X && migrated.Gate==point.Gate,"old respawn-point JSON loads as a waypoint");
        var token=Guid.NewGuid().ToString("N");
        foreach(var length in new[]{20,65534,65536,140000,1000000})
        {
            var json=new JObject{["text"]=new string('a',length)}.ToString();
            var message=HostSnapshotMessage.FromJson(token,json);
            var writer=Packet();message.WriteData(writer);var bytes=Bytes(writer);
            var copy=new HostSnapshotMessage();copy.ReadData(Packet(bytes));
            Check(copy.Valid && copy.Token==token && JObject.Parse(copy.Json())["text"].ToString().Length==length,"snapshot codec preserves large and padded saves");
            var builder=typeof(IPacket).Assembly.GetType("SSMP.Networking.Packet.ChunkAddonPacketBuilder",true);
            var chunk=(IPacket)builder.GetMethod("BuildClientBound").Invoke(null,new object[]{(byte)AssistPacket.HostSnapshot,(byte)2,message});
            Check(Bytes(chunk).Length>65535,"actual native chunk builder accepts snapshot");
        }
        var malformed=Packet();malformed.Write((byte)SnapshotKind.Snapshot);malformed.Write(token);malformed.Write(-1);
        Refused(()=>new HostSnapshotMessage().ReadData(Packet(Bytes(malformed))),"negative size must refuse before allocation");
        var huge=Packet();huge.Write((byte)SnapshotKind.Snapshot);huge.Write(token);huge.Write(HostSnapshotMessage.MaxBytes+1);
        Refused(()=>new HostSnapshotMessage().ReadData(Packet(Bytes(huge))),"oversized snapshot refused before allocation");
        Check(AppearanceWire.ServerData(AssistPacket.HostSnapshot) is HostSnapshotMessage && AppearanceWire.ClientData(AssistPacket.HostSnapshot) is HostSnapshotMessage,"matching factories both directions");
        var host=Save(Item("Mossberry",3),Item("Rosary_Set_Large",100));
        var client=Save(Item("Mossberry",1),Item("Rosary_Set_Large",4));
        host["playerData"]["visitedMosstown"]=true;host["playerData"]["pilbyMosstownConvo"]=true;
        host["playerData"]["mosstownAspidBerryCollected"]=true;
        var aligned=HostResourceSync.Align(host,client,client,new Dictionary<string,List<string>>());
        Check((bool)aligned["playerData"]["visitedMosstown"] && (bool)aligned["playerData"]["pilbyMosstownConvo"],"previously omitted town flags align");
        Check((bool)aligned["playerData"]["mosstownAspidBerryCollected"],"berry source flag and inventory from same snapshot");
        Check((int)aligned["playerData"]["Collectables"]["savedData"][0]["Data"]["Amount"]==3,"host berry quantity");
        Check((int)aligned["playerData"]["Collectables"]["savedData"][1]["Data"]["Amount"]==4,"personal rosary strings retained");
        var catalog=new ProgressSyncCatalog(Array.Empty<string>(),new Dictionary<string,System.Collections.Generic.IEnumerable<(string,string)>>{
            ["persistentBools"]=new[]{("mosstown_01","personal-platform")}});
        ((JArray)host["sceneData"]["persistentBools"]["serializedList"]).Add(World("door",true));
        ((JArray)aligned["sceneData"]["persistentBools"]["serializedList"]).Add(World("source",true));
        ((JArray)aligned["sceneData"]["persistentBools"]["serializedList"]).Add(World("personal-platform",true));
        var own=(JObject)aligned.DeepClone();
        HostWorldSync.Align(host,own,aligned,catalog);
        var objects=(JArray)aligned["sceneData"]["persistentBools"]["serializedList"];
        Check(objects.Count==2 && !objects.Any(x=>(string)x["ID"]=="source"),"classified pickup resets to host");
        Check(objects.Any(x=>(string)x["ID"]=="personal-platform"),"personal platform retained");
        var before=aligned.ToString();HostWorldSync.Align(host,aligned,aligned,catalog);
        Check(aligned.ToString()==before,"world alignment idempotent");
        Console.WriteLine("Passed "+checks+" checkpoint, native snapshot chunk/codec, town/resource and return-point checks. No Unity or sockets.");
    }
}
