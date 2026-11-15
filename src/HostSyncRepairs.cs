using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using SSMP.Api.Client;

namespace Silksong.CoopAssist
{
    internal static class HostSyncRepairs
    {
        [ThreadStatic] internal static bool IncludeResources;
        static readonly System.Reflection.Assembly Native=typeof(ClientAddon).Assembly;
        static readonly Type Save=Native.GetType("SSMP.Game.Client.Save.CoopSave",true);
        static FieldInfo[] augmentedStoryFields;
        internal static void Install()
        {
            var h=new Harmony("silksong.coopassist.hostresources");
            h.Patch(AccessTools.Method(Native.GetType("SSMP.Game.Client.Save.CoopHostSync",true),"Merge"),
                postfix:new HarmonyMethod(typeof(HostSyncRepairs),nameof(AfterMerge)));
            h.Patch(AccessTools.Method(Save,"GetJoinWarning"),postfix:new HarmonyMethod(typeof(HostSyncRepairs),nameof(AfterWarning)));
            h.Patch(AccessTools.Method(Save,"GetJoinSummary"),postfix:new HarmonyMethod(typeof(HostSyncRepairs),nameof(AfterSummary)));
            h.Patch(AccessTools.Method(Save,"OnJoinSlotSubmit"),prefix:new HarmonyMethod(typeof(HostSyncRepairs),nameof(RequireFreshHost)));
            h.Patch(AccessTools.Method(Save,"GetStoryFields"),postfix:new HarmonyMethod(typeof(HostSyncRepairs),nameof(CompleteStoryFields)));
            h.Patch(AccessTools.Method(Save,"ApplyInteractionFlags"),
                postfix:new HarmonyMethod(typeof(HostSyncRepairs),nameof(AfterInteractionFlags)));
            h.Patch(AccessTools.Method(Save,"OnInteractionSwitchState"),
                prefix:new HarmonyMethod(typeof(CoopAssistPlugin),nameof(CoopAssistPlugin.RedirectCompletedGate)));
            h.Patch(AccessTools.Method(Save,"OnSetLoadedGameData"),postfix:new HarmonyMethod(typeof(HostSyncRepairs),nameof(AfterLoad)));
        }
        static void AfterLoad(object __instance,SaveGameData __2)
        {
            CoopAssistPlugin.Instance?.AfterSaveLoaded(__instance,__2);
        }
        static void CompleteStoryFields(ref FieldInfo[] __result)
        {
            if(__result==null || ReferenceEquals(__result,augmentedStoryFields))return;
            var fields=__result.ToList();
            var indices=(IDictionary<string,int>)AccessTools.Field(Save,"StoryFieldIndices").GetValue(null);
            foreach(var name in HostResourceSync.AdditionalWorldFlags.Concat(GrandGateProgress.StoryRecords))
                if(!fields.Any(f=>f.Name==name))
                {
                    var field=AccessTools.Field(typeof(PlayerData),name);
                    if(field==null || field.FieldType!=typeof(bool))throw new MissingFieldException(typeof(PlayerData).FullName,name);
                    indices[name]=fields.Count;fields.Add(field);
                }
            var records=(ISet<string>)AccessTools.Field(Save,"StoryRecordNames").GetValue(null);
            records.UnionWith(GrandGateProgress.StoryRecords);
            __result=fields.ToArray();AccessTools.Field(Save,"_storyFields").SetValue(null,__result);
            augmentedStoryFields=__result;
        }
        static void AfterInteractionFlags(object __0,int __result)
        {
            if(__result>0)CoopAssistPlugin.Instance?.ObserveGrandGateRecords(__0);
        }
        static void AfterMerge(JObject __0,JObject __1,object __2,ref JObject __result)
        {
            if(__result==null)return;
            var fields=ReflectionCache.Get(__2,"Fields") as IDictionary<string,List<string>>;
            __result = IncludeResources
                ? HostProgressSync.Align(__0,__1,__result,fields,ProgressSyncCatalog.Bundled)
                : HostProgressSync.AlignForJoin(__0,__1,__result,fields,ProgressSyncCatalog.Bundled);
        }
        // Each connection requests a fresh host snapshot. A missing host does not authorize an unsynchronised load.
        static void RequireFreshHost(object __instance)=>ReflectionCache.Set(__instance,"_noHostSlot",-1);
        static void AfterWarning(ref string __result)
        {
            __result=__result?.Replace("钱、材料、买的东西和装备搭配都还是你的。进去之前会先备份。",
                "剧情、祈愿、地图和装备进度会同步。个人库存和念珠保留。进入前会先备份，资源可在长椅按 F9 同步。")
                .Replace("Your money, materials, purchases and loadout stay yours. The save is backed up first.",
                "Story, wishes, maps and equipment progression sync. Personal inventory and rosaries stay yours. Your save is backed up first.");
        }
        static void AfterSummary(ref string __result)
        {__result="已同步剧情、祈愿、地图和装备进度。个人资源可在长椅按 F9 手动同步。";}
    }
}
