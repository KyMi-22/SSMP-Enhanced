using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using SSMP.Networking.Packet;
using Silksong.CoopAssist;

// Recording game/network adapters execute the production map tick on three machines.
// Native packet read/write is real; Unity rendering, disk and sockets are simulated.
namespace UnityEngine { public sealed class GameObject {public bool activeInHierarchy=true;} }
namespace HarmonyLib
{
    public sealed class Harmony {public Harmony(string id){}public void Patch(object method,HarmonyMethod prefix=null,HarmonyMethod postfix=null){}}
    public sealed class HarmonyMethod {public HarmonyMethod(Type type,string name){}}
    public static class AccessTools {public static object Method(Type type,string name)=>type.GetMethod(name);}
}
public sealed class ShopItem
{
    public enum TypeFlags { Map = 2 }
    public bool IsPurchased;
    public TypeFlags Flags = TypeFlags.Map;
    public TypeFlags GetTypeFlags() => Flags;
}
public sealed class ShopItemStats {public ShopItem Item;}
public sealed class HeroController
{public static HeroController instance=new HeroController();public UnityEngine.GameObject gameObject=new UnityEngine.GameObject();}
public sealed class GameMap
{
    public int Refreshes, Draws;
    public void SetupMap(bool force) { Refreshes++; }
    public bool UpdateGameMap() { Draws++; return true; }
}
public sealed class GameManager {public static GameManager instance;public GameMap gameMap=new GameMap();}
public sealed class PlayerData
{
    public static PlayerData instance;
    public int health=5,QuillState,geo=77;
    public bool controlReqlinquished;
    public bool HasMossGrottoMap,HasWildsMap,HasBoneforestMap,HasDocksMap,HasGreymoorMap,HasBellhartMap,
        HasShellwoodMap,HasCrawlMap,HasHuntersNestMap,HasJudgeStepsMap,HasDustpensMap,HasSlabMap,
        HasPeakMap,HasCitadelUnderstoreMap,HasCoralMap,HasSwampMap,HasCloverMap,HasAbyssMap,HasHangMap,
        HasSongGateMap,HasHallsMap,HasWardMap,HasCogMap,HasLibraryMap,HasCradleMap,HasArboriumMap,
        HasAqueductMap,HasWeavehomeMap,hasQuill;
    public HashSet<string> scenesVisited=new HashSet<string>(),scenesMapped=new HashSet<string>(),
        scenesEncounteredBench=new HashSet<string>(),scenesEncounteredCocoon=new HashSet<string>{"my-personal-cocoon"};
}
namespace Silksong.CoopAssist
{
    public static class PlayerMotionRepairs {public static double Now;}
    public static class NativeSaveBridge {public static bool Writing;public static bool IsWriting()=>Writing;}
    public static class AppearanceWire {public static bool CanSend(bool connected,object id)=>connected && id is byte;}
    public sealed class MapTestApi {public MapTestNet NetClient=new MapTestNet();}
    public sealed class MapTestNet {public bool IsConnected=true,IsStarted=true;}
    public sealed class MapTestServer {public MapTestNet NetServer=new MapTestNet();public MapTestPlayers ServerManager=new MapTestPlayers();}
    public sealed class MapTestPlayers {public HashSet<ushort> Players=new HashSet<ushort>();public object GetPlayer(ushort id)=>Players.Contains(id)?this:null;}
    public sealed class MapTestSender
    {
        public Action<SharedMapMessage,ushort> Send;
        public readonly List<int> Sizes=new List<int>();
        public void SendCollectionData(AssistPacket kind,SharedMapMessage data,ushort peer=0)
        {
            var packet=SharedMapTests.Packet();data.WriteData(packet);var bytes=SharedMapTests.Bytes(packet);Sizes.Add(bytes.Length);
            var copy=new SharedMapMessage();copy.ReadData(SharedMapTests.Packet(bytes));Send(copy,peer);
        }
    }
    public sealed class MapTestLog {public readonly List<string> Errors=new List<string>();public void LogInfo(string value){}public void LogError(string value){Errors.Add(value);}}
    public sealed class MapTestOwner {public ICollection<ushort> Members=new HashSet<ushort>();}
    public sealed partial class CoopAssistPlugin
    {
        public static CoopAssistPlugin Instance;
        bool compatible=true;
        public MapTestApi api=new MapTestApi();public MapTestServer server;
        public MapTestSender sender=new MapTestSender(),appearanceSender=new MapTestSender();
        public MapTestOwner coop=new MapTestOwner();
        FieldInfo checkedField=typeof(MapTestOwner).GetField("Members");
        object outboundAddon=new object();
        bool applyingSnapshot=false;
        string snapshotToken="";
        public bool Hosting,Down;
        public static bool Loading;
        public string SaveIdentity="slot-1";
        public MapTestLog Logger=new MapTestLog();
        public PlayerData Data=new PlayerData();public GameManager Manager=new GameManager();
        public int Checkpoints;public List<string> Notices=new List<string>();
        static object Value(object owner,string field)=>(byte)0;
        bool Checked(ushort peer)=>coop.Members.Contains(peer);
        void Say(string value){Notices.Add(value);}
        void ScheduleCheckpoint(){Checkpoints++;}
        public void Tick(){Instance=this;PlayerData.instance=Data;GameManager.instance=Manager;TickSharedMaps();}
        public string Token=>mapToken;
        public string Epoch=>mapEpoch;
    }
}
static class SharedMapTests
{
    static int checks;
    static void Check(bool condition,string reason){checks++;if(!condition)throw new Exception(reason);}
    static void Refused(Action action,string reason)
    {try{action();}catch(ArgumentException){checks++;return;}throw new Exception(reason);}
    internal static IPacket Packet(byte[] bytes=null)
    {
        var type=typeof(IPacket).Assembly.GetType("SSMP.Networking.Packet.Packet",true);
        return (IPacket)Activator.CreateInstance(type,bytes==null?Array.Empty<object>():new object[]{bytes});
    }
    internal static byte[] Bytes(IPacket p)=>(byte[])p.GetType().GetMethod("ToArray").Invoke(p,null);
    static void Main()
    {
        foreach(var name in SharedMapProgress.OwnershipFields)
        {
            var data=new PlayerData();typeof(PlayerData).GetField(name).SetValue(data,true);
            var state=NativeMapBridge.Capture(data);int index=Array.IndexOf(SharedMapProgress.OwnershipFields,name);
            Check(state.Flags==(1u<<index),"typed read bit: "+name);
            var other=new PlayerData();Check(NativeMapBridge.Apply(other,state),"grant changes map: "+name);
            Check((bool)typeof(PlayerData).GetField(name).GetValue(other),"typed grant field: "+name);
            Check(!NativeMapBridge.Apply(other,state),"grant is idempotent: "+name);
        }
        var host=new JObject{["HasMossGrottoMap"]=true,["hasQuill"]=true,["QuillState"]=2,
            ["scenesVisited"]=new JArray("Moss_01"),["scenesMapped"]=new JArray("Moss_01"),["scenesEncounteredBench"]=new JArray("Moss_Bench")};
        var client=new JObject{["HasAbyssMap"]=true,["HasMossGrottoMap"]=false,["geo"]=123,
            ["scenesVisited"]=new JArray("Another_World"),["scenesEncounteredCocoon"]=new JArray("my-cocoon")};
        HostMapSync.Align(host,client);
        Check((bool)client["HasMossGrottoMap"] && !(bool)client["HasAbyssMap"],"join replaces maps instead of leaking previous world");
        Check(client["scenesVisited"].Values<string>().SequenceEqual(new[]{"Moss_01"}),"host explored rooms replace stale guest rooms");
        Check((int)client["geo"]==123 && (string)client["scenesEncounteredCocoon"][0]=="my-cocoon","currency and personal cocoon unchanged");
        Check((bool)client["hasQuill"] && (int)client["QuillState"]==2,"drawing tool follows host");
        var before=client.ToString();HostMapSync.Align(host,client);Check(before==client.ToString(),"F9 and repeated joins are idempotent");
        ((JArray)host["scenesMapped"]).Add("Moss_02");Check(((JArray)client["scenesMapped"]).Count==1,"snapshots do not alias host arrays");
        HostMapSync.Align(new JObject(),client);Check(!(bool)client["HasMossGrottoMap"] && !client["scenesMapped"].Any(),"missing host fields use empty defaults");
        Refused(()=>HostMapSync.Align(new JObject{["HasMossGrottoMap"]=1},new JObject()),"malformed map flag refused");
        Refused(()=>HostMapSync.Align(new JObject{["scenesVisited"]=new JObject()},new JObject()),"malformed rooms refused");
        Refused(()=>HostMapSync.Align(new JObject{["scenesMapped"]=new JArray(7)},new JObject()),"non-room data refused");
        var token=Guid.NewGuid().ToString("N");var epoch=Guid.NewGuid().ToString("N");
        var progress=new SharedMapProgress{Flags=SharedMapProgress.ValidFlags,Quill=2};
        for(int i=0;i<1500;i++){progress.Rooms[0].Add("Scene_"+i);progress.Rooms[1].Add("Scene_"+i);}
        var batches=SharedMapMessage.Batches(SharedMapKind.State,token,epoch,progress).ToArray();
        var received=new SharedMapProgress();
        foreach(var batch in batches.Reverse())
        {
            var packet=Packet();batch.WriteData(packet);var copy=new SharedMapMessage();copy.ReadData(Packet(Bytes(packet)));
            Check(copy.Valid && copy.Rooms.Length<=32 && Bytes(packet).Length<17000,"bounded native wire batch");
            received.Merge(copy.Progress());Check(!received.Merge(copy.Progress()),"duplicate reliable batch is harmless");
        }
        Check(received.Same(progress),"out-of-order large explored map converges without truncation");
        Check(SharedMapMessage.Batches(SharedMapKind.Offer,token,epoch,progress,progress).Single().Rooms.Length==0,"unchanged map avoids re-sending explored rooms");
        Refused(()=>new SharedMapMessage{Kind=SharedMapKind.Offer,Token=token,Epoch=epoch,Flags=1u<<31}.WriteData(Packet()),"unknown map flags refused");
        Refused(()=>new SharedMapMessage{Kind=SharedMapKind.State,Token=token,Epoch=epoch,Rooms=new[]{new SharedMapRoom{Kind=3,Name="x"}}}.WriteData(Packet()),"personal map categories cannot enter wire");
        var a=new CoopAssistPlugin{Hosting=true,server=new MapTestServer()};
        var b=new CoopAssistPlugin();var c=new CoopAssistPlugin();
        var peers=new Dictionary<ushort,CoopAssistPlugin>{{0,a},{1,b},{2,c}};
        a.server.ServerManager.Players.UnionWith(peers.Keys);a.coop.Members=new HashSet<ushort>{1,2};
        b.coop.Members=new HashSet<ushort>{0,2};c.coop.Members=new HashSet<ushort>{0,1};
        a.sender.Send=(data,id)=>peers[id].QueueMapState(data);
        b.appearanceSender.Send=(data,id)=>a.QueueMapOffer(1,data);c.appearanceSender.Send=(data,id)=>a.QueueMapOffer(2,data);
        double now=0;
        void Run(int frames){for(int f=0;f<frames;f++){PlayerMotionRepairs.Now=now;foreach(var peer in peers.Values)peer.Tick();now+=.05;}}
        a.Data.HasMossGrottoMap=true;a.Data.scenesMapped.Add("Moss_01");
        a.Data.scenesVisited.Add(new string('x', 129));
        Run(100);
        Check(b.Data.HasMossGrottoMap && c.Data.HasMossGrottoMap,"host map reaches both guests without movement readiness");
        Check(b.Data.scenesMapped.Contains("Moss_01") && c.Data.scenesMapped.Contains("Moss_01"),"host explored map reaches both guests");
        Check(b.Manager.gameMap.Draws==1 && b.Manager.gameMap.Refreshes>0,"new ownership invokes native mapping and refresh");
        Check(a.Data.scenesVisited.Count==1 && b.Data.scenesVisited.Count==0,
            "an invalid historical room name stays local without blocking valid records");
        b.Data.controlReqlinquished=true;b.Data.HasCoralMap=true;b.Data.scenesVisited.Add("Coral_32");b.Data.scenesMapped.Add("Coral_32");
        c.Data.scenesEncounteredBench.Add("Bellway_08");Run(120);
        Check(a.Data.HasCoralMap && c.Data.HasCoralMap,"guest purchase shares while buyer stays in shop");
        Check(a.Data.scenesMapped.Contains("Coral_32") && c.Data.scenesVisited.Contains("Coral_32"),"guest exploration is shared");
        Check(a.Data.scenesEncounteredBench.Contains("Bellway_08") && b.Data.scenesEncounteredBench.Contains("Bellway_08"),"bench discoveries are shared");
        Check(peers.Values.All(p=>p.Data.geo==77 && p.Data.scenesEncounteredCocoon.SetEquals(new[]{"my-personal-cocoon"})),"shared map never spends recipients' money or replaces cocoons");
        var checkpoint=a.Checkpoints;Run(100);Check(a.Checkpoints==checkpoint,"idle map cannot request a save every tick");
        var stale=new SharedMapMessage{Kind=SharedMapKind.Offer,Token=Guid.NewGuid().ToString("N"),Epoch=a.Epoch,Flags=1u<<17};
        a.QueueMapOffer(1,stale);Run(10);Check(!a.Data.HasAbyssMap,"another session nonce cannot add assets");
        stale.Token=b.Token;stale.Epoch=Guid.NewGuid().ToString("N");a.QueueMapOffer(1,stale);Run(10);
        Check(!a.Data.HasAbyssMap,"old host epoch cannot add assets");
        var stranger=new CoopAssistPlugin();stranger.Data.HasAbyssMap=true;
        int sent=0;stranger.appearanceSender.Send=(data,id)=>sent++;
        for(int i=0;i<100;i++){PlayerMotionRepairs.Now=now;stranger.Tick();now+=.05;}
        Check(sent==0 && stranger.Token=="","unchecked save cannot publish old-world maps");
        var oldToken=b.Token;var oldEpoch=a.Epoch;
        b.Data=new PlayerData();Run(120);
        Check(b.Token!=oldToken && b.Data.HasCoralMap,"reloaded guest requests fresh maps and restores shared assets");
        var late=new SharedMapMessage{Kind=SharedMapKind.State,Token=oldToken,Epoch=oldEpoch,Flags=1u<<17};b.QueueMapState(late);Run(10);
        Check(!b.Data.HasAbyssMap,"late response from previous loaded save is ignored");
        a.Data.HasCoralMap=false;a.Data.scenesMapped.Clear();Run(20);
        Check(a.Data.HasCoralMap && a.Data.scenesMapped.Contains("Coral_32"),"native stale overwrite is repaired during same session");

        int roomOffers = 0;
        b.appearanceSender.Send = (message, id) =>
        {
            if (message.Kind == SharedMapKind.Offer && message.Rooms.Any(room => room.Name == "Lost_Visit"))
                if (++roomOffers == 1) return;
            a.QueueMapOffer(1, message);
        };
        b.Data.scenesVisited.Add("Lost_Visit");
        Run(150);
        Check(roomOffers >= 2 && a.Data.scenesVisited.Contains("Lost_Visit") && c.Data.scenesVisited.Contains("Lost_Visit"),
            "guest retries exploration until host confirms and shares it");
        var confirmedOffers = roomOffers;
        int hostOnlyEchoes = 0;
        b.appearanceSender.Send = (message, id) =>
        {
            if (message.Kind == SharedMapKind.Offer && message.Rooms.Any(room => room.Name == "Lost_Visit")) roomOffers++;
            if (message.Kind == SharedMapKind.Offer && message.Rooms.Any(room => room.Name == "Host_Only")) hostOnlyEchoes++;
            a.QueueMapOffer(1, message);
        };
        a.Data.scenesVisited.Add("Host_Only");
        var drawsBeforeExplore = b.Manager.gameMap.Draws;
        Run(350);
        Check(roomOffers == confirmedOffers, "confirmed guest exploration stops retrying");
        Check(b.Data.scenesVisited.Contains("Host_Only") && hostOnlyEchoes == 0, "host-only exploration is not echoed back");
        Check(b.Manager.gameMap.Draws == drawsBeforeExplore, "normal exploration retains native drawing timing");

        b.QueueMapState(new SharedMapMessage { Kind = SharedMapKind.State, Token = b.Token,
            Epoch = b.Epoch, Rooms = new[]{new SharedMapRoom { Kind=1, Name="Save_Then_Apply" }} });
        NativeSaveBridge.Writing = true;
        Run(10);
        Check(!b.Data.scenesMapped.Contains("Save_Then_Apply"), "map application waits while a native save is serializing");
        NativeSaveBridge.Writing = false;
        Run(20);
        Check(b.Data.scenesMapped.Contains("Save_Then_Apply"), "map application resumes after the native save");

        var activeToken = b.Token;
        b.QueueMapState(new SharedMapMessage { Kind = SharedMapKind.State, Token = activeToken,
            Epoch = Guid.NewGuid().ToString("N"), Flags = 1u << 17 });
        Run(30);
        Check(b.Token != activeToken && b.Epoch == a.Epoch && !b.Data.HasAbyssMap,
            "a conflicting epoch is verified with a fresh token before applying any records");
        Check(peers.Values.All(p=>p.Logger.Errors.Count==0),"production map runtime has no adapter errors");

        int sendAttempts = 0;
        a.sender.Send = (message, id) =>
        {
            if (id == 1 && message.Rooms.Any(room => room.Name == "Native_Queue_Retry") && ++sendAttempts == 1)
                throw new InvalidOperationException("Simulated native collection rejection");
            peers[id].QueueMapState(message);
        };
        a.Data.scenesMapped.Add("Native_Queue_Retry");
        Run(50);
        Check(sendAttempts == 2 && b.Data.scenesMapped.Contains("Native_Queue_Retry") && a.Logger.Errors.Count == 1,
            "a native enqueue exception leaves the same host batch queued for a quick retry");
        Check(peers.Values.All(p=>p.sender.Sizes.Concat(p.appearanceSender.Sizes).All(s=>s<17000)),"all three-machine packets stay bounded");
        CoopAssistPlugin.Instance=b;
        var prefix=typeof(SharedMapShop).GetMethod("BeforePurchase",BindingFlags.NonPublic|BindingFlags.Static);
        var canBuy=typeof(SharedMapShop).GetMethod("CanBuy",BindingFlags.NonPublic|BindingFlags.Static);
        var ownedMap=new ShopItem{IsPurchased=true};int callbacks=0;
        bool proceed=(bool)prefix.Invoke(null,new object[]{ownedMap,(Action)(()=>callbacks++)});
        Check(!proceed && callbacks==1,"stale map purchase stops before charge and releases waiting UI callback");
        var args=new object[]{new ShopItemStats{Item=ownedMap},true};
        Check(!(bool)canBuy.Invoke(null,args) && !(bool)args[1],"open shop cannot buy a newly shared map");
        Check((bool)prefix.Invoke(null,new object[]{new ShopItem{IsPurchased=false},null}),"unowned map retains original native purchase and price");
        Check((bool)prefix.Invoke(null,new object[]{new ShopItem{IsPurchased=true,Flags=0},null}),"non-map purchases keep native behavior");
        b.api.NetClient.IsConnected=false;
        Check((bool)prefix.Invoke(null,new object[]{ownedMap,null}),"offline shop remains native");
        Console.WriteLine("Passed "+checks+" shared-map snapshot, native codec and three-player production-tick checks. Unity/disk/sockets simulated.");
    }
}
