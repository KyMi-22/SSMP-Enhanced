using System;
using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using SSMP.Api.Client;
using BepInEx.Logging;

namespace Silksong.CoopAssist
{
    internal static class NetworkDiagnostics
    {
        internal static readonly PeerFlowMetrics Metrics=new PeerFlowMetrics();
        static readonly DispatchDelayMeter hostDispatch=new DispatchDelayMeter();
        sealed class Destination {internal ushort Id;}
        static readonly ConditionalWeakTable<object,Destination> destinations=new ConditionalWeakTable<object,Destination>();
        static readonly Assembly ssmp=typeof(ClientAddon).Assembly;
        static Type T(string name)=>ssmp.GetType(name,true);
        static Func<object,bool> position;
        static Func<object,ushort> id,clientSequence,hostSequence;
        static Func<object,object> clientData,hostData;
        static Func<object,IList> instances;
        static double reportAt=PlayerMotionRepairs.Now;
        static bool installed;

        internal static void Install()
        {
            var player=T("SSMP.Networking.Packet.Data.PlayerUpdate");
            var clientPacket=T("SSMP.Networking.Packet.Update.ClientUpdatePacket");
            var hostPacket=T("SSMP.Networking.Packet.Update.ServerUpdatePacket");
            position=NativeFlowBindings.Position(player);id=NativeFlowBindings.Read<ushort>(player,"Id");
            clientSequence=NativeFlowBindings.Read<ushort>(clientPacket,"Sequence");
            hostSequence=NativeFlowBindings.Read<ushort>(hostPacket,"Sequence");
            clientData=NativeFlowBindings.PlayerData(clientPacket);hostData=NativeFlowBindings.PlayerData(hostPacket);
            instances=NativeFlowBindings.Read<IList>(T("SSMP.Networking.Packet.Data.RawPacketDataCollection"),"DataInstances");
            var h=new Harmony("silksong.coopassist.networkmetrics");
            Patch(h,T("SSMP.Networking.Packet.PacketManager"),"HandleClientUpdatePacket",nameof(ClientDecoded));
            Patch(h,T("SSMP.Networking.Packet.PacketManager"),"HandleServerUpdatePacket",nameof(HostDecoded));
            Patch(h,T("SSMP.Game.Server.ServerManager"),"OnPlayerUpdate",nameof(HostApplied));
            Patch(h,T("SSMP.Networking.Client.ClientUpdateManager"),"UpdatePlayerPosition",nameof(Sampled));
            var serverManager=T("SSMP.Networking.Server.ServerUpdateManager");
            h.Patch(AccessTools.Method(T("SSMP.Networking.Server.NetServer"),"GetUpdateManagerForClient"),
                postfix:new HarmonyMethod(typeof(NetworkDiagnostics),nameof(FoundDestination)));
            foreach(var manager in new[]{T("SSMP.Networking.Client.ClientUpdateManager"),serverManager})
                h.Patch(AccessTools.Method(manager,"SendBytes"),
                    prefix:new HarmonyMethod(typeof(NetworkDiagnostics),nameof(BeforeSend)),
                    postfix:new HarmonyMethod(typeof(NetworkDiagnostics),nameof(AfterSend)));
            installed=true;
        }
        static void Patch(Harmony h,Type type,string name,string prefix)
            =>h.Patch(AccessTools.Method(type,name),prefix:new HarmonyMethod(typeof(NetworkDiagnostics),prefix));
        static void ClientDecoded(object __0)
        {
            var collection=clientData(__0);if(collection==null)return;
            var data=instances(collection);var now=PlayerMotionRepairs.Now;var sequence=clientSequence(__0);
            for(var n=0;n<data.Count;n++)if(position(data[n]))
                Metrics.Observe(FlowStage.ClientReceive,id(data[n]),now,numbered:true,sequence:sequence);
        }
        static void HostDecoded(ushort __0,object __1)
        {
            var data=hostData(__1);if(data==null || !position(data))return;
            var now=PlayerMotionRepairs.Now;hostDispatch.Decoded(data,now);
            Metrics.Observe(FlowStage.HostReceive,__0,now,numbered:true,sequence:hostSequence(__1));
        }
        static void HostApplied(ushort __0,object __1)
        {
            if(!position(__1))return;
            var now=PlayerMotionRepairs.Now;
            Metrics.Observe(FlowStage.HostApply,__0,now,hostDispatch.Applied(__1,now));
        }
        internal static void ClientApplied(object data,double delay)
        {if(installed && position(data))Metrics.Observe(FlowStage.ClientApply,id(data),PlayerMotionRepairs.Now,delay);}
        static void Sampled()=>Metrics.Observe(FlowStage.Sample,-1,PlayerMotionRepairs.Now);
        static void FoundDestination(ushort __0,object __result)
        {if(__result!=null)destinations.GetValue(__result,_=>new Destination()).Id=__0;}
        static void BeforeSend(out double __state)=>__state=PlayerMotionRepairs.Now;
        static void AfterSend(object __instance,double __state)
        {
            var now=PlayerMotionRepairs.Now;
            if(destinations.TryGetValue(__instance,out var destination))
                Metrics.Observe(FlowStage.ServerSend,destination.Id,now,Math.Max(0,now-__state));
            else Metrics.Observe(__instance.GetType().Name=="ClientUpdateManager"?FlowStage.ClientSend:FlowStage.ServerSend,-1,now,Math.Max(0,now-__state));
        }
        internal static void Report(ManualLogSource logger,string scene)
        {
            var now=PlayerMotionRepairs.Now;var seconds=Math.Max(.001,now-reportAt);reportAt=now;
            var rows=Metrics.ReadAndReset();hostDispatch.ReadAndReset();if(rows.Length==0)return;
            var buffers=ReceiveScratch.ReadAndReset();
            if(buffers.Created+buffers.Reused>0)logger.LogInfo("[ReceiveAlloc] buffers="+buffers.Created+" timeoutReuses="+buffers.Reused+" successfulHandOffs="+buffers.HandedOff);
            logger.LogInfo("[PeerFlow] utc="+DateTime.UtcNow.ToString("HH:mm:ss.fff")+" scene="+scene+" window="+seconds.ToString("0.0")+
                "s; local boundary timing only; send counts include non-position datagrams; older/duplicate are not packet-loss rates.");
            foreach(var s in rows)
                logger.LogInfo("[PeerFlow] stage="+s.Stage+" peer="+(s.Peer<0?(s.Stage==FlowStage.ServerSend?"unmapped":"local"):s.Peer.ToString())+" calls="+s.Count+
                    " rate="+(s.Count/seconds).ToString("0.0")+"/s maxGap="+(s.MaximumGap*1000).ToString("0")+
                    "ms >50/150="+s.Over50+"/"+s.Over150+" older/duplicate="+s.Older+"/"+s.Duplicate+
                    (s.Delays>0?" localWait/sendCall="+(s.AverageDelay*1000).ToString("0.0")+"/"+(s.MaximumDelay*1000).ToString("0")+"ms mean/max":""));
        }
        internal static void Clear(){Metrics.Clear();hostDispatch.Clear();ReceiveScratch.ReadAndReset();reportAt=PlayerMotionRepairs.Now;}
        internal static void ForgetPeer(ushort peer)=>Metrics.Forget(peer);
    }
}
