using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Silksong.CoopAssist
{
    public static class HostProgressSync
    {
        public static JObject Align(JObject host,JObject client,JObject nativeMerged,IDictionary<string,List<string>> fields,ProgressSyncCatalog catalog)
        {
            if (nativeMerged == null) return null;
            var result=HostResourceSync.Align(host,client,nativeMerged,fields);
            var guestData = (JObject)client["playerData"];
            var targetData = (JObject)result["playerData"];
            HostWorldSync.Align(host,client,result,catalog);
            foreach(var field in catalog.PersonalPlayerDataFields)
                HostResourceSync.Copy(guestData,targetData,field);
            AlignSharedProgress((JObject)host["playerData"], guestData, (JObject)nativeMerged["playerData"], targetData, catalog);
            return result;
        }

        public static JObject AlignForJoin(JObject host, JObject client, JObject nativeMerged,
            IDictionary<string, List<string>> fields, ProgressSyncCatalog catalog)
        {
            if (nativeMerged == null) return null;
            var result = (JObject)nativeMerged.DeepClone();
            var hostData = (JObject)host["playerData"];
            var guestData = (JObject)client["playerData"];
            var target = (JObject)result["playerData"];
            AlignSharedProgress(hostData, guestData, (JObject)nativeMerged["playerData"], target, catalog);
            foreach (var name in HostResourceSync.PersonalMoney)
                HostResourceSync.Copy(guestData, target, name);
            foreach (var name in HostResourceSync.Resources)
                if (!HostResourceSync.IsEquipment(name)) HostResourceSync.Copy(guestData, target, name);
            foreach (var name in new[] {"ToolPouchUpgrades", "ToolKitUpgrades", "nailUpgrades", "nailRange"})
                if (hostData[name] is JValue value && (target[name] == null || (double)value > (double)target[name]))
                    target[name] = value.DeepClone();
            foreach (var name in HostResourceSync.EquipmentPurchaseFlags)
                if ((bool?)hostData[name] == true) target[name] = true;
            HostResourceSync.RestorePersonalInventory(guestData, target, catalog);
            HostResourceSync.Copy((JObject)client["sceneData"], (JObject)result["sceneData"], "geoRocks");
            foreach (var name in catalog.PickupFields)
                if (!SharedMapProgress.IsOwnershipField(name) && !HostResourceSync.IsEquipment(name))
                    HostResourceSync.Copy(guestData, target, name);
            foreach (var property in hostData.Properties())
                if (property.Name.StartsWith("Purchased", System.StringComparison.OrdinalIgnoreCase) &&
                    !HostResourceSync.IsEquipment(property.Name) &&
                    (fields == null || !fields.Values.Any(list => list.Contains(property.Name))))
                    HostResourceSync.Copy(guestData, target, property.Name);
            HostWorldSync.RestoreLocalPickups(client, result, catalog);
            return result;
        }

        static void AlignSharedProgress(JObject host, JObject guest, JObject native, JObject target, ProgressSyncCatalog catalog)
        {
            WishProgressSync.Align(host, target, catalog);
            HostMapSync.Align(host, target);
            PersonalCrestLoadout.Restore(host, guest, native, target);
            GrandGateProgress.Align(host, guest, target);
        }
    }
}
