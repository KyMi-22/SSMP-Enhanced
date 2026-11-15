using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Silksong.CoopAssist
{
    public static class WishProgressSync
    {
        // Mode is local metadata, never a quest reward. Currency and rosary strings are
        // preserved by HostResourceSync, including quests that test a personal balance.
        static readonly HashSet<string> Personal=new HashSet<string>(HostResourceSync.PersonalMoney,StringComparer.Ordinal){"permadeathMode","Collectables"};
        static readonly HashSet<string> Handled=new HashSet<string>(HostResourceSync.Resources,StringComparer.Ordinal){"QuestCompletionData","QuestRumourData"};
        public static void Align(JObject host,JObject target,ProgressSyncCatalog catalog)
        {
            foreach(var field in catalog.PlayerDataFields)
            {
                if(Personal.Contains(field) || Handled.Contains(field))continue;
                if(host.TryGetValue(field,out var value))target[field]=value.DeepClone();
                else target.Remove(field); // Absence is the host's default, not a stale client counter.
            }
            foreach(var field in new[]{"QuestCompletionData","QuestRumourData"})
            {
                if(host[field] is JObject table)target[field]=table.DeepClone();
                else if(host[field]!=null && host[field].Type!=JTokenType.Null)throw new ArgumentException("Invalid host quest table: "+field);
                else target[field]=new JObject{["savedData"]=new JArray()};
            }
        }
    }
}
