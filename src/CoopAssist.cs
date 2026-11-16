using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using SSMP.Api.Client;
using SSMP.Api.Server;
using SSMP.Api.Server.Networking;
using SSMP.Networking.Packet;
using SSMP.Networking.Packet.Data;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Silksong.CoopAssist
{
    public sealed class AssistClient : ClientAddon
    {
        public override uint ApiVersion => 1;
        protected override string Name => "CoopAssist";
        protected override string Version => ReleaseInfo.NetworkVersion;
        public override bool NeedsNetwork => true;
        public override void Initialize(IClientApi api)
        {
            CoopAssistPlugin.Instance?.Attach(api);
            var receiver = api.NetClient.GetNetworkReceiver<AssistPacket>(this, AppearanceWire.ClientData);
            receiver.RegisterPacketHandler<HostAnchor>(AssistPacket.HostAnchor, a => CoopAssistPlugin.Instance?.QueueAnchor(a));
            receiver.RegisterPacketHandler<TeamTravelMessage>(AssistPacket.TeamTravel, a => CoopAssistPlugin.Instance?.QueueTravel(a));
            receiver.RegisterPacketHandler<Appearance>(AssistPacket.Appearance, a => CoopAssistPlugin.Instance?.QueueAppearance(a));
            receiver.RegisterPacketHandler<NameStyleMessage>(AssistPacket.NameStyle, a => CoopAssistPlugin.Instance?.QueueNameStyle(a));
            receiver.RegisterPacketHandler<HostSnapshotMessage>(AssistPacket.HostSnapshot, a => CoopAssistPlugin.Instance?.QueueSnapshot(a));
            receiver.RegisterPacketHandler<SharedMapMessage>(AssistPacket.SharedMaps, a => CoopAssistPlugin.Instance?.QueueMapState(a));
            receiver.RegisterPacketHandler<RosaryMessage>(AssistPacket.Rosaries, a => CoopAssistPlugin.Instance?.QueueRosary(a));
            receiver.RegisterPacketHandler<EquipmentMessage>(AssistPacket.Equipment, a => CoopAssistPlugin.Instance?.QueueEquipment(a));
            CoopAssistPlugin.Instance?.SetAppearanceSender(api.NetClient.GetNetworkSender<AssistPacket>(this), this);
        }
    }

    public sealed class AssistServer : ServerAddon
    {
        public override uint ApiVersion => 1;
        protected override string Name => "CoopAssist";
        protected override string Version => ReleaseInfo.NetworkVersion;
        public override bool NeedsNetwork => true;
        public override void Initialize(IServerApi api)
        {
            var tx = api.NetServer.GetNetworkSender<AssistPacket>(this);
            var ledger = new AppearanceLedger();
            var styles = new NameStyleLedger();
            var travelLedger = new TeamTravelLedger();
            api.ServerManager.PlayerDisconnectEvent += player => travelLedger.Remove(player.Id);
            api.ServerManager.ServerShutdownEvent += travelLedger.Clear;
            // SSMP replaces its instantiator even on repeated calls. Acquire ONCE for all handlers.
            var receiver = api.NetServer.GetNetworkReceiver<AssistPacket>(this, AppearanceWire.ServerData);
            receiver.RegisterPacketHandler<RosaryMessage>(AssistPacket.Rosaries, (id, data) => CoopAssistPlugin.Instance?.QueueRosary(id, data));
            receiver.RegisterPacketHandler<EquipmentMessage>(AssistPacket.Equipment, (id, data) =>
            {
                if (!data.Valid || api.ServerManager.GetPlayer(id) == null) return;
                data.Peer = id;
                foreach (var peer in api.ServerManager.Players)
                    if (peer.Id != id) tx.SendCollectionData(AssistPacket.Equipment, data, peer.Id);
            });
            receiver.RegisterPacketHandler<SharedMapMessage>(AssistPacket.SharedMaps,(id,data)=>{
                if(api.ServerManager.GetPlayer(id)!=null)CoopAssistPlugin.Instance?.QueueMapOffer(id,data);
            });
            receiver.RegisterPacketHandler<TeamTravelMessage>(AssistPacket.TeamTravel, (id, data) => {
                    if(api.ServerManager.GetPlayer(id)==null || api.ServerManager.GetPlayer(data.Peer)==null)return;
                    if(travelLedger.Relay(id,data,(double)System.Diagnostics.Stopwatch.GetTimestamp()/System.Diagnostics.Stopwatch.Frequency,out var recipient,out var routed))
                        tx.SendCollectionData(AssistPacket.TeamTravel,routed,recipient);
                });
            CoopAssistPlugin.Instance?.AttachServer(api, tx);
            api.ServerManager.PlayerDisconnectEvent += player => { ledger.Remove(player.Id); NetworkDiagnostics.ForgetPeer(player.Id); };
            api.ServerManager.ServerShutdownEvent += ledger.Clear;
            api.ServerManager.PlayerDisconnectEvent += player => styles.Remove(player.Id);
            api.ServerManager.ServerShutdownEvent += styles.Clear;
            receiver.RegisterPacketHandler<NameStyleMessage>(AssistPacket.NameStyle,(id,data)=>{
                if(api.ServerManager.GetPlayer(id)==null || !data.Valid)return;
                tx.BroadcastCollectionData(AssistPacket.NameStyle,styles.Publish(id,data.Flags));
                foreach(var saved in styles.Snapshot())
                    if(saved.Id!=id && api.ServerManager.GetPlayer(saved.Id)!=null)
                        tx.SendCollectionData(AssistPacket.NameStyle,saved,id);
            });
            receiver.RegisterPacketHandler<HostSnapshotMessage>(AssistPacket.HostSnapshot,
                (id, data) => CoopAssistPlugin.Instance?.QueueSnapshotRequest(id, data));
            receiver.RegisterPacketHandler<Appearance>(AssistPacket.Appearance, (id, data) => {
                    if (api.ServerManager.GetPlayer(id) == null || !Appearance.Valid(data.Colour)) return;
                    var relay=ledger.Publish(id,data.Colour);
                    tx.BroadcastCollectionData(AssistPacket.Appearance,relay);
                    foreach(var saved in ledger.Snapshot())
                        if(saved.Id!=id && api.ServerManager.GetPlayer(saved.Id)!=null)
                            tx.SendCollectionData(AssistPacket.Appearance,saved,id);
                });
        }
    }

    [BepInPlugin("silksong.coopassist", "Silksong Coop Assist (Standard)", ReleaseInfo.PluginVersion)]
    [BepInDependency("org.silksong-modding.modmenu", "0.8.0")]
    [BepInDependency("ssmp")]
    [DefaultExecutionOrder(30000)]
    public sealed partial class CoopAssistPlugin : BaseUnityPlugin
    {
        // Private bindings target only the SHA-pinned native fork.
        const string ExpectedSsmp = "71E10C6808C24AB0F01E5B48CB463955A179166914652C9C4B16D9F380D11354";
        internal static CoopAssistPlugin Instance;
        IClientApi api;
        IServerApi server;
        IServerAddonNetworkSender<AssistPacket> sender;
        object coop;
        FieldInfo rescueField, checkedField, waitingField;
        PropertyInfo hostingProperty;
        MethodInfo sceneNameMethod;
        ConfigEntry<bool> spectate, joinHost;
        ConfigEntry<KeyCode> escapeKey;
        bool compatible, reportedError, wasConnected, watching;
        float pollAt, broadcastAt, safeAt;
        string safeScene = "", safeGate = "", session = Guid.NewGuid().ToString("N");
        Vector2 safePoint;
        Transform safeFloor;
        Vector3 floorAt;
        ulong sequence;
        readonly object anchorLock = new object();
        HostAnchor inbox, anchor;
        float anchorAt;
        string lastSession = "";
        ulong lastSequence;
        readonly ArrivalPolicy arrival = new ArrivalPolicy();
        HostAnchor travel;
        int lastProfile = -1;
        int terrain;
        readonly Collider2D[] overlaps = new Collider2D[24];
        HeroController colliderOwner;
        Collider2D heroCollider;
        readonly Dictionary<ushort, IClientPlayer> visible = new Dictionary<ushort, IClientPlayer>();

        void Awake()
        {
            Instance = this;
            spectate = Config.Bind("Play", "SpectateWhenDown", true, "Follow a living teammate in the same scene while awaiting rescue.");
            joinHost = Config.Bind("Play", "JoinAtHost", true, "After save alignment and getting up, join the host at their latest safe ground position.");
            escapeKey = Config.Bind("Play", "EscapeKey", KeyCode.F8, "Living player only: return to locally verified safe ground. Does not revive or force input.");
            using (var sha = SHA256.Create())
                compatible = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(typeof(ClientAddon).Assembly.Location))).Replace("-", "") == ExpectedSsmp;
            if (!compatible)
            { Logger.LogError("Coop Assist supports only the bundled SSMP 0.4.142 binary. Disabled; SSMP is left untouched."); enabled = false; return; }
            terrain = LayerMask.GetMask("Terrain");
            // The adjacent assembly is discovered by SSMP's own addon loader. Do not also RegisterAddon it.
            new Harmony("silksong.coopassist.camera").Patch(
                AccessTools.Method(typeof(CameraController), "LateUpdate"),
                postfix: new HarmonyMethod(typeof(CoopAssistPlugin), nameof(AfterCamera)));
            ConfigureEnhancements();
            ConfigureTeamTravel();
            ConfigureRosaries();
            StoryTriggerRepairs.Install();
            RuntimeRepairs.Install();
            PlayerMotionRepairs.Install();
            HitFeedbackRepairs.Install();
            PlaybackDiagnostics.Install();
            try { ReceiveAllocationRepairs.Install(Logger); }
            catch(Exception error) { Logger.LogWarning("[ReceiveAlloc] Retaining native receive loop: "+error); }
            try { NetworkDiagnostics.Install(); }
            catch(Exception error) { Logger.LogWarning("[PeerFlow] Diagnostics unavailable: "+error); }
            try { SignalIndicator.Install(Logger); }
            catch(Exception error) { Logger.LogWarning("[Signal] Indicator unavailable: "+error); }
            SceneManager.activeSceneChanged += SignalSceneChanged;
            SkinLoaderBridge.Install(Logger);
            NameFontRepairs.Install(Logger);
            Logger.LogInfo("Coop Assist "+ReleaseInfo.Version+" ["+ReleaseInfo.Revision+"] loaded. Resource alignment is manual (F9).");
        }

        internal void Attach(IClientApi client)
        {
            if (!compatible) return;
            api = client;
            var field = AccessTools.Field(client.ClientManager.GetType(), "_coopSave");
            coop = field?.GetValue(client.ClientManager);
            if (coop == null) throw new InvalidOperationException("SSMP co-op save binding missing");
            var t = coop.GetType();
            rescueField = AccessTools.Field(t, "_rescue");
            checkedField = AccessTools.Field(t, "_checkedMembers");
            waitingField = AccessTools.Field(t, "_waitingMembers");
            hostingProperty = AccessTools.Property(t, "IsHosting");
            sceneNameMethod = AccessTools.Method(typeof(ClientAddon).Assembly.GetType("SSMP.Util.SceneUtil"), "GetCurrentSceneName");
            if (rescueField == null || checkedField == null || waitingField == null || hostingProperty == null || sceneNameMethod == null)
                throw new InvalidOperationException("SSMP status bindings missing; no gameplay changes were made");
            ApplyBenchBinding();
            api.ClientManager.DisconnectEvent += Disconnected;
            api.ClientManager.PlayerDisconnectEvent += PeerDisconnected;
            api.ClientManager.PlayerEnterSceneEvent += PeerSceneChanged;
            api.ClientManager.PlayerLeaveSceneEvent += PeerSceneChanged;
            Logger.LogInfo("Coop Assist: client API and rescue status bound.");
        }

        internal void AttachServer(IServerApi value, IServerAddonNetworkSender<AssistPacket> tx)
        { server = value; sender = tx; }
        internal void QueueAnchor(HostAnchor value) { lock (anchorLock) inbox = value; }
        bool Hosting => hostingProperty?.GetValue(coop) is Func<bool> host && host();
        string Scene => (string)sceneNameMethod.Invoke(null, null);
        internal static object Value(object obj, string name) => ReflectionCache.Get(obj, name);
        static bool Flag(object obj, string name) => Value(obj, name) is bool b && b;
        static int Number(object obj, string name) => Value(obj, name) is int n ? n : -1;
        object Rescue => rescueField?.GetValue(coop);
        bool Down => Rescue != null;
        bool Waiting => Value(Rescue, "Outcome")?.ToString() == "Waiting";
        bool ScreenBack => Flag(Rescue, "ScreenBack");
        bool MemberDown(ushort id) => waitingField?.GetValue(coop) is IDictionary d && d.Contains(id);
        bool Checked(ushort id) => checkedField?.GetValue(coop) is ICollection<ushort> ids && ids.Contains(id);
        static bool Loading => global::GameManager.instance == null || global::GameManager.instance.IsInSceneTransition ||
            !Flag(global::GameManager.instance, "hasFinishedEnteringScene");
        internal static bool SignalLoading=>Loading;
        bool Ready
        {
            get
            {
                var hero = HeroController.instance;
                return !Loading && hero != null && hero.gameObject.activeInHierarchy &&
                    !Flag(Value(hero, "cState"), "dead") && !Flag(hero, "controlReqlinquished") &&
                    !Flag(PlayerData.instance, "atBench") && Number(PlayerData.instance, "health") > 0;
            }
        }

        void PeerDisconnected(IClientPlayer player){NetworkDiagnostics.ForgetPeer(player.Id);ForgetRosaryPeer(player.Id);}
        void PeerSceneChanged(IClientPlayer player)=>NetworkDiagnostics.Metrics.ResetSignal(player.Id);
        void SignalSceneChanged(UnityEngine.SceneManagement.Scene previous,UnityEngine.SceneManagement.Scene current)
        {
            NetworkDiagnostics.Metrics.ResetSignal();
            // A new scene brings its own vignette; the earlier level belongs to the scene that has just unloaded.
            // A new scene builds new plates; the references belong to the scene that has just unloaded.
            ForgetWaitingVignette();
        }

        void Disconnected()
        {
            ClearRosarySession();
            ClearEquipment();
            CloseCocoonMap();
            ShowWaitingVignette();
            TrySessionCheckpoint(true);
            ClearHostSnapshots();
            ClearSharedMaps();
            waypointTrip = null;
            grandGateSave = null;
            lock (anchorLock) inbox = null;
            anchor = travel = null; watching = false; arrival.Cancel(); safeScene = "";
            lastSession = ""; lastSequence = 0; lastProfile = -1; wasConnected = false;
            ClearEnhancements();
            ClearTeamTravel();
            PlaybackDiagnostics.Clear();
        }

        void Update()
        {
            if (api == null || !compatible || coop == null) return;
            try
            {
                var connected = api.NetClient.IsConnected;
                if (!connected) { if (wasConnected) Disconnected(); return; }
                if (!wasConnected) { wasConnected = true; arrival.Reset(); }
                TickHostSnapshots();
                lock (anchorLock)
                {
                    if (inbox != null)
                    {
                        var a = inbox; inbox = null;
                        if (a.Session.Length == 32 && a.Sequence > 0 &&
                            ArrivalPolicy.ValidAnchor(a.Scene, a.Gate, a.X, a.Y) &&
                            (lastSession != a.Session || a.Sequence > lastSequence))
                        { anchor = a; anchorAt = Time.unscaledTime; lastSession = a.Session; lastSequence = a.Sequence; }
                    }
                }
                if (Time.unscaledTime >= pollAt)
                {
                    pollAt = Time.unscaledTime + .2f;
                    visible.Clear();
                    foreach (var p in api.ClientManager.Players) if (p.IsInLocalScene && p.PlayerObject != null) visible[p.Id] = p;
                    if (!Loading)
                    {
                        var profile = Number(global::GameManager.instance, "profileID");
                        if (profile >= 0 && profile != lastProfile) { lastProfile = profile; arrival.Reset(); }
                    }
                    CacheSafeGround();
                    BroadcastAnchor();
                    TryArrival();
                    TrySessionCheckpoint(false);
                }
                TickTeamTravel();
                if (waypointTrip == null && Input.GetKeyDown(escapeKey.Value) && !travelPickerOpen && !optionsOpen &&
                    bindingCapture == null && !AssistChatOpen()) Escape();
            }
            catch (Exception e)
            {
                // Stop our movement instructions, not SSMP or the game's loader.
                CloseTravelPicker();pendingMenuPeer=-1;
                arrival.Cancel(); requestToken="";destination=null;watching = false;
                if (!reportedError) { reportedError = true; Logger.LogError("Coop Assist stopped an operation: " + e); }
            }
        }

        bool SafeAt(Vector2 position, out Collider2D ground)
        {
            ground = null;
            var hero = HeroController.instance;
            if (hero == null || terrain == 0) return false;
            if(colliderOwner!=hero || heroCollider==null || !heroCollider.enabled)
            {colliderOwner=hero;heroCollider=hero.GetComponents<Collider2D>().FirstOrDefault(c=>c.enabled && !c.isTrigger);}
            var body = heroCollider;
            if (body == null) return false;
            var b = body.bounds;
            var center = position + (Vector2)(b.center - hero.transform.position);
            var size = (Vector2)b.size * .85f;
            var filter=new ContactFilter2D();filter.SetLayerMask(terrain);filter.useTriggers=false;
            var n = Physics2D.OverlapBox(center, size, 0, filter, overlaps);
            if (n > 0) return false;
            filter.SetLayerMask(~0);filter.useTriggers=true;
            n = Physics2D.OverlapBox(center, size, 0, filter, overlaps);
            if (n == overlaps.Length) return false;
            for (var i = 0; i < n; i++)
                if (overlaps[i] != null && overlaps[i].GetComponentInParent<DamageHero>() != null) return false;
            var hit = Physics2D.Raycast(new Vector2(center.x, center.y - b.extents.y + .12f), Vector2.down, .45f, terrain);
            if (hit.collider == null || hit.collider.isTrigger || hit.collider.GetComponentInParent<DamageHero>() != null) return false;
            ground = hit.collider;
            return true;
        }

        // Call only after a fresh local SafeAt check. Death/input state stays game-owned.
        static void PlaceHeroAt(Vector2 point)
        {
            var hero=HeroController.instance;
            hero.transform.position=new Vector3(point.x,point.y,hero.transform.position.z);
            var body=hero.GetComponent<Rigidbody2D>();
            if(body!=null)body.linearVelocity=Vector2.zero;
            GameCameras.instance?.cameraController?.PositionToHero(true);
        }

        void CacheSafeGround()
        {
            if (!Ready || Down || !Flag(Value(HeroController.instance, "cState"), "onGround")) return;
            var p = (Vector2)HeroController.instance.transform.position;
            if (!SafeAt(p, out var floor)) return;
            safeScene = Scene; safePoint = p; safeAt = Time.unscaledTime;
            safeFloor = floor.transform; floorAt = safeFloor.position;
            var preferred=(string)Value(Value(global::GameManager.instance,"LastSceneLoad"),"EntryGateName")??"";
            TrySceneGate(Scene,preferred,out safeGate);
        }

        bool SafeCache => safeFloor != null && safeScene == Scene &&
            (safeFloor.position - floorAt).sqrMagnitude < .01f && SafeAt(safePoint, out _);

        void BroadcastAnchor()
        {
            if (server == null || sender == null || !server.NetServer.IsStarted || !Hosting ||
                !Ready || Down || Time.unscaledTime < broadcastAt || !SafeCache || Time.unscaledTime - safeAt > 4) return;
            broadcastAt = Time.unscaledTime + .5f;
            var settings = Value(api.ClientManager, "_modSettings");
            var key = (string)Value(settings, "AuthKey");
            var host = server.ServerManager.Players.FirstOrDefault(p => p.AuthKey == key && !string.IsNullOrEmpty(key));
            if (host == null || !ArrivalPolicy.ValidAnchor(safeScene, safeGate, safePoint.x, safePoint.y)) return;
            sender.BroadcastSingleData(AssistPacket.HostAnchor, new HostAnchor {
                Session = session, Sequence = ++sequence, Host = host.Id, Scene = safeScene, Gate = safeGate, X = safePoint.x, Y = safePoint.y
            });
        }

        bool GateExists(string scene, string gate)
        {
            var map = SceneTeleportMap.GetTeleportMap();
            if (map == null) return false;
            bool Has(string name) => map.TryGetValue(name, out var item) &&
                item.TransitionGates != null && item.TransitionGates.Contains(gate);
            return Has(scene) || (WorldInfo.SubSceneNameSuffixes != null && WorldInfo.SubSceneNameSuffixes.Any(s => Has(scene + s)));
        }

        bool TrySceneGate(string scene,string preferred,out string gate)
        {
            gate=preferred;
            if(!string.IsNullOrEmpty(gate) && GateExists(scene,gate))return true;
            // A player who loaded at a bench has LastSceneLoad.EntryGateName=RestBench,
            // which isn't a transition gate. Select an actual map entrance before sending
            // a travel/return destination, then verify the final point against local terrain.
            var map=SceneTeleportMap.GetTeleportMap();
            if(map==null)return false;
            string First(string name)=>map.TryGetValue(name,out var entry) && entry.TransitionGates!=null
                ?entry.TransitionGates.Where(x=>!string.IsNullOrEmpty(x)).OrderBy(x=>x,StringComparer.Ordinal).FirstOrDefault():null;
            gate=First(scene);
            if(gate==null && WorldInfo.SubSceneNameSuffixes!=null)
                foreach(var suffix in WorldInfo.SubSceneNameSuffixes){gate=First(scene+suffix);if(gate!=null)break;}
            gate=gate??"";
            return gate.Length>0;
        }

        void TryArrival()
        {
            if(waypointTrip!=null)return;
            if (!joinHost.Value || Hosting) { arrival.Cancel(); return; }
            if (arrival.Expired(Time.unscaledTime))
            { arrival.Cancel(); travel = null; Say("集合超时，已停止自动转场；可以正常操作和退出。"); return; }
            if (!Ready || Down) return;
            var a = travel ?? anchor;
            if (a == null || !ArrivalPolicy.CanArrive(arrival.Pending, api.NetClient.IsConnected, Hosting, Ready, Down,
                Checked(a.Host) && !MemberDown(a.Host), travel != null ? 0 : Time.unscaledTime - anchorAt)) return;
            if (arrival.Moving && Scene != arrival.Destination) return;
            if (Scene != a.Scene)
            {
                if (!GateExists(a.Scene, a.Gate)) { arrival.Cancel(); Say("无法确认房主场景入口，已跳过自动集合。"); return; }
                travel = a;
                HeroController.instance.RecordLeaveSceneCState();
                global::GameManager.instance.BeginSceneTransition(new global::GameManager.SceneLoadInfo {
                    SceneName = a.Scene, EntryGateName = a.Gate, WaitForSceneTransitionCameraFade = true
                });
                if (global::GameManager.instance.IsInSceneTransition) arrival.Begin(a.Scene, Time.unscaledTime);
                else { arrival.Cancel(); travel = null; Say("游戏暂未接受集合转场，已停止重试。"); }
                return;
            }
            // The source point is only a hint: collision is verified against this game's loaded world.
            var point = new Vector2(a.X, a.Y);
            if (SafeAt(point, out _))
            {
                PlaceHeroAt(point);
                Say("已在房主的安全落脚点集合。");
            }
            else Say("已进入房主场景；落脚点有障碍，保留在正常入口。");
            arrival.Cancel(); travel = null;
        }

        void Escape()
        {
            if (!Ready || Down)
            { Say("倒地请等队友救援，或按你设置的返回长椅键（默认 F5／L3）；转场中请稍候。"); return; }
            if (!SafeCache)
            {
                var gate = (string)Value(Value(global::GameManager.instance, "LastSceneLoad"), "EntryGateName");
                if (!string.IsNullOrEmpty(gate) && GateExists(Scene, gate))
                {
                    arrival.Cancel(); travel = null;
                    HeroController.instance.RecordLeaveSceneCState();
                    global::GameManager.instance.BeginSceneTransition(new global::GameManager.SceneLoadInfo {
                        SceneName = Scene, EntryGateName = gate, WaitForSceneTransitionCameraFade = true
                    });
                    Say(global::GameManager.instance.IsInSceneTransition ? "正在通过正常入口重新进入本场景。" : "当前无法重新进入，请稍后再试。");
                }
                else Say("没有可确认的安全落脚点或入口，已取消脱困。");
                return;
            }
            PlaceHeroAt(safePoint);
            Say("已返回本场景最近的安全落脚点。");
        }

        static void AfterCamera(CameraController __instance) { Instance?.Watch(__instance); }
        void Watch(CameraController camera)
        {
            if (api == null || !compatible) return;
            try
            {
                // Most frames are ordinary play. Do not build a sorted teammate query for a
                // spectator camera that is not active; inspect rescue state before any scan.
                if (!CanUseCocoonView || cocoonMap != null)
                { StopWatching(camera); return; }
                var target = CocoonTarget;
                var watch = ArrivalPolicy.CanWatch(true, true, true, false,
                    target != null && target.IsInLocalScene && target.PlayerObject != null &&
                    target.PlayerObject.activeInHierarchy, target == null || MemberDown(target.Id));
                if (watch)
                {
                    var next = target.PlayerObject.transform.position;
                    next.z = camera.transform.position.z;
                    // No camera mode, target, player position, input or death flags are changed.
                    camera.transform.position = camera.KeepWithinSceneBounds(next);
                    watching = true;
                }
                else StopWatching(camera);
            }
            catch (Exception e)
            { if (!reportedError) { reportedError = true; Logger.LogWarning("Spectator stopped: " + e.Message); } StopWatching(camera); }
        }

        void StopWatching(CameraController camera)
        {
            if (watching) { watching=false; if(!Loading&&HeroController.instance!=null)camera.PositionToHero(true); }
        }

        void Say(string message)
        {
            feedback.Publish(message,Time.unscaledTime);
            Logger.LogInfo(message);
        }
        void OnDestroy()
        {
            CloseTravelPicker();
            CloseCocoonMap();
            ShowWaitingVignette();
            new Harmony(StoryTriggerRepairs.PatchId).UnpatchSelf();
            new Harmony(RosaryPurchaseHooks.PatchId).UnpatchSelf();
            RosaryPurchaseHooks.ClearPayments();
            DestroyRosaryUi();
            waypointTrip=null;ClearSharedMaps();
            new Harmony("silksong.coopassist.travelpicker").UnpatchSelf();
            new Harmony("silksong.coopassist.camera").UnpatchSelf();
            new Harmony("silksong.coopassist.repairs").UnpatchSelf();
            new Harmony("silksong.coopassist.namefonts").UnpatchSelf();
            new Harmony("silksong.coopassist.sessionfreedom").UnpatchSelf();
            new Harmony("silksong.coopassist.hostresources").UnpatchSelf();
            new Harmony("silksong.coopassist.waypoints").UnpatchSelf();
            new Harmony("silksong.coopassist.mapshop").UnpatchSelf();
            new Harmony("silksong.coopassist.comfort").UnpatchSelf();
            new Harmony("silksong.coopassist.skinloader").UnpatchSelf();
            new Harmony("silksong.coopassist.playermotion").UnpatchSelf();
            new Harmony("silksong.coopassist.hitfeedback").UnpatchSelf();
            new Harmony("silksong.coopassist.playbackmetrics").UnpatchSelf();
            new Harmony("silksong.coopassist.receiveallocations").UnpatchSelf();
            PlaybackDiagnostics.Clear();
            if(head!=null)Destroy(head);
            SceneManager.activeSceneChanged -= SignalSceneChanged;
            if(uiFont!=null)Destroy(uiFont);
            if (api != null) api.ClientManager.DisconnectEvent -= Disconnected;
            if (api != null) api.ClientManager.PlayerDisconnectEvent -= PeerDisconnected;
            if (api != null) api.ClientManager.PlayerEnterSceneEvent -= PeerSceneChanged;
            if (api != null) api.ClientManager.PlayerLeaveSceneEvent -= PeerSceneChanged;
            if (Instance == this) Instance = null;
        }
    }
}

