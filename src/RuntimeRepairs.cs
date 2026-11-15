using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using SSMP.Api.Client;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Silksong.CoopAssist
{
    internal static class RuntimeRepairs
    {
        static readonly Assembly Ssmp = typeof(ClientAddon).Assembly;
        static Type T(string name) => Ssmp.GetType(name, true);
        static readonly Type Manager = T("SSMP.Game.Client.Entity.EntityManager");
        static readonly MethodInfo UpdateActive = AccessTools.Method(T("SSMP.Game.Client.Entity.Entity"), "UpdateIsActive");
        static readonly MethodInfo Drain = AccessTools.Method(Manager, "DrainPendingUpdates");
        static readonly MethodInfo Canonical = AccessTools.Method(T("SSMP.Util.SceneUtil"), "GetCurrentSceneName");
        public static bool LowLatency = true;
        public static void Install()
        {
            var h = new Harmony("silksong.coopassist.repairs");
            h.Patch(AccessTools.Method(Manager, "HandleReliableEntityUpdate"), postfix: new HarmonyMethod(typeof(RuntimeRepairs), nameof(AfterReliable)));
            h.Patch(AccessTools.Method(Manager, "BecomeSceneHost"), prefix: new HarmonyMethod(typeof(RuntimeRepairs), nameof(BeforePromotion)));
            h.Patch(AccessTools.Method(T("SSMP.Game.Client.ClientManager"), "OnSceneHostTransfer"), prefix: new HarmonyMethod(typeof(RuntimeRepairs), nameof(BeforeTransfer)));
            h.Patch(AccessTools.Method(T("SSMP.Fsm.PredictiveInterpolation"), "AdaptToRTT"), postfix: new HarmonyMethod(typeof(RuntimeRepairs), nameof(AfterTiming)));
            // This debug-only routine walks every FSM/state/action two seconds after every
            // scene load and writes one line per reader. Keep cheap timing metrics instead.
            h.Patch(AccessTools.Method(T("SSMP.Game.Client.Save.CoopSave"), "LogStoryReadersNow"),
                prefix: new HarmonyMethod(typeof(RuntimeRepairs), nameof(SkipStoryReaderDump)));
        }
        static bool SkipStoryReaderDump()=>false;
        static object V(object obj, string name) => ReflectionCache.Get(obj, name);
        static bool B(object obj, string name) => V(obj,name) is bool b && b;
        // The original late-entry exception skips both off and on snapshots for Aknid. Retain personal reward/health
        // handling, but accept a host's explicit living/on state so the replica is not left disabled forever.
        static void AfterReliable(object __instance, object __0, bool __1, bool __result)
        {
            if (!__result || !__1 || B(__instance,"IsSceneHost") || !B(__0,"IsActive")) return;
            var types = V(__0,"UpdateTypes") as IEnumerable;
            if (types == null || !types.Cast<object>().Any(x => x.ToString() == "Active")) return;
            if (!(V(__instance,"_entities") is IDictionary entities)) return;
            var entity = entities[V(__0,"Id")];
            if (entity != null && V(entity,"Type")?.ToString() == "Aknid") UpdateActive.Invoke(entity,new object[]{true});
        }
        static bool BeforePromotion(object __instance, uint __0)
        {
            if(B(__instance,"_sceneRoleDetermined"))return true;
            // No previous role means there is no initialized replica to promote. Use the native first-host path,
            // which initializes the entities, publishes their state and drains queued updates together.
            AccessTools.Method(Manager,"InitializeSceneHost").Invoke(__instance,new object[]{__0});
            return false;
        }
        // A transfer can name the parent gameplay scene while Unity's active scene is a loaded subscene.
        static bool BeforeTransfer(object __instance, object __0)
        {
            var scene = V(__0,"SceneName") as string;
            if (SceneManager.GetActiveScene().name == scene || !B(__instance,"_fullSynchronisation") ||
                (string)Canonical.Invoke(null,null) != scene) return true;
            var manager = V(__instance,"_entityManager");
            var demote = B(__0,"Demote");
            AccessTools.Method(Manager, demote ? "InitializeSceneClient" : "BecomeSceneHost").Invoke(manager,new[]{V(__0,"SceneHostEpoch")});
            if (!demote)
            {
                var arena = V(__instance,"_arenaCoop");
                AccessTools.Method(arena.GetType(),"OnBecomeSceneHost").Invoke(arena,null);
            }
            return false;
        }
        static void AfterTiming(float __0, ref float ____adaptedCorrectionTime)
        {
            if (!LowLatency || __0 < 0 || __0 > 120) return;
            var cap = __0 <= 60 ? .03f : .06f;
            // This runs for every replica, every frame. Field injection avoids boxing two
            // floats and invoking reflection repeatedly in a scene full of creatures.
            if (____adaptedCorrectionTime > cap) ____adaptedCorrectionTime = cap;
        }
    }
}
