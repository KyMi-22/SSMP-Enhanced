using System;
using System.Collections.Generic;
using SSMP.Api.Client;
using System.Linq;
using UnityEngine;

namespace Silksong.CoopAssist
{
    public sealed partial class CoopAssistPlugin
    {
        bool ownCocoonView;
        ushort? cocoonPeer;
        PlayMakerFSM cocoonMap;
        PlayMakerFSM quickMapHud;
        // The dark that follows the living player is the vignette plate pair drawn from the hero light.
        HeroController vignetteHero;
        Renderer[] vignetteRenderers;
        bool[] vignetteEnabled;
        bool vignetteFaultLogged;
        bool vignetteAcquired;

        bool CanUseCocoonView => api?.NetClient.IsConnected == true && Waiting && ScreenBack && !Loading;
        IClientPlayer CocoonTarget => ownCocoonView || (!spectate.Value && !cocoonPeer.HasValue)
            ? null : FindCocoonTarget();

        void TickCocoonView()
        {
            if (!CanUseCocoonView)
            {
                CloseCocoonMap();
                ownCocoonView = false;
                cocoonPeer = null;
                ShowWaitingVignette();
                return;
            }
            // A downed player cannot act, and the room's dark is drawn around the living hero: hide it for the
            // whole wait, so the scene around a teammate across the room stays readable.
            HideWaitingVignette();
            var manager = global::GameManager.instance;
            if (manager.isPaused || optionsOpen || travelPickerOpen || AssistChatOpen() || ledgerOpen)
            {
                CloseCocoonMap();
                return;
            }
            var actions = InputHandler.Instance?.inputActions;
            var mapAction = actions?.QuickMap;
            if (mapAction?.IsPressed != true) CloseCocoonMap(instant: false);
            if (mapAction?.WasPressed == true && mapAction.IsPressed) OpenCocoonMap(manager.gameMap);
            if (cocoonMap != null) return;
            int direction = Input.GetKeyDown(KeyCode.DownArrow) ? 1 : Input.GetKeyDown(KeyCode.UpArrow) ? -1 : 0;
            var target = CocoonTarget;
            if (direction != 0 && mapAction?.IsPressed != true)
            {
                var next = CocoonViewSelection.Move(target?.Id, direction,
                    api.ClientManager.Players.Where(CanWatchCocoonPeer).Select(peer => peer.Id));
                target = next.HasValue ? api.ClientManager.GetPlayer(next.Value) : null;
                if (target != null && !CanWatchCocoonPeer(target))
                {
                    target = null;
                    Say("队友刚离开当前场景，只有同场景的队友可以观战。");
                }
                else Say(target == null ? "已切回自己的视角。" : "正在观战 " + target.Username + "。↑／↓ 切换视角。");
                ownCocoonView = target == null;
                cocoonPeer = target?.Id;
            }
            else if (cocoonPeer.HasValue && target == null)
            {
                // The watched teammate died, disconnected or left the scene: do not silently retarget.
                ownCocoonView = true;
                cocoonPeer = null;
                Say("队友已离开当前场景，已切回自己的视角。");
            }
        }

        // The hero carries the vignette that darkens everything away from it; a death leaves it drawn around the
        // cocoon, so a teammate elsewhere is lit only by the room. Stopping the drawing of those plates leaves the
        // scene itself, which is what the whole wait should show. The plate object keeps running its FSM, so the
        // rescue flow still puts the room's own darkness back.
        void HideWaitingVignette()
        {
            var hero = HeroController.instance;
            if (hero == null) return;
            // Acquire once per hero: a wait that finds no plates must not scan the hierarchy every frame.
            if (vignetteHero != hero || !vignetteAcquired) AcquireWaitingVignette(hero);
            if (vignetteRenderers == null) return;
            for (int i = 0; i < vignetteRenderers.Length; i++)
            {
                var renderer = vignetteRenderers[i];
                // The scene keeps switching the pair on: keep it hidden for as long as the wait lasts.
                if (renderer != null && renderer.enabled) renderer.enabled = false;
            }
        }

