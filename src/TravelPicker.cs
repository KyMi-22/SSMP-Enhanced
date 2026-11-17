using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Silksong.ModMenu.Elements;
using Silksong.ModMenu.Screens;
using UnityEngine;

namespace Silksong.CoopAssist
{
    public sealed partial class CoopAssistPlugin
    {
        readonly TravelSelection travelSelection=new TravelSelection();
        readonly object travelInputOwner=new object();
        HeroController pickerHero;
        bool travelPickerOpen;
        float pickerRefreshAt;
        string pickerScene="",pickerStatus="";
        int pickerSlot;
        Vector2 pickerScroll;
        int pendingMenuPeer=-1;
        float pendingMenuDeadline;
        string pendingMenuScene="";
        int pendingMenuSlot;
        ScrollingMenuScreen nativeTravelPicker;

        void InstallTravelPickerInput()
        {
            new Harmony("silksong.coopassist.travelpicker").Patch(
                AccessTools.Method(typeof(global::GameManager),"PauseGameToggle",new[]{typeof(bool)}),
                prefix:new HarmonyMethod(typeof(CoopAssistPlugin),nameof(BeforePickerPause)));
            new Harmony("silksong.coopassist.travelpicker").Patch(
                AccessTools.Method(typeof(InputHandler),"SetCursorVisible",new[]{typeof(bool)}),
                prefix:new HarmonyMethod(typeof(CoopAssistPlugin),nameof(BeforePickerCursor)));
        }
        static bool BeforePickerPause(ref IEnumerator __result)
        {
            if(Instance==null || (!Instance.travelPickerOpen && Instance.bindingCapture==null))return true;
            // Esc belongs to the picker until it closes. Do not toggle the game's pause state.
            __result=SkipPickerPause();
            return false;
        }
        static IEnumerator SkipPickerPause(){yield break;}
        static void BeforePickerCursor(ref bool __0)
        {
            if (Instance == null) return;
            if (Instance.travelPickerOpen || Instance.optionsOpen || Instance.ledgerOpen ||
                Instance.purchase != null || Instance.rosaryAsks.Count > 0)
                __0 = true;
        }


