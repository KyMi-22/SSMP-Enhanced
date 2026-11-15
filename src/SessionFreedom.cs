using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using SSMP.Api.Client;

namespace Silksong.CoopAssist
{
    internal static class SessionFreedom
    {
        static readonly Type Save=typeof(ClientAddon).Assembly.GetType("SSMP.Game.Client.Save.CoopSave",true);
        static readonly MethodInfo Release=AccessTools.Method(Save,"ReleaseHold");
        static readonly MethodInfo Find=AccessTools.Method(Save,"FindMembers");
        static readonly MethodInfo Unchecked=AccessTools.Method(Save,"HasUncheckedMember");
        internal static void Install()
        {
            var h=new Harmony("silksong.coopassist.sessionfreedom");
            h.Patch(AccessTools.Method(Save,"Hold"),prefix:new HarmonyMethod(typeof(SessionFreedom),nameof(NoHold)));
            h.Patch(AccessTools.Method(Save,"UpdateHoldPrompt"),prefix:new HarmonyMethod(typeof(SessionFreedom),nameof(ReleaseBeforePrompt)));
            h.Patch(AccessTools.Method(Save,"TryOpen"),prefix:new HarmonyMethod(typeof(SessionFreedom),nameof(OpenWithoutRoster)));
            h.Patch(AccessTools.Method(Save,"IsGroupComplete"),prefix:new HarmonyMethod(typeof(SessionFreedom),nameof(OnlineGroup)));
            h.Patch(AccessTools.Method(Save,"HasAllMembers"),prefix:new HarmonyMethod(typeof(SessionFreedom),nameof(OnlineMembers)));
        }
        // Use the native owner-aware release: never blindly RegainControl over a death, bench or transition.
        static bool NoHold(object __instance,HeroController __0)
        { Release.Invoke(__instance,new object[]{__0}); return false; }
        static void ReleaseBeforePrompt(object __instance)
        { Release.Invoke(__instance,new object[]{HeroController.SilentInstance}); }
        // Join alignment is handled before this HOST/solo gate and stays intact; keep the co-op marker.
        static bool OpenWithoutRoster(ref bool __result) { __result=true; return false; }
        // Departed members remain in the save's roster, but no longer veto the active group's play.
        // Online players still need the native world-state check; do not mark unchecked members as checked.
        static bool OnlineGroup(object __instance,object __1,ref bool __result)
        { __result=!(bool)Unchecked.Invoke(__instance,new[]{__1}); return false; }
        static bool OnlineMembers(object __instance,object __0,ref bool __result)
        { __result=!(bool)Unchecked.Invoke(__instance,new[]{Find.Invoke(__instance,new[]{__0})}); return false; }
    }
}