        void AcquireWaitingVignette(HeroController hero)
        {
            vignetteHero = hero;
            vignetteAcquired = true;
            vignetteRenderers = null;
            vignetteEnabled = null;
            var found = new List<Renderer>();
            try
            {
                var light = hero.heroLight;
                var plates = light == null ? null : Value(light, "vignette") as Transform;
                if (plates != null) found.AddRange(plates.GetComponentsInChildren<Renderer>(true));
            }
            catch (Exception error) { ReportVignette(error); }
            try
            {
                if (hero.vignette != null && !found.Contains(hero.vignette)) found.Add(hero.vignette);
            }
            catch (Exception error) { ReportVignette(error); }
            if (found.Count == 0) return;
            vignetteRenderers = found.ToArray();
            vignetteEnabled = new bool[found.Count];
            for (int i = 0; i < found.Count; i++)
            {
                vignetteEnabled[i] = vignetteRenderers[i].enabled;
                vignetteRenderers[i].enabled = false;
            }
            Logger.LogInfo("[CocoonView] Waiting view: hiding " + found.Count + " vignette renderer(s).");
        }

        void ShowWaitingVignette()
        {
            if (vignetteRenderers != null)
                for (int i = 0; i < vignetteRenderers.Length; i++)
                    if (vignetteRenderers[i] != null && vignetteEnabled[i]) vignetteRenderers[i].enabled = true;
            vignetteRenderers = null;
            vignetteEnabled = null;
            vignetteHero = null;
            vignetteAcquired = false;
        }

        // A new scene builds a new hero and new plates; the old references are gone with the scene that unloaded.
        void ForgetWaitingVignette()
        {
            vignetteRenderers = null;
            vignetteEnabled = null;
            vignetteHero = null;
            vignetteAcquired = false;
        }

        void ReportVignette(Exception error)
        {
            if (vignetteFaultLogged) return;
            vignetteFaultLogged = true;
            Logger.LogWarning("[CocoonView] Waiting view could not reach the vignette: " + error.Message);
        }

        void OpenCocoonMap(GameMap map)
        {
            var player = PlayerData.instance;
            if (map == null || player == null || player.disableInventory || !player.HasAnyMap ||
                CollectableItemManager.IsInHiddenMode() && !map.HasAnyMapForZone(GlobalEnums.MapZone.THE_SLAB)) return;
            if (quickMapHud == null && GameCameras.instance != null)
                quickMapHud = GameCameras.instance.GetComponentsInChildren<PlayMakerFSM>(true)
                    .FirstOrDefault(fsm => fsm.FsmName == "Quick Map" && fsm.Fsm.GetState("Try Quick Map") != null);
            if (quickMapHud == null || !quickMapHud.isActiveAndEnabled) return;
            if (quickMapHud.ActiveStateName != "Idle" && quickMapHud.ActiveStateName != "Close" &&
                quickMapHud.ActiveStateName != "Close Instant") return;
            // Reuse the native HUD without entering the hero's map animation or changing rescue input locks.
            cocoonMap = quickMapHud;
            cocoonMap.SendEvent("OPEN QUICK MAP");
        }

        bool CanWatchCocoonPeer(IClientPlayer peer) =>
            Checked(peer.Id) && !MemberDown(peer.Id) && peer.IsInLocalScene;

        IClientPlayer FindCocoonTarget()
        {
            IClientPlayer first = null;
            foreach (var peer in api.ClientManager.Players)
            {
                if (!CanWatchCocoonPeer(peer)) continue;
                if (peer.Id == cocoonPeer) return peer;
                if (first == null || peer.Id < first.Id) first = peer;
            }
            // A watched teammate who left the scene returns to the local view rather than another player.
            return cocoonPeer.HasValue ? null : first;
        }

        void CloseCocoonMap(bool instant = true)
        {
            if (cocoonMap != null) cocoonMap.SendEvent(instant ? "CLOSE QUICK MAP INSTANT" : "CLOSE QUICK MAP");
            cocoonMap = null;
        }
    }
}
