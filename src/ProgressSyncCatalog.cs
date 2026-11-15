using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Silksong.CoopAssist
{
    // Generated from the shipped quest targets and scene components, not guessed object
    // names. The small exception sets protect rosary sources and independent platforms.
    public sealed class ProgressSyncCatalog
    {
        public readonly string[] PlayerDataFields;
        public readonly string[] PersonalPlayerDataFields;
        public readonly string[] PickupFields;
        readonly HashSet<(string Scene, string Id)> pickups;
        readonly HashSet<string> sharedCollectables;
        readonly Dictionary<string,HashSet<(string Scene,string Id)>> personal;
        public ProgressSyncCatalog(IEnumerable<string> fields,IDictionary<string,IEnumerable<(string Scene,string Id)>> records,
            IEnumerable<string> personalFields=null, IEnumerable<string> pickupFields=null,
            IEnumerable<(string Scene,string Id)> pickupRecords=null, IEnumerable<string> sharedItems=null)
        {
            PlayerDataFields=fields.Distinct(StringComparer.Ordinal).OrderBy(x=>x,StringComparer.Ordinal).ToArray();
            PersonalPlayerDataFields=(personalFields??Array.Empty<string>()).Distinct(StringComparer.Ordinal).ToArray();
            personal=records.ToDictionary(x=>x.Key,x=>new HashSet<(string,string)>(x.Value.Select(k=>(k.Scene.ToLowerInvariant(),k.Id))));
            PickupFields = (pickupFields ?? Array.Empty<string>()).Where(name => !string.IsNullOrEmpty(name)).Distinct().ToArray();
            pickups = new HashSet<(string,string)>(pickupRecords ?? Array.Empty<(string,string)>());
            sharedCollectables = new HashSet<string>(sharedItems ?? Array.Empty<string>(), StringComparer.Ordinal);
        }
        public bool IsPersonal(string list,string scene,string id)=>personal.TryGetValue(list,out var keys) && keys.Contains((scene.ToLowerInvariant(),id));
        internal bool IsPickup(string scene, string id) => pickups.Contains((scene.ToLowerInvariant(), id));
        internal bool IsStoryItem(string name) => sharedCollectables.Contains(name);
        public static ProgressSyncCatalog Parse(string json)
        {
            var root=JObject.Parse(json);
            if((bool?)root["Complete"]!=true || (string)root["GameVersion"]!="1.0.30000")
                throw new InvalidDataException("Progress catalog is incomplete or targets a different game version");
            var records=new Dictionary<string,IEnumerable<(string,string)>>();
            foreach(var list in new[]{"persistentBools","persistentInts"})
            {
                if(!(root["PersonalRecords"]?[list] is JArray rows))throw new InvalidDataException("Missing personal source catalog: "+list);
                records[list]=rows.Select(row=>((string)row[0],(string)row[1])).ToArray();
            }
            var storyItems = new HashSet<string>(ReadStoryItems(), StringComparer.Ordinal) { "Simple Key", "Ward Key" };
            var pickups = root["Pickups"]?.OfType<JObject>().Where(row => !storyItems.Contains((string)row["Item"])).ToArray()
                ?? Array.Empty<JObject>();
            return new ProgressSyncCatalog(root["PlayerDataFields"].Values<string>(),records,
                root["PersonalPlayerDataFields"]?.Values<string>(),
                pickups.Select(row => (string)row["PlayerDataFlag"]),
                pickups.Select(row => row["Record"]).OfType<JArray>().Select(row => ((string)row[0], (string)row[1])),
                storyItems);
        }

        static IEnumerable<string> ReadStoryItems()
        {
            // Build packages embed the shipped SSMP's item classifications alongside the asset catalog.
            using var stream = typeof(ProgressSyncCatalog).Assembly.GetManifestResourceStream("Silksong.CoopAssist.NativeStoryItems.json");
            if (stream == null) return Array.Empty<string>();
            using var reader = new StreamReader(stream);
            var table = (JArray)JObject.Parse(reader.ReadToEnd())["items"];
            return table.OfType<JObject>()
                .Where(row => (bool?)row["shareRemoval"] == true || (string)row["kind"] is "key" or "boolKey" or "plot")
                .Select(row => (string)row["name"]).ToArray();
        }
        static readonly Lazy<ProgressSyncCatalog> bundled=new Lazy<ProgressSyncCatalog>(()=>{
            using(var stream=typeof(ProgressSyncCatalog).Assembly.GetManifestResourceStream("Silksong.CoopAssist.ProgressSyncCatalog.json"))
            {
                if(stream==null)throw new InvalidDataException("Bundled progress/source catalog missing");
                using(var reader=new StreamReader(stream))return Parse(reader.ReadToEnd());
            }
        });
        public static ProgressSyncCatalog Bundled=>bundled.Value;
    }
}
