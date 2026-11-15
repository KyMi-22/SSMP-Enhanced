using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Silksong.CoopAssist
{
    public sealed partial class CoopAssistPlugin
    {
        readonly ConcurrentQueue<EquipmentMessage> equipmentInbox = new ConcurrentQueue<EquipmentMessage>();
        readonly HashSet<(byte Kind, string Name, int Value)> sentEquipment = new HashSet<(byte, string, int)>();
        Dictionary<string, ToolItem> equipmentTools;
        Dictionary<string, ToolCrest> equipmentCrests;
        Dictionary<ToolItem, ToolItem[]> equipmentReplacements;
        PlayerData equipmentSave;
        static readonly Dictionary<string, FieldInfo> EquipmentFlags = HostResourceSync.EquipmentPurchaseFlags
            .ToDictionary(name => name, name => AccessTools.Field(typeof(PlayerData), name), StringComparer.Ordinal);
        float nextEquipmentTick, nextEquipmentFull;

        internal void QueueEquipment(EquipmentMessage message)
        {
            if (message.Valid && equipmentInbox.Count < 512) equipmentInbox.Enqueue(message);
        }

        void ClearEquipment()
        {
            equipmentSave = null;
            sentEquipment.Clear();
            while (equipmentInbox.TryDequeue(out _)) { }
        }

        void TickEquipment()
        {
            if (!RosaryConnected || Loading || Down || snapshotToken.Length != 0 || applyingSnapshot ||
                PlayerData.instance == null || Time.unscaledTime < nextEquipmentTick) return;
            nextEquipmentTick = Time.unscaledTime + .35f;
            var player = PlayerData.instance;
            if (equipmentSave != player)
            {
                equipmentSave = player;
                sentEquipment.Clear();
                nextEquipmentFull = 0;
            }
            if (Time.unscaledTime >= nextEquipmentFull)
            {
                nextEquipmentFull = Time.unscaledTime + 8;
                sentEquipment.Clear();
            }
            if (equipmentTools == null)
            {
                equipmentTools = ToolItemManager.GetAllTools().Where(tool => tool != null)
                    .GroupBy(tool => tool.name).ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
                equipmentReplacements = equipmentTools.Values.Where(tool => Value(tool, "getReplaces") is ToolItem prior && prior != null)
                    .GroupBy(tool => (ToolItem)Value(tool, "getReplaces"))
                    .ToDictionary(group => group.Key, group => group.ToArray());
            }
            equipmentCrests ??= ToolItemManager.GetAllCrests().Where(crest => crest != null && crest.name != "Cursed")
                .GroupBy(crest => crest.name).ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            if (!NativeSaveBridge.IsWriting())
            {
                bool changed = false;
                while (equipmentInbox.TryDequeue(out var message))
                    if (Checked(message.Peer) && api.ClientManager.GetPlayer(message.Peer) != null)
                        changed |= ApplyEquipment(player, message);
                if (changed) ScheduleCheckpoint();
            }
            if (!api.ClientManager.Players.Any(peer => Checked(peer.Id))) return;
            foreach (var tool in equipmentTools.Values)
                if (tool.IsUnlocked && tool.IsUnique && !SupersededTool(tool)) PublishEquipment(0, tool.name, 1);
            foreach (var crest in equipmentCrests.Values)
                if (crest.IsUnlocked) PublishEquipment(1, crest.name, 1);
            PublishEquipment(2, "", player.ToolPouchUpgrades);
            PublishEquipment(3, "", player.ToolKitUpgrades);
            PublishEquipment(4, "", player.nailUpgrades);
            foreach (var flag in EquipmentFlags)
                if (flag.Value.GetValue(player) is true) PublishEquipment(5, flag.Key, 1);
            PublishEquipment(6, "", player.nailRange);
        }

        bool SupersededTool(ToolItem tool) => equipmentReplacements.TryGetValue(tool, out var replacements) &&
            replacements.Any(replacement => replacement.IsUnlocked);

        void PublishEquipment(byte kind, string name, int value)
        {
            var key = (kind, name, value);
            if (value <= 0 || sentEquipment.Contains(key)) return;
            var message = new EquipmentMessage { Kind = kind, Name = name, Value = value };
            if (!message.Valid) return;
            appearanceSender.SendCollectionData(AssistPacket.Equipment, message);
            sentEquipment.Add(key);
        }

        bool ApplyEquipment(PlayerData player, EquipmentMessage message)
        {
            switch (message.Kind)
            {
                case 0:
                    if (!equipmentTools.TryGetValue(message.Name, out var tool) || tool.IsUnlocked ||
                        !tool.IsUnique || SupersededTool(tool)) return false;
                    tool.Unlock(null, ToolItem.PopupFlags.None);
                    return true;
                case 1:
                    if (!equipmentCrests.TryGetValue(message.Name, out var crest) || crest.IsUnlocked) return false;
                    crest.Unlock();
                    return true;
                case 2:
                    if (message.Value <= player.ToolPouchUpgrades) return false;
                    player.ToolPouchUpgrades = message.Value;
                    return true;
                case 3:
                    if (message.Value <= player.ToolKitUpgrades) return false;
                    player.ToolKitUpgrades = message.Value;
                    return true;
                case 4:
                    if (message.Value <= player.nailUpgrades) return false;
                    player.nailUpgrades = message.Value;
                    return true;
                case 5:
                    if (!EquipmentFlags.TryGetValue(message.Name, out var flag) || flag.GetValue(player) is true) return false;
                    flag.SetValue(player, true);
                    return true;
                case 6:
                    if (message.Value <= player.nailRange) return false;
                    player.nailRange = message.Value;
                    return true;
                default: return false;
            }
        }
    }
}
