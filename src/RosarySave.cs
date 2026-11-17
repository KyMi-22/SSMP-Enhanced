using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;

namespace Silksong.CoopAssist
{
    internal sealed class RosarySave
    {
        const string Scene = HostWorldSync.LocalLedgerScene;
        readonly SceneData.PersistentItemDataCollection<int, SceneData.SerializableIntData> records;
        string key;
        internal RosaryLedger Ledger { get; }
        internal bool Exists => key != null;
        // True when an older ledger was upgraded and has to be written back once.
        internal bool Migrated { get; private set; }

        internal RosarySave(SceneData scene)
        {
            records = scene.PersistentInts;
            var scenes = ReflectionCache.Get(records, "scenes") as Dictionary<string, Dictionary<string, PersistentItemData<int>>>;
            if (scenes == null) throw new InvalidOperationException("Persistent int records unavailable");
            if (scenes.TryGetValue(Scene, out var rows))
            {
                foreach (var item in rows.Keys)
                {
                    if (!item.StartsWith("v1:", StringComparison.Ordinal)) continue;
                    if (key != null) throw new InvalidOperationException("Multiple rosary ledgers in the same save");
                    key = item;
                }
            }
            Ledger = key == null ? new RosaryLedger() :
                JsonConvert.DeserializeObject<RosaryLedger>(Encoding.UTF8.GetString(Convert.FromBase64String(key.Substring(3))));
            if (Ledger == null || !RosaryMessage.Key(Ledger.Owner) || Ledger.Transfers == null ||
                Ledger.Payments == null || Ledger.Receipts == null) throw new InvalidOperationException("Invalid rosary ledger");
            // Older saves carried only the flat "overdue" flag and some had no deadline at all. Upgrade them once:
            // one period keeps the old +3, and a missing deadline starts a fresh term instead of being due forever.
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            foreach (var row in Ledger.Transfers.Values)
            {
                if (row.Mode != RosaryMode.Loan) continue;
                if (row.Overdue && row.OverduePeriods == 0)
                {
                    row.OverduePeriods = 1;
                    if (row.Due > 0) row.Due = now;
                    Migrated = true;
                }
                if (row.Due <= 0)
                {
                    row.Due = now + RosaryLedger.OverdueSeconds;
                    Migrated = true;
                }
            }
        }

        internal void Write()
        {
            var encoded = "v1:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(Ledger)));
            if (key == encoded) return;
            records.SetValue(new PersistentItemData<int> { SceneName = Scene, ID = encoded, Value = 1 });
            if (key != null) records.Remove(Scene, key);
            key = encoded;
        }
    }
}
