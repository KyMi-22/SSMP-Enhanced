using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using InControl;
using UnityEngine;

namespace Silksong.CoopAssist
{
    public sealed partial class CoopAssistPlugin
    {
        ConfigEntry<KeyCode> teleportKey,benchKey;
        readonly ConcurrentQueue<TeamTravelMessage> travelInbox=new ConcurrentQueue<TeamTravelMessage>();
        int selectedPeer=-1;
        string requestToken="";
        ushort requestPeer;
        float requestDeadline;
        TeamTravelMessage destination;
        bool travelChangingScene;
        string travelOrigin="";
        KeyCode appliedBenchKey=KeyCode.F5;

        void ConfigureTeamTravel()
        {
            ConfigureAssistBindings();
            InstallTravelPickerInput();
            SessionFreedom.Install();
            HostSyncRepairs.Install();
            ConfigureWaypoints();
            SharedMapShop.Install();
        }
        bool ApplyBenchBinding()
        {
            if(api==null)return true;
            var settings=Value(api.ClientManager,"_modSettings");
            var action=Value(Value(settings,"Keybinds"),"CoopLeave") as PlayerAction;
            if(action==null)throw new InvalidOperationException("Native cocoon-return binding missing");
            if(!TryNativeKey(benchKey.Value,out var key))
            {
                Logger.LogWarning("[BenchKey] Unsupported keyboard key: "+benchKey.Value+"; retaining "+appliedBenchKey);
                benchKey.Value=appliedBenchKey;return false;
            }
            // Keep device bindings (including L3), but remove the old keyboard/mouse binding so K stops firing.
            foreach(var binding in action.Bindings.ToArray())
                if(binding is KeyBindingSource || binding is MouseBindingSource)action.RemoveBinding(binding);
            action.AddBinding(new KeyBindingSource(new KeyCombo(key)));
            appliedBenchKey=benchKey.Value;
            settings.GetType().GetMethod("Save")?.Invoke(settings,null);
            Logger.LogInfo("[BenchKey] Cocoon return keyboard="+benchKey.Value+"; existing controller binding retained.");
            return true;
        }
        // Unity and InControl name several keys differently; the rest parse by name.
        static readonly Dictionary<KeyCode,string> NativeKeyNames=new Dictionary<KeyCode,string>{
            {KeyCode.Alpha0,"Key0"},{KeyCode.Alpha1,"Key1"},{KeyCode.Alpha2,"Key2"},{KeyCode.Alpha3,"Key3"},
            {KeyCode.Alpha4,"Key4"},{KeyCode.Alpha5,"Key5"},{KeyCode.Alpha6,"Key6"},{KeyCode.Alpha7,"Key7"},
            {KeyCode.Alpha8,"Key8"},{KeyCode.Alpha9,"Key9"},
            {KeyCode.Keypad0,"Pad0"},{KeyCode.Keypad1,"Pad1"},{KeyCode.Keypad2,"Pad2"},{KeyCode.Keypad3,"Pad3"},
            {KeyCode.Keypad4,"Pad4"},{KeyCode.Keypad5,"Pad5"},{KeyCode.Keypad6,"Pad6"},{KeyCode.Keypad7,"Pad7"},
            {KeyCode.Keypad8,"Pad8"},{KeyCode.Keypad9,"Pad9"},{KeyCode.KeypadDivide,"PadDivide"},
            {KeyCode.KeypadMultiply,"PadMultiply"},{KeyCode.KeypadMinus,"PadMinus"},{KeyCode.KeypadPlus,"PadPlus"},
            {KeyCode.KeypadEnter,"PadEnter"},{KeyCode.KeypadPeriod,"PadPeriod"},{KeyCode.KeypadEquals,"PadEquals"},
            {KeyCode.Exclaim,"ExclamationMark"},{KeyCode.Less,"LessThan"},{KeyCode.Greater,"GreaterThan"},
            {KeyCode.Question,"QuestionMark"},{KeyCode.LeftCurlyBracket,"LeftBrace"},{KeyCode.RightCurlyBracket,"RightBrace"}
        };
        static bool TryNativeKey(KeyCode code,out Key key)
        {
            var name=NativeKeyNames.TryGetValue(code,out var alias)?alias:code.ToString();
            return Enum.TryParse(name,true,out key) && key!=Key.None;
        }
        internal void QueueTravel(TeamTravelMessage data)
        { if(data!=null && data.Valid && travelInbox.Count<64)travelInbox.Enqueue(data); }
        void ClearTeamTravel()
        {
            while(travelInbox.TryDequeue(out _)){}
            CloseTravelPicker();
            pendingMenuPeer=-1;
            selectedPeer=-1; requestToken=""; destination=null;travelChangingScene=false;travelOrigin="";bindingCapture=null;
        }
        bool Playing => Value(global::GameManager.instance,"GameState")?.ToString()=="PLAYING";
        void TickTeamTravel()
        {
            while(travelInbox.TryDequeue(out var data))
            {
                if(data.Kind==TravelKind.Request){AnswerTravel(data);continue;}
                if(requestToken.Length==0 || data.Token!=requestToken || data.Peer!=requestPeer || Time.unscaledTime>requestDeadline)continue;
                if(data.Kind==TravelKind.Unavailable){EndTravel(TravelRefusal(data.Reason));continue;}
                destination=data;
            }
            if(requestToken.Length>0)
            {
                if(Time.unscaledTime>requestDeadline){EndTravel("传送等待超时；可以继续操作，稍后重试。");return;}
                if(api.ClientManager.GetPlayer(requestPeer)==null){EndTravel("目标队友已离线，已取消传送。");return;}
                if(Down){EndTravel("你已倒地，已取消传送。");return;}
                if(destination!=null && !travelChangingScene && (Scene!=travelOrigin || Loading))
                {EndTravel("场景已经变化，已取消这次传送。");return;}
                if(destination!=null && Playing && Ready)FinishTeamTravel();
            }
            TickTravelPicker();
            TickMenuTravel();
        }
        static string TravelRefusal(TravelUnavailableReason reason) =>
            reason==TravelUnavailableReason.UnsafeGround ? "队友未在安全地面" : "队友移动中";
        void RequestTeamTravel()
        {
            if(snapshotToken.Length>0 || applyingSnapshot){SayChat("存档同步中，请完成后再传送。");return;}
            if(waypointTrip!=null){SayChat("正在前往传送点，请稍候。");return;}
            if(requestToken.Length>0){SayChat("正在等待传送，请稍候。");return;}
            if(selectedPeer<0 || api.ClientManager.GetPlayer((ushort)selectedPeer)==null)
            {SayChat("目标队友已离线，请重新选择。");return;}
            if(!Ready || Down){SayChat("请在存活、离开长椅且可以正常操作后传送。");return;}
            if(!Checked((ushort)selectedPeer)){SayChat("队友的存档还在同步，请同步完成后再传送。");return;}
            if(MemberDown((ushort)selectedPeer)){SayChat("目标队友已倒地，请选择存活的队友。");return;}
            if(appearanceSender==null || !AppearanceWire.CanSend(api.NetClient.IsConnected,Value(outboundAddon,"Id")))
            {SayChat("联机通道尚未就绪，请稍后重试传送。");return;}
            arrival.Cancel();travel=null;
            requestPeer=(ushort)selectedPeer;requestToken=Guid.NewGuid().ToString("N");
            requestDeadline=Time.unscaledTime+8;destination=null;travelChangingScene=false;travelOrigin=Scene;
            appearanceSender.SendCollectionData(AssistPacket.TeamTravel,new TeamTravelMessage{Kind=TravelKind.Request,Token=requestToken,Peer=requestPeer});
            SayChat("正在请求传送到 "+api.ClientManager.GetPlayer(requestPeer).Username+" 身边。");
        }
        void AnswerTravel(TeamTravelMessage request)
        {
            if(appearanceSender==null || api.ClientManager.GetPlayer(request.Peer)==null)return;
            var reply=new TeamTravelMessage{
                Kind=TravelKind.Unavailable,Reason=TravelUnavailableReason.Moving,Peer=request.Peer,Token=request.Token
            };
            if(Ready && !Down && Playing && Checked(request.Peer))
            {
                var here=(Vector2)HeroController.instance.transform.position;
                var preferred=(string)Value(Value(global::GameManager.instance,"LastSceneLoad"),"EntryGateName")??"";
                // Without a usable entry gate there is nowhere to land, so it counts as an unsafe spot.
                if(SafeAt(here,out _) && TrySceneGate(Scene,preferred,out var gate) && !string.IsNullOrEmpty(gate))
                {
                    reply.Kind=TravelKind.Destination;reply.Reason=TravelUnavailableReason.None;
                    reply.Scene=Scene;reply.X=here.x;reply.Y=here.y;reply.Gate=gate;
                }
                else reply.Reason=TravelUnavailableReason.UnsafeGround;
            }
            appearanceSender.SendCollectionData(AssistPacket.TeamTravel,reply);
        }
        void FinishTeamTravel()
        {
            var point=destination;
            if(Scene!=point.Scene)
            {
                if(travelChangingScene)return;
                if(!GateExists(point.Scene,point.Gate)){EndTravel("队友未在安全地面");return;}
                HeroController.instance.RecordLeaveSceneCState();
                travelChangingScene=true;requestDeadline=Time.unscaledTime+25;
                global::GameManager.instance.BeginSceneTransition(new global::GameManager.SceneLoadInfo{
                    SceneName=point.Scene,EntryGateName=point.Gate,WaitForSceneTransitionCameraFade=true
                });
                if(!global::GameManager.instance.IsInSceneTransition)EndTravel("游戏暂未接受转场，请稍后再试。");
                return;
            }
            var peer=api.ClientManager.GetPlayer(requestPeer);
            if(peer==null || !peer.IsInLocalScene || MemberDown(requestPeer))
            {EndTravel("队友移动中");return;}
            if(peer.PlayerObject==null || !peer.PlayerObject.activeInHierarchy){EndTravel("队友移动中");return;}
            var origin=(Vector2)peer.PlayerObject.transform.position;
            // Prefer beside the teammate; verify against LOCAL terrain and hazards after scene load.
            foreach(var offset in new[]{1.5f,-1.5f,2.5f,-2.5f,0f})
            {
                var landing=origin+Vector2.right*offset;
                if(!SafeAt(landing,out _))continue;
                PlaceHeroAt(landing);
                EndTravel("已传送到目标队友的安全落脚点旁。");return;
            }
            EndTravel("队友未在安全地面");
        }
        void EndTravel(string message)
        {
            requestToken="";destination=null;travelChangingScene=false;travelOrigin="";SayChat(message);
        }
    }
}
