using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using Newtonsoft.Json;
using SSMP.Api.Client;
using UnityEngine;

namespace Silksong.CoopAssist
{
    public sealed partial class CoopAssistPlugin
    {
        ConfigEntry<string> waypointConfig;
        Dictionary<string, Waypoint> waypoints = new Dictionary<string, Waypoint>();
        WaypointTrip waypointTrip;
        string SaveIdentity => GameManager.instance == null ? null : NativeSaveBridge.Identity(GameManager.instance.profileID);

        void ConfigureWaypoints()
        {
            // The former respawn setting stores exactly the same scene, entrance and position.
            waypointConfig = Config.Bind("Play", "CustomRespawnPoints", "{}",
                "Personal travel points by save slot. Edit with the in-game controls.");
            try
            {
                waypoints = JsonConvert.DeserializeObject<Dictionary<string, Waypoint>>(waypointConfig.Value) ?? waypoints;
            }
            catch (JsonException error)
            {
                Logger.LogWarning("[Waypoint] Invalid saved settings: " + error.Message);
            }
            var save = typeof(ClientAddon).Assembly.GetType("SSMP.Game.Client.Save.CoopSave", true);
            new Harmony("silksong.coopassist.waypoints").Patch(AccessTools.Method(save, "OnClearSaveFile"),
                postfix: new HarmonyMethod(typeof(CoopAssistPlugin), nameof(AfterWaypointSaveCleared)));
        }

        static void AfterWaypointSaveCleared(int __2)
        {
            var plugin = Instance;
            var key = NativeSaveBridge.Identity(__2);
            if (plugin == null || key == null || !plugin.waypoints.Remove(key)) return;
            if (plugin.waypointTrip?.Save == key) plugin.waypointTrip = null;
            plugin.SaveWaypoints();
        }

        void SaveWaypoints() => waypointConfig.Value = JsonConvert.SerializeObject(waypoints);

        void SetWaypoint()
        {
            var key = SaveIdentity;
            if (key == null || !Ready || Down || !GameManager.instance.IsGameplayScene() ||
                applyingSnapshot || snapshotToken.Length > 0 || waypointTrip != null)
            {
                SayChat("请回到游戏，在可以操作时站到安全地面再设置传送点。");
                return;
            }
            var hero = HeroController.instance;
            var here = (Vector2)hero.transform.position;
            var scene = Scene;
            var entrance = Value(Value(GameManager.instance, "LastSceneLoad"), "EntryGateName") as string ?? "";
            if (!hero.cState.onGround || !SafeAt(here, out var floor) || floor.attachedRigidbody != null ||
                !TrySceneGate(scene, entrance, out var gate))
            {
                SayChat("请选择有正常入口的场景，并站在固定安全地面上。");
                return;
            }
            var point = new Waypoint { Scene = scene, Gate = gate, X = here.x, Y = here.y };
            if (!point.Valid)
            {
                SayChat("当前位置无法设为传送点。");
                return;
            }
            waypoints[key] = point;
            SaveWaypoints();
            SayChat("已设置传送点。按 " + waypointKey.Value + " 前往这里。");
        }

        void ClearWaypoint()
        {
            var key = SaveIdentity;
            if (key == null || !waypoints.Remove(key))
            {
                SayChat("当前存档没有已设置的传送点。");
                return;
            }
            waypointTrip = null;
            SaveWaypoints();
            SayChat("已删除当前存档的传送点。");
        }

        void RequestWaypoint() => StartWaypoint(false);
        void RequestWaypointFromMenu() => StartWaypoint(true);

