using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
namespace Silksong.CoopAssist
{
    internal static class Comfort
    {
        public static ConfigEntry<bool> Magnet;
        static ConfigEntry<float> radius;
        public static void Install(ConfigFile config)
        {
            Magnet=config.Bind("Comfort","PersonalCurrencyMagnet",true,"Attract only local loose rosaries and shards. No wallet or quest sharing.");
            radius=config.Bind("Comfort","MagnetRadius",7f,"Maximum attraction radius in game units.");
            new Harmony("silksong.coopassist.comfort").Patch(AccessTools.Method(typeof(CurrencyObjectBase),"OnFixedUpdate"),prefix:new HarmonyMethod(typeof(Comfort),nameof(BeforeCurrency)));
        }
        static void BeforeCurrency(CurrencyObjectBase __instance, bool ___activated, bool ___isDisabling,
            ref bool ___isAttracted, ref Transform ___hero, Rigidbody2D ___body)
        {
            if(!Magnet.Value||!(__instance is GeoControl||__instance is ShellShard)||HeroController.instance==null||!__instance.gameObject.activeInHierarchy)return;
            var hero=HeroController.instance;
            if(PlayerData.instance==null||PlayerData.instance.health<=0||___activated||___isDisabling)return;
            var range=Mathf.Clamp(radius.Value,1,12);if((hero.transform.position-__instance.transform.position).sqrMagnitude>range*range)return;
            if(___body==null)return;
            ___hero=hero.transform;___isAttracted=true;___body.gravityScale=0;
        }
    }
}
