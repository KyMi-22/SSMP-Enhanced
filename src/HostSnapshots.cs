using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using UnityEngine;

namespace Silksong.CoopAssist
{
    public sealed partial class CoopAssistPlugin
    {
        readonly ConcurrentQueue<(ushort Peer,HostSnapshotMessage Data)> snapshotRequests=new ConcurrentQueue<(ushort,HostSnapshotMessage)>();
        readonly ConcurrentQueue<HostSnapshotMessage> snapshotInbox=new ConcurrentQueue<HostSnapshotMessage>();
        readonly ConcurrentQueue<Action> saveCompletions=new ConcurrentQueue<Action>();
        readonly Dictionary<ushort,double> snapshotSentAt=new Dictionary<ushort,double>();
        readonly CheckpointSchedule checkpoints=new CheckpointSchedule();
        string snapshotToken="";
        int snapshotSlot=-1;
        double snapshotDeadline;
        bool applyingSnapshot;
        HostSnapshotMessage receivedSnapshot;
        NativeSaveBridge saves;
        SaveGameData pendingResyncReload;
        int checkpointSlot=-1;
        int resyncCompletedSlot=-1;

        internal void QueueSnapshotRequest(ushort peer,HostSnapshotMessage data)
        {
            // Server handlers run on its network thread. Unity/save APIs run only in Tick.
            if(data!=null && data.Valid && data.Kind==SnapshotKind.Request && snapshotRequests.Count<16)
                snapshotRequests.Enqueue((peer,data));
        }
        internal void QueueSnapshot(HostSnapshotMessage data)
        {if(data!=null && data.Valid && data.Kind!=SnapshotKind.Request && snapshotInbox.Count<4)snapshotInbox.Enqueue(data);}
        internal void ScheduleCheckpoint()=>checkpoints.Request();
        internal void AfterSaveLoaded(object owner,SaveGameData loaded)
        {
            // Only our explicit replacement reload resets native caches. A normal death,
            // rescue, or unrelated load must retain the native session/control lifecycle.
            if(pendingResyncReload!=null && ReferenceEquals(pendingResyncReload,loaded))
            {
                pendingResyncReload=null;NativeSaveBridge.Loaded(owner);
                resyncCompletedSlot=global::GameManager.instance.profileID;
            }
            ScheduleCheckpoint();
        }
        bool SaveReady=>global::GameManager.instance!=null && !Loading && !Down &&
            PlayerData.instance!=null && PlayerData.instance.health>0 && HeroController.instance!=null &&
            HeroController.instance.gameObject.activeInHierarchy && (Ready || PlayerData.instance.atBench);
        bool ManualSyncReady=>SaveReady && PlayerData.instance.atBench && Playing && !optionsOpen;

        void TrySessionCheckpoint(bool leaving)
        {
            if(!SaveReady || applyingSnapshot || snapshotToken.Length>0 || NativeSaveBridge.IsWriting() || WalletSavePending)return;
            var slot=global::GameManager.instance.profileID;
            if(slot<0)return;
            if(slot!=checkpointSlot){checkpointSlot=slot;checkpoints.Reset();}
            if(leaving)checkpoints.Request();
            if(!checkpoints.TryBegin(PlayerMotionRepairs.Now,true,out var ticket))return;
            var started=PlayerMotionRepairs.Now;
            try
            {
                global::GameManager.instance.SaveGame(ok=>saveCompletions.Enqueue(()=>{
                    checkpoints.Complete(ticket,PlayerMotionRepairs.Now,ok);
                    if(!ok)Logger.LogWarning("[Checkpoint] Save failed; retry in 5 seconds.");
                    else Logger.LogInfo("[Checkpoint] Saved co-op slot "+slot+" ("+((PlayerMotionRepairs.Now-started)*1000).ToString("0")+" ms async).");
                }));
            }
            catch(Exception error){checkpoints.Complete(ticket,PlayerMotionRepairs.Now,false);Logger.LogError("[Checkpoint] "+error);}
        }

        void RequestHostResync()
        {
            if(api?.NetClient.IsConnected!=true){Say("请先加入联机房间。");return;}
            if(Hosting){Say("你是房主。需要恢复同步的队友可坐长椅后点击“按房主一键同步”。");return;}
            if(!SaveReady || !PlayerData.instance.atBench){Say("请先坐在长椅上，再按房主一键同步；同步会保存并重新加载当前存档。");return;}
            if(waypointTrip!=null || snapshotToken.Length>0 || applyingSnapshot || checkpoints.Saving || NativeSaveBridge.IsWriting() || WalletSavePending)
            {Say("同步或保存正在进行，请稍后再试。");return;}
            if(appearanceSender==null || !AppearanceWire.CanSend(true,Value(outboundAddon,"Id")))
            {Say("联机通道尚未就绪，请稍后重试同步。");return;}
            snapshotToken=Guid.NewGuid().ToString("N");snapshotSlot=global::GameManager.instance.profileID;
            snapshotDeadline=PlayerMotionRepairs.Now+30;receivedSnapshot=null;
            appearanceSender.SendSingleData(AssistPacket.HostSnapshot,new HostSnapshotMessage{Kind=SnapshotKind.Request,Token=snapshotToken});
            Say("正在获取房主快照。请关闭设置并留在长椅上。同步前自动备份，保留个人纹章配装、念珠和念珠串。");
        }

