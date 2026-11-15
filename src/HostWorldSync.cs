using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Silksong.CoopAssist
{
    // Follow all persisted host sources, including materials absent from SSMP's old table.
    // Exceptions are taken from the client's original snapshot, never the native union.
    public static class HostWorldSync
    {
        internal const string LocalLedgerScene = "CoopAssist_RosaryLedger";

        internal static void RestoreLocalPickups(JObject client, JObject merged, ProgressSyncCatalog catalog)
        {
            var mine = (JObject)client["sceneData"];
            var target = (JObject)merged["sceneData"];
            foreach (var listName in new[] {"persistentBools", "persistentInts"})
            {
                bool Local(JObject row)
                {
                    var scene = (string)row["SceneName"] ?? "";
                    var id = (string)row["ID"] ?? "";
                    return scene == LocalLedgerScene || catalog.IsPersonal(listName, scene, id) || catalog.IsPickup(scene, id);
                }

                if (!(target[listName] is JObject list)) target[listName] = list = new JObject();
                var current = (list["serializedList"] as JArray ?? new JArray()).OfType<JObject>().Where(row => !Local(row));
                var local = (mine[listName]?["serializedList"] as JArray ?? new JArray()).OfType<JObject>().Where(Local);
                list["serializedList"] = new JArray(current.Concat(local).Select(row => row.DeepClone()));
            }
        }

        public static void Align(JObject host,JObject client,JObject merged,ProgressSyncCatalog catalog)
        {
            if(!(host?["sceneData"] is JObject source) || !(client?["sceneData"] is JObject mine) || !(merged?["sceneData"] is JObject target))
                throw new ArgumentException("Progress snapshots need sceneData");
            foreach(var listName in new[]{"persistentBools","persistentInts"})
            {
                var records=new Dictionary<(string Scene,string Id),JObject>();
                void Read(JObject save,bool personal)
                {
                    var token=save[listName]?["serializedList"];
                    if(token==null || token.Type==JTokenType.Null)return;
                    if(!(token is JArray list))throw new ArgumentException("Invalid persistent source list: "+listName);
                    foreach(var tokenRow in list)
                    {
                        if(!(tokenRow is JObject row))throw new ArgumentException("Invalid persistent source record");
                        var scene=(string)row["SceneName"]??"";var id=(string)row["ID"]??"";
                        if(scene.Length==0 || id.Length==0)throw new ArgumentException("Invalid saved source identity");
                        if((scene == LocalLedgerScene || catalog.IsPersonal(listName,scene,id))==personal)
                            records[(scene.ToLowerInvariant(),id)]=(JObject)row.DeepClone();
                    }
                }
                Read(source,false);Read(mine,true);
                var collection=source[listName] is JObject saved?(JObject)saved.DeepClone():new JObject();
                collection["serializedList"]=new JArray(records.OrderBy(x=>x.Key.Scene,StringComparer.Ordinal).ThenBy(x=>x.Key.Id,StringComparer.Ordinal).Select(x=>x.Value));
                target[listName]=collection;
            }
            // GeoRock's own saved collection is personal rosary rocks. Do not accidentally
            // import a host's rock just because the native baseline contains it.
            if(mine.TryGetValue("geoRocks",out var rocks))target["geoRocks"]=rocks.DeepClone();
            else target.Remove("geoRocks");
        }
    }
}
