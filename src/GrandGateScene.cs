using System;
using System.Collections.Generic;
using System.Linq;
using HutongGames.PlayMaker;
using UnityEngine.SceneManagement;

namespace Silksong.CoopAssist
{
    public sealed partial class CoopAssistPlugin
    {
        const string LastJudgeArena = "Coral_Judge_Arena";
        Scene grandGateScene;
        PlayerData grandGateSave;

        internal static void RedirectCompletedGate(Fsm __1, ref FsmState __2)
        {
            var next = __2?.Name;
            if (next != "Gate Open Scene" && next != "Gate Open To Cinematic" &&
                next != "Start Fall" && next != "Start Refight") return;
            if (Instance?.api?.NetClient.IsConnected != true || __1.Name != "Control") return;
            var root = __1.GameObject;
            var save = PlayerData.instance;
            if (root == null || root.name != "Boss Scene" || root.transform.parent != null ||
                root.scene.name != LastJudgeArena || !HasCompletedGrandGate(save) || save.blackThreadWorld) return;

            // Use the arena's completed branch before an opening or fight intro begins.
            var defeated = __1.GetState("Defeated");
            if (defeated == null) return;
            ReleaseBattleCamera(__1);
            __2 = defeated;
        }

        static void ReleaseBattleCamera(Fsm fsm)
        {
            // The normal opening states release the combat camera lock.
            var cameraLock = fsm.Variables.FindFsmGameObject("CamLock B")?.Value;
            if (cameraLock != null) cameraLock.SetActive(false);
        }

        static bool HasCompletedGrandGate(PlayerData save) =>
            save != null && save.defeatedLastJudge && save.SeenLastJudgeGateOpen;

        internal void ObserveGrandGateRecords(object update)
        {
            if (api?.NetClient.IsConnected != true ||
                !(Value(update, "FlagNames") is List<string> names) ||
                !names.Any(name => Array.IndexOf(GrandGateProgress.StoryRecords, name) >= 0)) return;

            var save = PlayerData.instance;
            if (!HasCompletedGrandGate(save)) return;
            ScheduleCheckpoint();

            var scene = SceneManager.GetActiveScene();
            if (scene.name != LastJudgeArena || save.blackThreadWorld) return;
            grandGateScene = scene;
            grandGateSave = save;
        }

        void TickGrandGateProgress()
        {
            var save = grandGateSave;
            if (save == null) return;
            if (api?.NetClient.IsConnected != true || SceneManager.GetActiveScene() != grandGateScene ||
                save != PlayerData.instance || !HasCompletedGrandGate(save) ||
                save.blackThreadWorld || save.health <= 0 || Down)
            {
                grandGateSave = null;
                return;
            }
            if (!Ready || !Playing) return;
            grandGateSave = null;

            try
            {
                foreach (var root in grandGateScene.GetRootGameObjects())
                {
                    if (root.name != "Boss Scene") continue;
                    var controller = FSMUtility.LocateFSM(root, "Control");
                    if (controller == null || !controller.isActiveAndEnabled) continue;
                    switch (controller.ActiveStateName)
                    {
                        case "Encountered":
                        case "Out of Range":
                        case "In Range":
                        case "Defeated Pause":
                        case "Wait for enter":
                        case "Wait for enter 2":
                        case "Wait for enter 3":
                            break;
                        case "Battle":
                            // The enemy has a separate FSM. A progress-only update can
                            // arrive while it is alive locally, without a combat packet.
                            var judge = root.transform.Find("Last Judge");
                            if (judge != null) judge.gameObject.SetActive(false);
                            break;
                        default:
                            return;
                    }
                    // Use the same branch as entering an arena after victory. The saved
                    // completion marker takes Defeated -> Seen Gate Open? -> Gate Opened.
                    ReleaseBattleCamera(controller.Fsm);
                    controller.Fsm.SetState("Defeated");
                    Logger.LogInfo("[GrandGate] Restored the completed gate from shared story records.");
                    return;
                }
            }
            catch (Exception error)
            {
                Logger.LogWarning("[GrandGate] Could not refresh the arena: " + error.Message);
            }
        }
    }
}