        void StartWaypoint(bool fromMenu)
        {
            var key = SaveIdentity;
            if (key == null || !waypoints.TryGetValue(key, out var point) || point == null || !point.Valid)
            {
                SayChat("当前存档还没有传送点：站在固定安全地面按 " + setWaypointKey.Value + " 设置。");
                return;
            }
            if (waypointTrip != null) { SayChat("正在前往传送点，请稍候。"); return; }
            if (requestToken.Length > 0) { SayChat("正在等待队友传送，请稍候。"); return; }
            if (snapshotToken.Length > 0 || applyingSnapshot) { SayChat("正在同步存档，请稍候再传送。"); return; }
            if (!SaveReady) { SayChat("存档还在加载，请稍候再传送。"); return; }
            if (!GameManager.instance.IsGameplayScene() ||
                !(Playing || fromMenu && GameManager.instance.GameState == GlobalEnums.GameState.PAUSED))
            {
                SayChat("请回到游戏后再使用传送点。");
                return;
            }
            var scene = Scene;
            if (scene != point.Scene && !GateExists(point.Scene, point.Gate))
            {
                SayChat("传送点的场景入口已不可用，请重新设置。");
                return;
            }
            arrival.Cancel();
            travel = null;
            optionsOpen = false;
            waypointTrip = new WaypointTrip(key, scene, point, Time.unscaledTime);
            if (fromMenu && !Playing)
            {
                UIManager.instance.UIClosePauseMenu();
                UIManager.instance.TogglePauseGame();
            }
            SayChat("正在前往传送点。");
        }

        void TickWaypoint()
        {
            var trip = waypointTrip;
            if (trip == null) return;
            try
            {
                AdvanceWaypoint(trip);
            }
            catch (Exception error)
            {
                Logger.LogError("[Waypoint] " + error);
                FinishWaypoint("传送未完成。");
            }
        }

        void AdvanceWaypoint(WaypointTrip trip)
        {
            bool loading = Loading;
            var hero = HeroController.instance;
            if (Down || hero != null && hero.cState.dead ||
                !trip.CanContinue(SaveIdentity, loading ? null : Scene, Time.unscaledTime, loading))
            {
                FinishWaypoint("角色、场景或存档状态已变化，传送已取消。");
                return;
            }
            if (loading || !Playing) return;
            if (PlayerData.instance.atBench && !trip.EnteringTarget && !trip.BenchExitSent)
            {
                trip.BenchExitSent = true;
                var bench = FindRestingBench(hero);
                if (bench != null)
                    bench.SendEvent("GET UP");
                else
                    SayChat("传送已开始：请起身离开长椅，传送会自动继续。");
                return;
            }
            if (!Ready || !hero.CanInput()) return;
            var target = trip.Target;
            if (!trip.EnteringTarget && Scene != target.Scene)
            {
                hero.RecordLeaveSceneCState();
                trip.BeginTransition(Time.unscaledTime);
                GameManager.instance.BeginSceneTransition(new GameManager.SceneLoadInfo {
                    SceneName = target.Scene,
                    EntryGateName = target.Gate,
                    WaitForSceneTransitionCameraFade = true
                });
                if (!GameManager.instance.IsInSceneTransition) FinishWaypoint("游戏暂未接受传送。");
                return;
            }
            var position = new Vector2(target.X, target.Y);
            if (!SafeAt(position, out var floor) || floor.attachedRigidbody != null)
            {
                FinishWaypoint("传送点附近已不安全，已停在当前正常位置。");
                return;
            }
            PlaceHeroAt(position);
            FinishWaypoint("已到达传送点。");
        }

        static PlayMakerFSM FindRestingBench(HeroController hero)
        {
            if (hero == null) return null;
            var origin = hero.transform.position;
            // Only the nearby bench object is addressed, and only while the hero is seated. A bench that is
            // not resting ignores the event, so the state name is not used to filter it out here.
            return UnityEngine.Object.FindObjectsByType<PlayMakerFSM>(FindObjectsSortMode.None)
                .Where(fsm => fsm.isActiveAndEnabled && fsm.FsmName == "Bench Control" &&
                    (fsm.transform.position - origin).sqrMagnitude < 25)
                .OrderBy(fsm => (fsm.transform.position - origin).sqrMagnitude)
                .FirstOrDefault();
        }

        void FinishWaypoint(string message)
        {
            waypointTrip = null;
            SayChat(message);
        }
    }
}
