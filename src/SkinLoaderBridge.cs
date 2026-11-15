using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx.Bootstrap;
using HarmonyLib;
using SSMP.Api.Client;

namespace Silksong.CoopAssist
{
    internal static class SkinLoaderBridge
    {
        public static void Install(BepInEx.Logging.ManualLogSource logger)
        {
            var plugin=Chainloader.PluginInfos.Values.Select(p=>p.Instance)
                .FirstOrDefault(p=>p!=null&&p.GetType().FullName=="CustomizerT2DOnly.CustomizerT2DOnly");
            if(plugin==null)return;
            var build=AccessTools.Method(plugin.GetType(),"BuildOverrideMap");
            if(build==null)return;
            new Harmony("silksong.coopassist.skinloader").Patch(build,
                transpiler:new HarmonyMethod(typeof(SkinLoaderBridge),nameof(FilterDirectories)));
            SkinHotReload.Install(plugin as UnityEngine.MonoBehaviour,logger);
            // The older bundled loader has no GetTexturePackDirsToScan. Bring its map into the same active-only
            // selection rule before its delayed texture scans begin, and tell SSMP exactly which pack supplied it.
            build.Invoke(plugin,null);
            if(!SkinHotReload.Available&&ReflectionCache.Get(plugin,"_keys") is ICollection<string> keys && keys.Count==0)
                plugin.enabled=false;
        }
        static IEnumerable<CodeInstruction> FilterDirectories(IEnumerable<CodeInstruction> code)
        {
            var old=AccessTools.Method(typeof(Directory),"GetDirectories",new[]{typeof(string)});
            var replacement=AccessTools.Method(typeof(SkinLoaderBridge),nameof(SelectedDirectories));
            foreach(var instruction in code)
            { if(instruction.Calls(old))instruction.operand=replacement;yield return instruction; }
        }
        public static string[] SelectedDirectories(string root)
        {
            var selected=SkinSelection.ActiveDirectories(root).ToList();
            var bridge=typeof(ClientAddon).Assembly.GetType("SSMP.Game.Client.Skin.CustomizerT2D");
            AccessTools.Method(bridge,"AfterSkinsChosen").Invoke(null,new object[]{selected});
            return selected.ToArray();
        }
    }
}
