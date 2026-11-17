using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Configuration;
using SSMP.Api.Client;
using SSMP.Api.Client.Networking;
using SSMP.Game.Settings;
using TMProOld;
using UnityEngine;

namespace Silksong.CoopAssist
{
    public sealed partial class CoopAssistPlugin
    {
        IClientAddonNetworkSender<AssistPacket> appearanceSender;
        SSMP.Api.Client.ClientAddon outboundAddon;
        readonly ConcurrentQueue<Appearance> appearances = new ConcurrentQueue<Appearance>();
        readonly Dictionary<ushort, Color> colours = new Dictionary<ushort, Color>();
        readonly ConcurrentQueue<NameStyleMessage> nameStyleInbox=new ConcurrentQueue<NameStyleMessage>();
        readonly Dictionary<ushort,bool> nameStyles=new Dictionary<ushort,bool>();
        readonly List<TextMeshPro> ownNameLabels=new List<TextMeshPro>();
        GameObject ownNameOwner;
        float ownNamesAt,enhancementsAt,nameStyleAt;
        bool? lastBold;
        bool enhancementFault;
        sealed class PeerVisual
        {
            internal IClientPlayer Player;
            internal GameObject Container, Map;
            internal readonly List<TextMeshPro> Names = new List<TextMeshPro>();
            internal readonly List<tk2dSprite> Icons = new List<tk2dSprite>();
            internal readonly GUIContent Label = new GUIContent();
            internal string Username, AwayLabel, LastArrow;
            internal int Distance = -1;
            internal float RefreshAt, LabelAt;
            internal bool Away;
            internal Color? AppliedNameColour;
            internal bool? AppliedBold;
            internal TextMeshPro LastName;
        }
        readonly Dictionary<ushort, PeerVisual> peerVisuals = new Dictionary<ushort, PeerVisual>();
        readonly List<PeerVisual> peerRows = new List<PeerVisual>();
        readonly HashSet<ushort> activeIds = new HashSet<ushort>();
        readonly List<ushort> staleIds = new List<ushort>();
        static readonly Comparison<PeerVisual> PeerOrder = (a,b) => a.Player.Id.CompareTo(b.Player.Id);
        ConfigEntry<bool> directionHeads, teamMap, lowLatency;
        bool mapConfigured, optionsOpen;
        float appearanceAt;
        float motionReportAt;
        string lastColour = "";
        Color previousOwnColour;
        readonly object[] ownColourArguments = new object[1];
        Texture2D head;
        Font uiFont;
        Rect optionsRect = new Rect(30,80,410,650);
        Vector2 skinScroll;
        Vector2 optionsScroll;
        IList skinChoices;
        string skinStatus = "";
        static readonly Assembly Ssmp = typeof(SSMP.Api.Client.ClientAddon).Assembly;
        static readonly Type OwnSkinType = Ssmp.GetType("SSMP.Game.Client.Skin.OwnSkin");
        static readonly MethodInfo SkinChoices = OwnSkinType.GetMethod("Choices", BindingFlags.Static|BindingFlags.Public);
        static readonly MethodInfo ChooseSkin = OwnSkinType.GetMethod("Choose", BindingFlags.Static|BindingFlags.Public);
        static readonly MethodInfo OwnColour = Ssmp.GetType("SSMP.Game.Client.PlayerManager").GetMethod("OwnColour", BindingFlags.Static|BindingFlags.NonPublic);
        void ConfigureEnhancements()
        {
            directionHeads=Config.Bind("Team","DirectionPortraits",true,"Named portrait and direction for teammates.");
            teamMap=Config.Bind("Team","MapPositions",true,"Host enables global teammate map icons without a compass requirement.");
            lowLatency=Config.Bind("Play","LowLatencySmoothing",true,"Use the player-only movement filter. Off restores native player interpolation. Position sampling fix stays enabled.");
            Comfort.Install(Config);
        }
        internal void SetAppearanceSender(IClientAddonNetworkSender<AssistPacket> value, SSMP.Api.Client.ClientAddon addon) { appearanceSender=value;outboundAddon=addon; }
        internal void QueueAppearance(Appearance value) { if(value!=null && Appearance.Valid(value.Colour) && appearances.Count<64) appearances.Enqueue(value); }
        internal void QueueNameStyle(NameStyleMessage value)
        {if(value!=null && value.Valid && nameStyleInbox.Count<64)nameStyleInbox.Enqueue(value);}
        void ClearEnhancements()
        {
            while(appearances.TryDequeue(out _)){}while(nameStyleInbox.TryDequeue(out _)){}
            colours.Clear();nameStyles.Clear();peerVisuals.Clear();peerRows.Clear();activeIds.Clear();staleIds.Clear();
            ownNameLabels.Clear();ownNameOwner=null;ownNamesAt=0;
            lastColour="";lastBold=null;appearanceAt=0;nameStyleAt=0;enhancementsAt=0;mapConfigured=false;
        }
        internal void InvalidateNameLabels()
        {enhancementsAt=0;ownNamesAt=0;foreach(var visual in peerRows)visual.RefreshAt=0;}
        void TickEnhancements()
        {
            if(api==null || !api.NetClient.IsConnected || Time.unscaledTime<enhancementsAt)return;
            enhancementsAt=Time.unscaledTime+.2f;
            try{UpdateEnhancements();enhancementFault=false;}
            catch(Exception error)
            {
                if(!enhancementFault)Logger.LogError("[NamePresentation] Retrying visual reconciliation: "+error);
                enhancementFault=true;
            }
        }
        void UpdateEnhancements()
        {
            RuntimeRepairs.LowLatency=lowLatency.Value;
            if(Time.unscaledTime>=motionReportAt)
            {motionReportAt=Time.unscaledTime+15;Logger.LogInfo(PlayerMotionRepairs.Report());Logger.LogInfo(PlaybackDiagnostics.Report());NetworkDiagnostics.Report(Logger,UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);}
            if(teamMap.Value && server!=null && server.NetServer.IsStarted && Hosting && !mapConfigured)
            {
                var settings=new ServerSettings();
                foreach(var p in typeof(ServerSettings).GetProperties().Where(p=>p.CanRead&&p.CanWrite&&p.GetIndexParameters().Length==0))
                    p.SetValue(settings,p.GetValue(server.ServerManager.ServerSettings));
                ReflectionCache.Set(settings,"AlwaysShowMapIcons",true);
                ReflectionCache.Set(settings,"OnlyBroadcastMapIconWithCompass",false);
                ReflectionCache.Set(settings,"AllowSkins",true);
                server.ServerManager.ApplyServerSettings(settings);mapConfigured=true;
            }
            if(server!=null && !server.NetServer.IsStarted)mapConfigured=false;
            while(appearances.TryDequeue(out var a)) if(ColorUtility.TryParseHtmlString("#"+a.Colour,out var c))
            {
                if(!colours.TryGetValue(a.Id,out var previous)||previous!=c)Logger.LogInfo("[NameColour] received player="+a.Id+" colour=#"+a.Colour);
                colours[a.Id]=c;
            }
            while(nameStyleInbox.TryDequeue(out var style))
            {
                if(!nameStyles.TryGetValue(style.Id,out var previous)||previous!=style.Bold)
                    Logger.LogInfo("[NameStyle] received player="+style.Id+" bold="+style.Bold);
                nameStyles[style.Id]=style.Bold;
            }
            var localSettings=api.ClientManager.ModSettings;ownColourArguments[0]=localSettings;
            // ColourOwnName controls only the local player's own display, not the selected identity sent to peers.
            var localColour=(Color)OwnColour.Invoke(null,ownColourArguments);
            var colourChanged=lastColour.Length==0||previousOwnColour!=localColour;
            var html=colourChanged?ColorUtility.ToHtmlStringRGBA(localColour):lastColour;
            if(appearanceSender!=null && AppearanceWire.CanSend(api.NetClient.IsConnected,Value(outboundAddon,"Id")) &&
                (html!=lastColour||Time.unscaledTime>=appearanceAt))
            {
                try{appearanceSender.SendSingleData(AssistPacket.Appearance,new Appearance{Colour=html});}
                catch(InvalidOperationException) when(!AppearanceWire.CanSend(api.NetClient.IsConnected,Value(outboundAddon,"Id"))){return;}
                if(html!=lastColour)Logger.LogInfo("[NameColour] published colour=#"+html);
                lastColour=html;previousOwnColour=localColour;appearanceAt=Time.unscaledTime+3;
            }
            var bold=Flag(localSettings,"BoldOwnName");
            if(appearanceSender!=null && AppearanceWire.CanSend(api.NetClient.IsConnected,Value(outboundAddon,"Id")) &&
                (lastBold!=bold || Time.unscaledTime>=nameStyleAt))
            {
                appearanceSender.SendSingleData(AssistPacket.NameStyle,new NameStyleMessage{Flags=(byte)(bold?1:0)});
                if(lastBold!=bold)Logger.LogInfo("[NameStyle] published bold="+bold);
                lastBold=bold;nameStyleAt=Time.unscaledTime+3;
            }
            RefreshOwnName(localSettings,localColour,bold);
            activeIds.Clear();
            NetworkDiagnostics.Metrics.BeginVisibility();
            var rowsChanged=false;
            foreach(var p in api.ClientManager.Players)
            {
                if(p.IsInLocalScene)NetworkDiagnostics.Metrics.MarkVisible(p.Id);
                activeIds.Add(p.Id);bool hasTint=colours.TryGetValue(p.Id,out var tint);
                if(!peerVisuals.TryGetValue(p.Id,out var visual))
                {
                    visual=new PeerVisual();peerVisuals.Add(p.Id,visual);peerRows.Add(visual);rowsChanged=true;
                }
                visual.Player=p;
                if(visual.Username!=p.Username)
                {visual.Username=p.Username;visual.AwayLabel=p.Username+" / 异图（打开地图查看）";visual.LabelAt=0;visual.Distance=-1;visual.LastArrow=null;}
                GameObject map=null;
                if(api.ClientManager.MapManager.TryGetEntry(p.Id,out var entry))map=Value(entry,"GameObject") as GameObject;
                var refresh=visual.Container!=p.PlayerContainer||visual.Map!=map||Time.unscaledTime>=visual.RefreshAt;
                if(refresh)
                {
                    // The List overload reuses its storage; a full component scan happens only
                    // on replacement or once a second, rather than allocating arrays every poll.
                    visual.Container=p.PlayerContainer;visual.Map=map;visual.RefreshAt=Time.unscaledTime+1;
                    visual.Names.Clear();visual.Icons.Clear();
                    if(visual.Container!=null)visual.Container.GetComponentsInChildren(true,visual.Names);
                    if(map!=null)map.GetComponentsInChildren(true,visual.Icons);
                }
                TextMeshPro displayedName=null;
                foreach(var text in visual.Names)
                    if(text!=null&&(text.gameObject.name=="Username"||string.Equals(text.text,p.Username,StringComparison.OrdinalIgnoreCase)))
                    {
                        displayedName=text;NameFontRepairs.StyleName(text);
                        if(nameStyles.TryGetValue(p.Id,out var peerBold))NameFontRepairs.ApplyWeight(text,peerBold);
                        if(hasTint)NameLabelStyle.ApplyColour(text,tint);
                    }
                if(visual.LastName!=displayedName)
                {visual.LastName=displayedName;visual.AppliedNameColour=null;visual.AppliedBold=null;}
                if(displayedName!=null && hasTint && (!visual.AppliedNameColour.HasValue||visual.AppliedNameColour.Value!=tint))
                {
                    visual.AppliedNameColour=tint;
                    Logger.LogInfo("[NameColour] applied legacy name player="+p.Id+" colour=#"+ColorUtility.ToHtmlStringRGBA(tint));
                }
                if(displayedName!=null && nameStyles.TryGetValue(p.Id,out var appliedBold) && visual.AppliedBold!=appliedBold)
                {
                    visual.AppliedBold=appliedBold;
                    Logger.LogInfo("[NameStyle] applied player="+p.Id+" face="+displayedName.font?.name+" size="+displayedName.fontSize+" bold="+appliedBold);
                }
                if(hasTint)foreach(var sprite in visual.Icons)if(sprite!=null&&sprite.color!=tint)sprite.color=tint;
            }
            staleIds.Clear();foreach(var pair in peerVisuals)if(!activeIds.Contains(pair.Key))staleIds.Add(pair.Key);
            foreach(var id in staleIds){peerRows.Remove(peerVisuals[id]);peerVisuals.Remove(id);colours.Remove(id);nameStyles.Remove(id);rowsChanged=true;}
            if(rowsChanged)peerRows.Sort(PeerOrder);
        }
        internal void ApplyPeerNameColour(TextMeshPro text)
        {
            if(text==null||text.gameObject.name!="Username"||api==null)return;
            foreach(var player in api.ClientManager.Players)
                if(player.PlayerContainer!=null&&text.transform.IsChildOf(player.PlayerContainer.transform))
                {
                    NameFontRepairs.StyleName(text);
                    if(nameStyles.TryGetValue(player.Id,out var bold))NameFontRepairs.ApplyWeight(text,bold);
                    if(colours.TryGetValue(player.Id,out var tint))NameLabelStyle.ApplyColour(text,tint);
                    return;
                }
        }
        void RefreshOwnName(object settings,Color tint,bool bold)
        {
            var hero=HeroController.instance;
            if(hero==null)return;
            if(ownNameOwner!=hero.gameObject || Time.unscaledTime>=ownNamesAt)
            {
                ownNameOwner=hero.gameObject;ownNamesAt=Time.unscaledTime+1;
                ownNameLabels.Clear();ownNameOwner.GetComponentsInChildren(true,ownNameLabels);
            }
            foreach(var text in ownNameLabels)
            {
                if(text==null || text.gameObject.name!="Username")continue;
                NameFontRepairs.StyleName(text);NameFontRepairs.ApplyWeight(text,bold);
                if(Flag(settings,"ColourOwnName"))NameLabelStyle.ApplyColour(text,tint);
            }
        }
        void LateUpdate()
        {
            if(!compatible)return;
            TickGrandGateProgress();
            TickWaypoint();
            TickRosaries();
            TickRosaryDialog();
            TickAssistHotkeys();
            // Every mod window needs the game pointer; the game hides it during play.
            PointerControl.Tick(ledgerOpen || optionsOpen || travelPickerOpen || bindingCapture != null ||
                purchase != null || rosaryAsks.Count > 0);
            TickCocoonView();
            TickEnhancements();
            TickSharedMaps();
            TickEquipment();
            SkinHotReload.Tick(this);
            if(Input.GetKeyDown(KeyCode.F7) && !travelPickerOpen && bindingCapture==null &&
                Silksong.ModMenu.Screens.MenuScreenNavigation.CurrentModMenuScreen==null)
            {
                if (ledgerOpen) { ledgerOpen = false; return; }
                optionsOpen=!optionsOpen;
                if(optionsOpen){bindingsExpanded=false;skinChoices=(IList)SkinChoices.Invoke(null,null);}
            }
        }
        void OnGUI()
        {
            if(!compatible)return;
            var old=GUI.skin.font;
            try
            {
                if(uiFont==null)uiFont=Font.CreateDynamicFontFromOSFont(new[]{"Microsoft YaHei","SimHei","Arial"},16);
                  GUI.skin.font=uiFont;DrawOverlay();DrawRosaryOverlay();DrawFeedbackToast();
            }
            finally{GUI.skin.font=old;}
        }
        void DrawOverlay()
        {
            if(!compatible)return;
            if(travelPickerOpen){DrawTravelPicker();return;}
            if(optionsOpen)
            {
                optionsRect.height=Mathf.Min(760,Mathf.Max(240,Screen.height-50));
                optionsRect.y=Mathf.Clamp(optionsRect.y,0,Mathf.Max(0,Screen.height-optionsRect.height));
                optionsRect=GUI.Window(20327508,optionsRect,DrawOptions,"联机体验 / F7");
            }
            if(Event.current.type!=EventType.Repaint)return;
            if(api==null||!api.NetClient.IsConnected||!directionHeads.Value||Loading||HeroController.instance==null)return;
            if(head==null)head=MakeHead();var cam=Camera.main;if(cam==null)return;int row=0;
            foreach(var visual in peerRows)
            {
                var p=visual.Player;var tint=colours.TryGetValue(p.Id,out var c)?c:Color.white;Vector2 at;
                if(p.IsInLocalScene&&p.PlayerObject!=null)
                {
                    var view=cam.WorldToScreenPoint(p.PlayerObject.transform.position);if(view.z<0)continue;
                    if(view.x>=60&&view.x<=Screen.width-60&&view.y>=60&&view.y<=Screen.height-70)continue;
                    var delta=(Vector2)(p.PlayerObject.transform.position-HeroController.instance.transform.position);
                    if(visual.Away||Time.unscaledTime>=visual.LabelAt)
                    {
                        var arrow=Arrow(delta);var distance=Mathf.RoundToInt(delta.magnitude);
                        if(visual.Away||arrow!=visual.LastArrow||distance!=visual.Distance)
                            visual.Label.text=p.Username+" "+arrow+" "+distance;
                        visual.Away=false;visual.LastArrow=arrow;visual.Distance=distance;visual.LabelAt=Time.unscaledTime+.1f;
                    }
                    at=new Vector2(Mathf.Clamp(view.x,45,Screen.width-185),Mathf.Clamp(Screen.height-view.y-70,40,Screen.height-65));
                }
                else{if(!visual.Away||visual.Label.text!=visual.AwayLabel)visual.Label.text=visual.AwayLabel;visual.Away=true;at=new Vector2(Screen.width-285,90+row++*44);}
                var old=GUI.color;GUI.color=tint;GUI.Box(new Rect(at.x-3,at.y-3,34,34),GUIContent.none);
                GUI.color=Color.white;GUI.DrawTexture(new Rect(at.x,at.y,28,28),head);
                GUI.color=tint;GUI.Label(new Rect(at.x+35,at.y,180,34),visual.Label);GUI.color=old;
            }
        }
        static string Arrow(Vector2 d)
        {
            if(Mathf.Abs(d.x)>Mathf.Abs(d.y)*1.6f)return d.x>=0?">":"<";
            if(Mathf.Abs(d.y)>Mathf.Abs(d.x)*1.6f)return d.y>=0?"^":"v";
            return d.y>=0?(d.x>=0?"^>":"<^"):(d.x>=0?"v>":"<v");
        }
        static Texture2D MakeHead()
        {
            var tex=new Texture2D(32,32,TextureFormat.RGBA32,false);var pixels=new Color[1024];
            for(int y=0;y<32;y++)for(int x=0;x<32;x++)
            {
                bool mask=(x-16)*(x-16)/100f+(y-11)*(y-11)/64f<=1;
                bool horns=y>=15&&y<30&&(Mathf.Abs(x-(8-(y-15)/5))<2||Mathf.Abs(x-(24+(y-15)/5))<2);
                bool eyes=y>=10&&y<14&&(x>=10&&x<=12||x>=20&&x<=22);
                pixels[y*32+x]=mask||horns?(eyes?Color.black:Color.white):Color.clear;
            }
            tex.SetPixels(pixels);tex.Apply();return tex;
        }
        void DrawOptions(int id)
        {
            optionsScroll=GUILayout.BeginScrollView(optionsScroll,GUILayout.Height(Mathf.Max(72,optionsRect.height-FeedbackFooterHeight-70)));
            try{DrawOptionsContent();}finally{GUILayout.EndScrollView();}
            DrawOptionsFeedback();
            if(GUILayout.Button("关闭（F7）")){optionsOpen=false;bindingCapture=null;}
            GUI.DragWindow(new Rect(0,0,410,25));
        }
        void DrawOptionsContent()
        {
            GUILayout.Label("— 外观 —");
            GUILayout.Label("皮肤和 UI 只改自己，队友看到的是你自己的选择。");
            if(GUILayout.Button("原版外观"))PickSkin(null);
            skinScroll=GUILayout.BeginScrollView(skinScroll,GUILayout.Height(140));
            try
            {
                if(skinChoices!=null)foreach(var choice in skinChoices)
                    if(GUILayout.Button((string)Value(choice,"Name")))PickSkin(choice);
            }
            finally{GUILayout.EndScrollView();}
            GUILayout.Label(SkinHotReload.Busy||SkinHotReload.Status.Length>0?SkinHotReload.Status:skinStatus);
            GUILayout.Label("— 名字 —");
            GUILayout.Label("名字颜色同步给队友的名字、地图与方位提示。");
            GUILayout.BeginHorizontal();
            foreach(var pair in new[]{("原版白色","white"),("黄色","yellow"),("绿色","green"),("青色","cyan"),("橙色","orange"),("红色","red")})
                if(GUILayout.Button(pair.Item1)&&api!=null) SelectNameColour(pair.Item2);
            GUILayout.EndHorizontal();
            GUILayout.Label("— 队伍与画面 —");
            directionHeads.Value=GUILayout.Toggle(directionHeads.Value,"队友离开屏幕时显示方向与距离");
            lowLatency.Value=GUILayout.Toggle(lowLatency.Value,"动作平滑（降低队友移动延迟；关闭后恢复原版插值）");
            Comfort.Magnet.Value=GUILayout.Toggle(Comfort.Magnet.Value,"念珠与结晶自动吸附（只影响自己拾取）");
            DrawRosaryOptions();
            DrawTravelOptions();
            GUILayout.Label("安全脱困：["+escapeKey.Value+"]；可破坏平台不同步，各端各自显示。");
        }
        void PickSkin(object choice)
        {
            if(SkinHotReload.Available)
            {
                if(!CanChangeSkin){SkinHotReload.Reject("正在换图，请进入场景后再试。");return;}
                SkinHotReload.Request(this,choice);return;
            }
            var game=global::GameManager.instance;
            var state=Value(game,"GameState")?.ToString();
            if(api?.NetClient.IsConnected==true||state=="PLAYING"||state=="PAUSED")
            {skinStatus="请先回到主菜单并退出房间，再选皮肤。";return;}
            ChooseSkin.Invoke(null,new[]{choice});
            skinStatus=(choice==null?"已选择原版":"已选择 "+Value(choice,"Name"))+"；重启游戏应用完整皮肤与 UI";
        }
        internal IClientApi SkinApi=>api;
        internal bool SkinTransition=>global::GameManager.instance!=null&&global::GameManager.instance.IsInSceneTransition;
        internal bool CanChangeSkin=>!SkinTransition;
        internal object CurrentSkin()
        {
            foreach(var pack in (IList)SkinChoices.Invoke(null,null))if(Flag(pack,"IsActive"))return pack;
            return null;
        }
    }
}
