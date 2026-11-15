using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Silksong.CoopAssist
{
    // Runs on a freshly received host snapshot before the native save loader. Never adds resources on rejoin.
    public static class HostResourceSync
    {
        // These arrival flags were absent from SSMP's generated world/story tables.
        // Keep the list shared by join alignment and the native live story channel.
        public static readonly string[] AdditionalWorldFlags={"visitedMosstown","visitedBoneBottom","visitedMossCave","pilbyMosstownConvo"};
        public static readonly string[] Resources={
            "ShellShards","TempShellShardStore","health","maxHealth","maxHealthBase","healthBlue","prevHealth",
            "heartPieces","silk","silkMax","silkRegenMax","silkParts","silkSpoolParts","IsSilkSpoolBroken",
            "nailRange","nailUpgrades","ToolPouchUpgrades","ToolKitUpgrades",
            "Tools","ToolLiquids","toolAmountsOverride","MateriumCollected","Relics",
            "EnemyJournalKillData",
            "mosstownAspidBerryCollected","druidMossBerriesSold","mossBerryValueList"
        };
        public static readonly string[] PersonalMoney={"geo","TempGeoStore","dicePilgrimBank","CrawbellCurrency"};
        static bool Rosary(JToken item)=>((string)item?["Name"])?.StartsWith("Rosary_Set_",StringComparison.Ordinal)==true;
        internal static readonly string[] EquipmentPurchaseFlags = {
            "PurchasedPilgrimsRestToolPouch", "PurchasedBelltownToolPouch", "PurchasedForgeToolKit",
            "purchasedGrindleToolKit", "PurchasedArchitectToolKit"
        };
        internal static bool IsEquipment(string field) => field == "Tools" || field == "ToolPouchUpgrades" ||
            field == "ToolKitUpgrades" || field == "nailRange" || field == "nailUpgrades" ||
            Array.IndexOf(EquipmentPurchaseFlags, field) >= 0;
        internal static void Copy(JObject source,JObject target,string name)
        {if(source.TryGetValue(name,out var value))target[name]=value.DeepClone();else target.Remove(name);}
        internal static void RestorePersonalInventory(JObject mine, JObject target, ProgressSyncCatalog catalog)
        {
            bool Shared(JObject item) => !Rosary(item) && catalog.IsStoryItem((string)item["Name"]);
            var baseline = target["Collectables"] as JObject ?? new JObject();
            var shared = (baseline["savedData"] as JArray ?? new JArray()).OfType<JObject>().Where(Shared);
            var personal = (mine["Collectables"]?["savedData"] as JArray ?? new JArray()).OfType<JObject>().Where(item => !Shared(item));
            baseline["savedData"] = new JArray(shared.Concat(personal).Select(row => row.DeepClone()));
            target["Collectables"] = baseline;
        }
        public static JObject Align(JObject host,JObject client,JObject nativeMerged,IDictionary<string,List<string>> progress)
        {
            if(nativeMerged==null)return null; // Preserve native incompatible-mode/refusal checks.
            if(!(host?["playerData"] is JObject theirs) || !(client?["playerData"] is JObject mine))
                throw new ArgumentException("Host and client snapshots need playerData");
            var result=(JObject)nativeMerged.DeepClone();
            if(!(result["playerData"] is JObject target))throw new ArgumentException("Aligned save needs playerData");
            // Player metadata starts from the native safety merge. HostProgressSync also
            // aligns the quest counters and persistent sources before this result can load.
            if(progress!=null)foreach(var list in progress.Values)foreach(var name in list)Copy(theirs,target,name);
            foreach(var name in AdditionalWorldFlags)Copy(theirs,target,name);
            foreach(var name in Resources)Copy(theirs,target,name);
            foreach(var property in theirs.Properties())
                if(property.Name.StartsWith("Purchased",StringComparison.OrdinalIgnoreCase))Copy(theirs,target,property.Name);
            if(theirs["Collectables"]!=null && theirs["Collectables"].Type!=JTokenType.Null && !(theirs["Collectables"] is JObject))
                throw new ArgumentException("Invalid host collectable inventory");
            {
                var inventory=theirs["Collectables"] as JObject ?? new JObject{["savedData"]=new JArray()};
                var copy=(JObject)inventory.DeepClone();
                if(!(copy["savedData"] is JArray entries))throw new ArgumentException("Invalid host collectable inventory");
                foreach(var entry in entries.Where(Rosary).ToArray())entry.Remove();
                if(mine["Collectables"]?["savedData"] is JArray personal)
                    foreach(var entry in personal.Where(Rosary))entries.Add(entry.DeepClone());
                target["Collectables"]=copy;
            }
            // Restore exact personal balances even when the native merge awarded currency for completed wishes.
            foreach(var name in PersonalMoney)Copy(mine,target,name);
            // A host may send a snapshot while down: joining must still create a living, controllable character.
            if(target["maxHealth"]!=null && target["health"]!=null)
            {
                var maximum=Math.Max(1,(int)target["maxHealth"]);
                target["health"]=Math.Max(1,Math.Min(maximum,(int)target["health"]));
                target["prevHealth"]=target["health"].DeepClone();
            }
            if(target["silkMax"]!=null && target["silk"]!=null)
                target["silk"]=Math.Max(0,Math.Min((int)target["silkMax"],(int)target["silk"]));
            return result;
        }
    }
}
