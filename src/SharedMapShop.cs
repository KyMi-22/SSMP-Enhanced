using System;
using HarmonyLib;

namespace Silksong.CoopAssist
{
    internal static class SharedMapShop
    {
        internal static void Install()
        {
            var hooks=new Harmony("silksong.coopassist.mapshop");
            hooks.Patch(AccessTools.Method(typeof(ShopItemStats),"CanBuy"),
                prefix:new HarmonyMethod(typeof(SharedMapShop),nameof(CanBuy)));
            hooks.Patch(AccessTools.Method(typeof(ShopItem),"SetPurchased"),
                prefix:new HarmonyMethod(typeof(SharedMapShop),nameof(BeforePurchase)),
                postfix:new HarmonyMethod(typeof(SharedMapShop),nameof(AfterPurchase)));
        }
        static bool Shared(ShopItem item)=>CoopAssistPlugin.Instance?.MapSharingActive==true &&
            item!=null && (item.GetTypeFlags() & ShopItem.TypeFlags.Map) != 0;
        static bool CanBuy(ShopItemStats __instance,ref bool __result)
        {
            if(!Shared(__instance.Item) || !__instance.Item.IsPurchased)return true;
            __result=false;return false;
        }
        static bool BeforePurchase(ShopItem __instance,Action __0)
        {
            // An already-open shop can still point at a map received over the network.
            // Refuse that stale purchase before the native method charges any currency.
            if(!Shared(__instance) || !__instance.IsPurchased)return true;
            CoopAssistPlugin.Instance.MapAlreadyOwned();__0?.Invoke();return false;
        }
        static void AfterPurchase(ShopItem __instance)
        {if(Shared(__instance))CoopAssistPlugin.Instance.MapPurchaseChanged();}
    }
    public sealed partial class CoopAssistPlugin
    {
        internal bool MapSharingActive=>compatible && api?.NetClient.IsConnected==true && coop!=null;
        internal void MapAlreadyOwned()=>Say("队友已共享这张地图，无需再次购买；重新打开商店后该地图会从货架移除。");
        internal void MapPurchaseChanged()
        {
            nextMapTick = nextMapOffer = 0;
            ScheduleCheckpoint();
        }
    }
}
