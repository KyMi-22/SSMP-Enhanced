using HarmonyLib;
using HutongGames.PlayMaker;
using SSMP.Api.Client;
using System.Collections.Generic;
using UnityEngine;

namespace Silksong.CoopAssist
{
    internal static class StoryTriggerRepairs
    {
        internal const string PatchId = "silksong.coopassist.storytriggers";
        const float EdgeTolerance = 1.25f;
        const float RestartRadius = 3f;

        internal static void Install()
        {
            var story = typeof(ClientAddon).Assembly.GetType("SSMP.Game.Client.BossRoomCoop", true);
            var hooks = new Harmony(PatchId);
            hooks.Patch(AccessTools.Method(story, "IsInStoryTriggers"),
                postfix: new HarmonyMethod(typeof(StoryTriggerRepairs), nameof(NearRemoteTrigger)));
            hooks.Patch(AccessTools.Method(story, "IsLocalHeroInStoryTrigger"),
                postfix: new HarmonyMethod(typeof(StoryTriggerRepairs), nameof(NearLocalTrigger)));
        }

        static void NearRemoteTrigger(List<Collider2D> __0, Vector2 __1, ref bool __result)
        {
            if (!__result) __result = Near(__0, __1);
        }

        static void NearLocalTrigger(object __0, ref bool __result)
        {
            var hero = HeroController.instance;
            if (__result || hero == null || !hero.gameObject.activeInHierarchy) return;
            var point = (Vector2)hero.transform.position;
            if (CoopAssistPlugin.Value(__0, "Triggers") is List<Collider2D> triggers && Near(triggers, point))
                __result = true;
            else if (CoopAssistPlugin.Value(__0, "Position") is Vector2 origin)
                __result = (point - origin).sqrMagnitude <= RestartRadius * RestartRadius;
        }

        static bool Near(List<Collider2D> triggers, Vector2 point)
        {
            foreach (var trigger in triggers)
                if (trigger != null && trigger.isActiveAndEnabled &&
                    (trigger.ClosestPoint(point) - point).sqrMagnitude <= EdgeTolerance * EdgeTolerance) return true;
            return false;
        }
    }
}
