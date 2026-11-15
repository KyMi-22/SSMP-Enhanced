using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Silksong.CoopAssist
{
    internal static class PersonalCrestLoadout
    {
        internal static void Restore(JObject host, JObject player, JObject native, JObject target)
        {
            // The native merge starts with the player's selection and applies story
            // constraints and crest upgrades, e.g. Hunter -> Hunter_v2.
            foreach (var key in new[] { "CurrentCrestID", "PreviousCrestID", "IsCurrentCrestTemp" })
            {
                if (native.TryGetValue(key, out var value)) target[key] = value.DeepClone();
                else target.Remove(key);
            }

            var equipped = new HashSet<string>(System.StringComparer.Ordinal);
            foreach (var key in new[] { "ToolEquips", "ExtraToolEquips" })
            {
                RestoreSlots(host, player, native, target, key);
                if (!(target[key] is JObject table)) continue;
                foreach (var slot in ToolAssignments(table))
                    if ((string)slot.Value is string name && name.Length > 0) equipped.Add(name);
            }

            // An independently equipped tool must still exist after the host inventory
            // is applied. Keep the player's ownership only where a loadout needs it.
            foreach (var row in Entries(player, "Tools"))
            {
                var name = (string)row["Name"];
                if (name == null || !equipped.Contains(name) || (bool?)row["Data"]?["IsUnlocked"] != true) continue;
                if (!(target["Tools"] is JObject tools)) target["Tools"] = tools = new JObject();
                if (!(tools["savedData"] is JArray rows)) tools["savedData"] = rows = new JArray();
                var copy = rows.OfType<JObject>().FirstOrDefault(item => (string)item["Name"] == name);
                if (copy == null) rows.Add(row.DeepClone());
                else if ((bool?)copy["Data"]?["IsUnlocked"] != true) copy.Replace(row.DeepClone());
            }
        }

        static void RestoreSlots(JObject host, JObject player, JObject native, JObject target, string key)
        {
            var baseline = native[key] as JObject ?? player[key] as JObject ?? host[key] as JObject;
            if (baseline == null)
            {
                target.Remove(key);
                return;
            }
            var table = (JObject)baseline.DeepClone();
            var rows = new JArray();
            var names = new HashSet<string>(System.StringComparer.Ordinal);
            var unlocked = new HashSet<string>(Entries(native, key).Concat(Entries(host, key))
                .Where(row => (bool?)row["Data"]?["IsUnlocked"] == true)
                .Select(row => (string)row["Name"]), System.StringComparer.Ordinal);
            // A local row wins in full, including deliberately empty sockets.
            foreach (var row in Entries(player, key))
            {
                var name = (string)row["Name"];
                if (!names.Add(name)) continue;
                var copy = (JObject)row.DeepClone();
                if (key == "ToolEquips" && unlocked.Contains(name) && copy["Data"] is JObject crest)
                    crest["IsUnlocked"] = true;
                rows.Add(copy);
            }
            foreach (var row in Entries(native, key))
            {
                var name = (string)row["Name"];
                if (!names.Add(name)) continue;
                var copy = (JObject)row.DeepClone();
                // Native curse recovery can use the host's slots as a template.
                if (name == "Cursed") ClearAssignments(copy);
                rows.Add(copy);
            }
            foreach (var row in Entries(host, key))
            {
                if (!names.Add((string)row["Name"])) continue;
                var copy = (JObject)row.DeepClone();
                ClearAssignments(copy);
                rows.Add(copy);
            }
            table["savedData"] = rows;
            target[key] = table;
        }

        static IEnumerable<JObject> Entries(JObject data, string key) =>
            ((data[key] as JObject)?["savedData"] as JArray ?? new JArray())
                .OfType<JObject>().Where(row => row["Name"]?.Type == JTokenType.String);

        static IEnumerable<JProperty> ToolAssignments(JObject data) =>
            data.Descendants().OfType<JProperty>().Where(p => p.Name == "EquippedTool");

        static void ClearAssignments(JObject row)
        {
            foreach (var assignment in ToolAssignments(row).ToArray()) assignment.Value = "";
        }
    }
}
