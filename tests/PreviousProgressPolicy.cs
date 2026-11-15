using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace BeforeSourceAudit
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
            "nailRange","nailUpgrades","ToolPouchUpgrades","ToolKitUpgrades","CurrentCrestID","PreviousCrestID",
            "ToolEquips","ExtraToolEquips","Tools","ToolLiquids","toolAmountsOverride","IsCurrentCrestTemp","MateriumCollected","Relics",
            "QuestCompletionData","QuestRumourData","EnemyJournalKillData",
            "mosstownAspidBerryCollected","druidMossBerriesSold","mossBerryValueList"
        };
        public static readonly string[] PersonalMoney={"geo","TempGeoStore","dicePilgrimBank","CrawbellCurrency"};
        static bool Rosary(JToken item)=>((string)item?["Name"])?.StartsWith("Rosary_Set_",StringComparison.Ordinal)==true;
        static void Copy(JObject source,JObject target,string name)
        {if(source.TryGetValue(name,out var value))target[name]=value.DeepClone();}
        public static JObject Align(JObject host,JObject client,JObject nativeMerged,IDictionary<string,List<string>> progress)
        {
            if(nativeMerged==null)return null; // Preserve native incompatible-mode/refusal checks.
            if(!(host?["playerData"] is JObject theirs) || !(client?["playerData"] is JObject mine))
                throw new ArgumentException("Host and client snapshots need playerData");
            var result=(JObject)nativeMerged.DeepClone();
            if(!(result["playerData"] is JObject target))throw new ArgumentException("Aligned save needs playerData");
            // Native world-object classifications remain intact: rosary pickups and personal platforms stay personal.
            if(progress!=null)foreach(var list in progress.Values)foreach(var name in list)Copy(theirs,target,name);
            foreach(var name in AdditionalWorldFlags)Copy(theirs,target,name);
            foreach(var name in Resources)Copy(theirs,target,name);
            foreach(var property in theirs.Properties())
                if(property.Name.StartsWith("Purchased",StringComparison.OrdinalIgnoreCase))Copy(theirs,target,property.Name);
            if(theirs["Collectables"] is JObject inventory)
            {
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


namespace BeforeSourceAudit
{
    // Resource quantities follow the host, so their classified source records must follow
    // the SAME snapshot, rather than the native union of pickups from two different saves.
    public static class HostWorldSync
    {
        public static void Align(JObject host,JObject merged,string listName,IDictionary<string,Dictionary<string,string>> rules)
        {
            if(rules==null || !(host?["sceneData"] is JObject source) || !(merged?["sceneData"] is JObject target))return;
            var hostList=source[listName]?["serializedList"] as JArray ?? new JArray();
            if(!(target[listName] is JObject collection))target[listName]=collection=new JObject();
            if(!(collection["serializedList"] is JArray result))collection["serializedList"]=result=new JArray();
            bool Shared(JToken item)
            {
                var scene=((string)item["SceneName"]??"").ToLowerInvariant();
                var id=(string)item["ID"]??"";
                return rules.TryGetValue(scene,out var entries) && entries.ContainsKey(id);
            }
            foreach(var item in result.Where(Shared).ToArray())item.Remove();
            foreach(var item in hostList.Where(Shared))result.Add(item.DeepClone());
            // Unclassified personal sources, currency and temporary platforms retain the
            // native merge. No guessed name patterns or whole-scene replacement.
        }
    }
}