        void OpenTravelPicker()
        {
            if(api?.NetClient.IsConnected!=true){SayChat("请先进入联机房间。");return;}
            if(!Ready || !Playing || Down){SayChat("请在存活、离开长椅且可以正常操作后传送。");return;}
            if(requestToken.Length>0 || pendingMenuPeer>=0){SayChat("正在等待传送，请稍候。");return;}
            if(snapshotToken.Length>0 || applyingSnapshot || waypointTrip!=null)
            {SayChat("正在同步或前往传送点，请完成后再操作。");return;}
            optionsOpen=false;
            bindingCapture=null;
            arrival.Cancel();travel=null;
            pickerScene=Scene;pickerSlot=global::GameManager.instance.profileID;
            pickerStatus=api.ClientManager.Players.Count==0?"房间内暂无队友，可取消后等待队友加入。":"请选择目标，再按 Enter 或点击确认传送。";
            pickerScroll=Vector2.zero;
            travelSelection.Refresh(api.ClientManager.Players.Select(p=>(int)p.Id));
            travelSelection.Select(TravelSelection.Nearest);
            pickerHero=HeroController.instance;
            pickerHero.AddInputBlocker(travelInputOwner);
            travelPickerOpen=true;pickerRefreshAt=Time.unscaledTime+.25f;
            SayChat("已打开队友选择。"+pickerStatus);
        }
        void CloseTravelPicker()
        {
            travelPickerOpen=false;
            // Remove only our blocker from the same hero that received it.
            if(pickerHero!=null)pickerHero.RemoveInputBlocker(travelInputOwner);
            pickerHero=null;
        }
        void CancelTravelPicker(string message)
        {CloseTravelPicker();SayChat(message);}
        void SelectTravelTarget(int id)
        {
            travelSelection.Select(id);
            pickerStatus=id==TravelSelection.Cancel?"确认后取消选择。":"已选中："+TravelRowLabel(id)+"。按 Enter 或点击确认传送。";
        }
        void MoveTravelTarget(int direction)
        {
            travelSelection.Move(direction);
            SelectTravelTarget(travelSelection.Selected);
            ScrollPickerToSelection();
        }
        void TickTravelPicker()
        {
            if(!travelPickerOpen)return;
            if(!Ready || Down || Loading || !Playing || Scene!=pickerScene || global::GameManager.instance.profileID!=pickerSlot ||
                HeroController.instance!=pickerHero || AssistChatOpen())
            {CancelTravelPicker("游戏状态已变化，已取消队友选择。");return;}
            if(Time.unscaledTime>=pickerRefreshAt)
            {
                int previous=travelSelection.Selected;
                travelSelection.Refresh(api.ClientManager.Players.Select(p=>(int)p.Id));
                if(previous!=travelSelection.Selected)
                {pickerStatus="所选队友已离线，请重新选择并确认。";SayChat(pickerStatus);}
                pickerRefreshAt=Time.unscaledTime+.25f;
            }
            if(Input.GetKeyDown(KeyCode.Escape)){CancelTravelPicker("已取消队友选择。");return;}
            if(Input.GetKeyDown(KeyCode.UpArrow))MoveTravelTarget(-1);
            if(Input.GetKeyDown(KeyCode.DownArrow))MoveTravelTarget(1);
            if(Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))ConfirmPicker(travelSelection.Selected,false);
        }
        void ScrollPickerToSelection()
        {
            int index=travelSelection.Rows.ToList().IndexOf(travelSelection.Selected);
            float top=index*44,view=PickerViewportHeight;
            if(top<pickerScroll.y)pickerScroll.y=top;
            else if(top+44>pickerScroll.y+view)pickerScroll.y=top+44-view;
        }
        string PeerUnavailable(int id)
        {
            if(api?.ClientManager.GetPlayer((ushort)id)==null)return "已离线";
            if(MemberDown((ushort)id))return "已倒地";
            if(!Checked((ushort)id))return "同步中";
            return "";
        }
        string TravelRowLabel(int id)
        {
            if(id==TravelSelection.Nearest)return "传送至距离最近的队友";
            if(id==TravelSelection.Cancel)return "取消";
            var peer=api?.ClientManager.GetPlayer((ushort)id);
            var reason=PeerUnavailable(id);
            return (peer?.Username??"队友")+" ["+id+"] / "+(reason.Length>0?reason:peer.IsInLocalScene?"同图":"异图");
        }
        int NearestTravelPeer()
        {
            var manager=api.ClientManager.MapManager;
            var coordinates=TravelMapCoordinates.Read(manager);
            var hero=(Vector2)HeroController.instance.transform.position;
            var candidates=new List<(int Id,double DistanceSquared)>();
            foreach(var peer in api.ClientManager.Players)
            {
                if(PeerUnavailable(peer.Id).Length>0)continue;
                bool visible=peer.IsInLocalScene && peer.PlayerObject!=null && peer.PlayerObject.activeInHierarchy;
                if(coordinates!=null && visible && coordinates.TryProject(peer.PlayerObject.transform.position,out var position))
                {
                    // Use the same map transform as the local origin, even before a peer icon arrives.
                    candidates.Add((peer.Id,(position-coordinates.Origin).sqrMagnitude));
                }
                else if(coordinates!=null && manager.TryGetEntry(peer.Id,out var entry) && entry.HasMapIcon)
                {
                    var delta=new Vector2(entry.Position.X,entry.Position.Y)-coordinates.Origin;
                    candidates.Add((peer.Id,delta.sqrMagnitude));
                }
                else if(coordinates==null && visible)
                    candidates.Add((peer.Id,((Vector2)peer.PlayerObject.transform.position-hero).sqrMagnitude));
            }
            return TravelSelection.FindNearest(candidates);
        }
        void ConfirmPicker(int choice,bool fromMenu)
        {
            if(choice==TravelSelection.Cancel){CancelTravelPicker("已取消队友选择。");return;}
            if(!fromMenu && travelSelection.ConfirmationBlocked)
            {pickerStatus="所选队友已离线，请重新选择目标后确认。";SayChat(pickerStatus);return;}
            if(api?.NetClient.IsConnected!=true || HeroController.instance==null || Down || Loading ||
                (fromMenu && Value(global::GameManager.instance,"GameState")?.ToString()!="PAUSED"))
            {CloseTravelPicker();SayChat("游戏状态已变化，请回到游戏后重新选择传送。");return;}
            int peer=choice==TravelSelection.Nearest?NearestTravelPeer():choice;
            string error=peer<0?"暂无可定位的存活队友，请选择姓名。":PeerUnavailable(peer);
            if(error.Length>0)
            {
                pickerStatus=error;
                if(fromMenu && nativeTravelPicker!=null)nativeTravelPicker.TitleText.text=error;
                SayChat(error);return;
            }
            selectedPeer=peer;
            CloseTravelPicker();
            if(!fromMenu){RequestTeamTravel();return;}
            // Let the game's pause/menu lifecycle finish before requesting a safe destination.
            pendingMenuPeer=peer;pendingMenuDeadline=Time.unscaledTime+8;pendingMenuScene=Scene;
            pendingMenuSlot=global::GameManager.instance.profileID;
            UIManager.instance.UIClosePauseMenu();
            UIManager.instance.TogglePauseGame();
        }
        void TickMenuTravel()
        {
            if(pendingMenuPeer<0)return;
            if(Time.unscaledTime>pendingMenuDeadline || Down || Loading || Scene!=pendingMenuScene ||
                global::GameManager.instance.profileID!=pendingMenuSlot || PeerUnavailable(pendingMenuPeer).Length>0)
            {pendingMenuPeer=-1;SayChat("目标或游戏状态已变化，已取消传送，请重新选择。");return;}
            if(!Playing || !Ready)return;
            selectedPeer=pendingMenuPeer;pendingMenuPeer=-1;RequestTeamTravel();
        }
        void ShowMenuTravelPicker()
        {
            if(nativeTravelPicker!=null)return;
            if(api?.NetClient.IsConnected!=true || HeroController.instance==null || Down || Loading ||
                Value(global::GameManager.instance,"GameState")?.ToString()!="PAUSED")
            {SayChat("请进入联机游戏后，打开暂停菜单选择传送。");return;}
            var menu=new ScrollingMenuScreen("选择传送队友（↑↓选择，确认传送）");
            menu.OnGoBack+=()=>SayChat("已取消队友选择。");
            nativeTravelPicker=menu;
            menu.OnHide+=_=>{
                if(nativeTravelPicker==menu)nativeTravelPicker=null;
                Destroy(menu.Container);
            };
            menu.ScrollPane.Axes=ScrollingPane.ScrollAxes.Vertical;
            var selection=new TravelSelection();
            selection.Refresh(api.ClientManager.Players.Select(p=>(int)p.Id));
            foreach(int id in selection.Rows)
            {
                if(id==TravelSelection.Cancel)continue; // Native Back button provides cancellation.
                int target=id;
                menu.Add(new TextButton(TravelRowLabel(target),"“最近”按地图直线距离；地图定位不可用时只比较同图队友。")
                    {OnSubmit=()=>ConfirmPicker(target,true)});
            }
            AddMenuFeedback(menu);
            MenuScreenNavigation.Show(menu);
            SayChat("已打开队友选择。上下选择队友，确认后传送，返回键取消。");
        }
        void DrawTravelPicker()
        {
            if(!travelPickerOpen)return;
            float width=Mathf.Min(540,Screen.width-24),height=PickerHeight;
            GUI.ModalWindow(20327512,new Rect((Screen.width-width)/2,Mathf.Max(12,(Screen.height-height-160)/2),width,height),_=>{
                GUILayout.Label("↑／↓ 选择　Enter 确认　Esc 取消");
                GUILayout.Label("“最近”按地图直线距离；标着“异图”的队友在别的场景，仍可请求传送。");
                pickerScroll=GUILayout.BeginScrollView(pickerScroll,GUILayout.Height(PickerViewportHeight));
                foreach(int id in travelSelection.Rows)
                {
                    bool selected=id==travelSelection.Selected;
                    var previous=GUI.backgroundColor;
                    if(selected)GUI.backgroundColor=new Color(.95f,.7f,.3f);
                    if(GUILayout.Button((selected?"▶ ":"    ")+TravelRowLabel(id),GUILayout.Height(40)))SelectTravelTarget(id);
                    GUI.backgroundColor=previous;
                }
                GUILayout.EndScrollView();
                GUILayout.Label(pickerStatus);
                GUILayout.BeginHorizontal();
                if(GUILayout.Button("↑ 上一项"))MoveTravelTarget(-1);
                if(GUILayout.Button("↓ 下一项"))MoveTravelTarget(1);
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                if(GUILayout.Button("确认传送",GUILayout.Height(36)))ConfirmPicker(travelSelection.Selected,false);
                if(GUILayout.Button("取消",GUILayout.Height(36)))CancelTravelPicker("已取消队友选择。");
                GUILayout.EndHorizontal();
            },"选择传送队友");
        }
        static float PickerHeight=>Mathf.Min(600,Screen.height-184);
        static float PickerViewportHeight=>Mathf.Min(340,Mathf.Max(72,PickerHeight-220));
    }
}