        void TickHostSnapshots()
        {
            if(resyncCompletedSlot>=0 && SaveReady && Playing)
            {
                if(global::GameManager.instance.profileID==resyncCompletedSlot)
                    Say("已同步进度、祈愿、物品与地图，保留个人纹章配装、念珠和念珠串。");
                resyncCompletedSlot=-1;
            }
            saves=saves??new NativeSaveBridge(coop);
            DrainSaveCompletions();
            while(snapshotRequests.TryDequeue(out var request))
            {
                if(server?.NetServer.IsStarted!=true || !Hosting || server.ServerManager.GetPlayer(request.Peer)==null)continue;
                var now=PlayerMotionRepairs.Now;
                var response=new HostSnapshotMessage{Kind=SnapshotKind.Unavailable,Token=request.Data.Token};
                if(SaveReady && Checked(request.Peer) && !NativeSaveBridge.IsWriting() &&
                    (!snapshotSentAt.TryGetValue(request.Peer,out var last) || now-last>=5))
                {
                    snapshotSentAt[request.Peer]=now;
                    try{response=HostSnapshotMessage.FromJson(request.Data.Token,NativeSaveBridge.Capture());}
                    catch(Exception error){Logger.LogError("[HostSync] Could not capture host snapshot: "+error);}
                }
                if(response.Kind==SnapshotKind.Snapshot)sender.SendChunkData(AssistPacket.HostSnapshot,response,request.Peer);
                else sender.SendSingleData(AssistPacket.HostSnapshot,response,request.Peer);
            }
            while(snapshotInbox.TryDequeue(out var response))
            {
                if(snapshotToken.Length==0 || response.Token!=snapshotToken || applyingSnapshot)continue;
                if(response.Kind==SnapshotKind.Unavailable){CancelHostResync("房主正在加载、倒地、保存或尚未校验，请稍后重试。");continue;}
                receivedSnapshot=response;
                Say("已收到房主快照。请关闭面板／设置并留在长椅，接下来会备份、同步并重新加载。");
            }
            if(snapshotToken.Length==0 || applyingSnapshot)return;
            if(global::GameManager.instance.profileID!=snapshotSlot || Down || PlayerMotionRepairs.Now>snapshotDeadline)
            {CancelHostResync("同步等待超时、存档已切换或角色已倒地，已取消。");return;}
            if(receivedSnapshot!=null && ManualSyncReady && !checkpoints.Saving && !NativeSaveBridge.IsWriting())ApplyHostSnapshot();
        }

        void DrainSaveCompletions()
        {
            while(saveCompletions.TryDequeue(out var completion))
                try{completion();}catch(Exception error){Logger.LogError("[HostSync] Save callback failed: "+error);CancelHostResync("同步保存处理失败，已停止操作；请查看日志。");}
        }

        void ApplyHostSnapshot()
        {
            applyingSnapshot=true;
            Say("正在保存当前存档并备份，随后写入房主进度与物品。请保持坐在长椅上。");
            var token=snapshotToken;var slot=snapshotSlot;var host=receivedSnapshot;
            // Flush the local state first, so the native backup includes the latest personal
            // currency. Never write the replacement after a failed local save.
            global::GameManager.instance.SaveGame(ok=>saveCompletions.Enqueue(()=>{
                if(token!=snapshotToken)return;
                if(!ok || !ManualSyncReady || global::GameManager.instance.profileID!=slot)
                {CancelHostResync("当前存档保存失败或已离开长椅，同步已取消。");return;}
                try
                {
                    var aligned=saves.Align(host.Json());saves.BackUp(slot);
                    Platform.Current.WriteSaveSlot(slot,global::GameManager.instance.GetBytesForSaveData(aligned),written=>saveCompletions.Enqueue(()=>{
                        if(token!=snapshotToken)return;
                        if(!written){CancelHostResync("同步存档写入失败；原备份已保留。");return;}
                        if(global::GameManager.instance.profileID!=slot || !ManualSyncReady)
                        {CancelHostResync("同步副本已保存，但角色状态已变化，已停止自动重载；备份保留。");return;}
                        CancelHostResync("同步存档已保存，正在通过游戏正常流程重新加载。");
                        arrival.Cancel();travel=null;ClearTeamTravel();
                        ClearSharedMaps();waypointTrip=null;pendingResyncReload=aligned;
                        try{global::GameManager.instance.LoadGameFromUI(slot,aligned);}
                        catch{pendingResyncReload=null;throw;}
                    }));
                }
                catch(Exception error){Logger.LogError("[HostSync] Manual sync failed: "+error);CancelHostResync("同步失败；原存档备份已保留，详情见日志。");}
            }));
        }
        void CancelHostResync(string message)
        {snapshotToken="";receivedSnapshot=null;applyingSnapshot=false;Say(message);}
        void ClearHostSnapshots()
        {
            snapshotToken="";receivedSnapshot=null;applyingSnapshot=false;
            pendingResyncReload=null;
            resyncCompletedSlot=-1;
            while(snapshotRequests.TryDequeue(out _)){}
            while(snapshotInbox.TryDequeue(out _)){}
            snapshotSentAt.Clear();
        }
    }
}
