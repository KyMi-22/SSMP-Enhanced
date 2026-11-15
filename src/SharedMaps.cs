using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace Silksong.CoopAssist
{
    public sealed partial class CoopAssistPlugin
    {
        const double MapScanInterval = .25;
        const double MapSendInterval = .034;
        const double MapRetryInterval = 3;
        const double MapFullRefreshInterval = 12;
        const int MapInboxLimit = 256;
        const int MapOutboxLimit = 512;

        sealed class MapSubscriber
        {
            internal string Token;
            internal SharedMapProgress Queued;
            internal double LastFull;
        }

        readonly ConcurrentQueue<(ushort Peer, SharedMapMessage Data)> mapOffers =
            new ConcurrentQueue<(ushort, SharedMapMessage)>();
        readonly ConcurrentQueue<SharedMapMessage> mapInbox = new ConcurrentQueue<SharedMapMessage>();
        readonly Queue<(ushort Peer, SharedMapMessage Data)> mapOutbox = new Queue<(ushort, SharedMapMessage)>();
        readonly Dictionary<ushort, MapSubscriber> mapSubscribers = new Dictionary<ushort, MapSubscriber>();
        SharedMapProgress sharedMaps = new SharedMapProgress();
        SharedMapProgress mapsConfirmedByHost = new SharedMapProgress();
        SharedMapProgress observedMaps;
        PlayerData mapPlayer;
        string mapSave, mapToken = "", mapEpoch = "";
        double nextMapTick, nextMapRequest, nextMapOffer, nextMapSend;
        bool mapFault;

        internal void QueueMapOffer(ushort peer, SharedMapMessage data)
        {
            // Network threads only enqueue decoded messages. Unity and save access stays in the tick.
            if (data != null && data.Valid && data.Kind != SharedMapKind.State && mapOffers.Count < MapInboxLimit)
                mapOffers.Enqueue((peer, data));
        }

        internal void QueueMapState(SharedMapMessage data)
        {
            if (data != null && data.Valid && data.Kind == SharedMapKind.State && mapInbox.Count < MapInboxLimit)
                mapInbox.Enqueue(data);
        }

        void ClearSharedMaps()
        {
            mapPlayer = null;
            mapSave = null;
            mapToken = mapEpoch = "";
            sharedMaps = new SharedMapProgress();
            mapsConfirmedByHost = new SharedMapProgress();
            observedMaps = null;
            mapSubscribers.Clear();
            mapOutbox.Clear();
            while (mapOffers.TryDequeue(out _)) { }
            while (mapInbox.TryDequeue(out _)) { }
            nextMapTick = nextMapRequest = nextMapOffer = nextMapSend = 0;
            mapFault = false;
        }

        bool EnqueueMap(ushort peer, SharedMapKind kind, string token, SharedMapProgress previous)
        {
            var batches = SharedMapMessage.Batches(kind, token, mapEpoch, sharedMaps, previous).ToArray();
            if (mapOutbox.Count + batches.Length > MapOutboxLimit || batches.Any(batch => !batch.Valid)) return false;
            foreach (var batch in batches) mapOutbox.Enqueue((peer, batch));
            return true;
        }

        void TickSharedMaps()
        {
            if (api?.NetClient.IsConnected != true || coop == null)
            {
                if (mapPlayer != null) ClearSharedMaps();
                return;
            }
            // Both initial join and F9 must install the host snapshot before publishing.
            var player = PlayerData.instance;
            if (Loading || Down || player == null || player.health <= 0 ||
                HeroController.instance == null || !HeroController.instance.gameObject.activeInHierarchy ||
                snapshotToken.Length > 0 || applyingSnapshot ||
                !AppearanceWire.CanSend(true, Value(outboundAddon, "Id"))) return;
            var key = SaveIdentity;
            var host = Hosting;
            if (key == null || (host ? server?.NetServer.IsStarted != true : !HasCheckedMapPeer())) return;
            if (!ReferenceEquals(mapPlayer, player) || mapSave != key)
            {
                ClearSharedMaps();
                mapPlayer = player;
                mapSave = key;
                mapToken = NewMapToken();
                if (host) mapEpoch = NewMapToken();
            }

            try
            {
                var now = PlayerMotionRepairs.Now;
                if (now >= nextMapTick && !NativeSaveBridge.IsWriting())
                {
                    nextMapTick = now + MapScanInterval;
                    ReconcileSharedMaps(now, host);
                }
                if (now < nextMapSend) return;
                nextMapSend = now + MapSendInterval;
                SendMapBatches(host);
                mapFault = false;
            }
            catch (Exception error)
            {
                if (mapFault) return;
                mapFault = true;
                Logger.LogError("[SharedMaps] Update deferred: " + error);
            }
        }

        void ReconcileSharedMaps(double now, bool host)
        {
            var localFlags = NativeMapBridge.MergeInto(mapPlayer, sharedMaps);
            if (host) UpdateHostMaps(now);
            else UpdateGuestMaps(now);

            var applied = NativeMapBridge.Apply(mapPlayer, sharedMaps);
            var changed = !sharedMaps.Same(observedMaps);
            if (applied || changed) ScheduleCheckpoint();
            if (changed) observedMaps = sharedMaps.Copy();
            if (!applied) return;

            var manager = global::GameManager.instance;
            if (manager != null && manager.gameMap != null)
            {
                var map = manager.gameMap;
                // Native map purchases immediately draw eligible rooms already visited.
                if ((sharedMaps.Flags & ~localFlags & SharedMapProgress.RegionalMapFlags) != 0)
                    map.UpdateGameMap();
                map.SetupMap(false);
            }
            if (localFlags != sharedMaps.Flags)
                Say("已共享队友获得的地图／绘图工具，商人处的对应地图将显示为已购买。");
            Logger.LogInfo("[SharedMaps] Applied flags=" + sharedMaps.Flags + " explored=" + sharedMaps.Rooms[0].Count +
                " mapped=" + sharedMaps.Rooms[1].Count + " benches=" + sharedMaps.Rooms[2].Count + "; checkpoint queued.");
        }

        void SendMapBatches(bool host)
        {
            // Pace collection sends and keep the head queued if SSMP rejects the enqueue.
            for (int sent = 0; sent < 2 && mapOutbox.Count > 0;)
            {
                var item = mapOutbox.Peek();
                var data = item.Data;
                if (data.Epoch == mapEpoch)
                {
                    if (host && data.Kind == SharedMapKind.State && IsCheckedMapClient(item.Peer) &&
                        mapSubscribers.TryGetValue(item.Peer, out var subscriber) && data.Token == subscriber.Token)
                    {
                        sender.SendCollectionData(AssistPacket.SharedMaps, data, item.Peer);
                        sent++;
                    }
                    else if (!host && data.Kind == SharedMapKind.Offer && data.Token == mapToken)
                    {
                        appearanceSender.SendCollectionData(AssistPacket.SharedMaps, data);
                        sent++;
                    }
                }
                mapOutbox.Dequeue();
            }
        }

        static string NewMapToken() => Guid.NewGuid().ToString("N");
        bool HasCheckedMapPeer() => checkedField?.GetValue(coop) is ICollection<ushort> members && members.Count > 0;
        bool IsCheckedMapClient(ushort peer) => Checked(peer) && server.ServerManager.GetPlayer(peer) != null;

        void UpdateHostMaps(double now)
        {
            foreach (var peer in mapSubscribers.Keys.ToArray())
                if (!IsCheckedMapClient(peer)) mapSubscribers.Remove(peer);

            while (mapOffers.TryDequeue(out var offer))
            {
                if (!IsCheckedMapClient(offer.Peer)) continue;
                var data = offer.Data;
                if (data.Kind == SharedMapKind.Request)
                {
                    if (!mapSubscribers.TryGetValue(offer.Peer, out var subscriber) || subscriber.Token != data.Token)
                        mapSubscribers[offer.Peer] = new MapSubscriber { Token = data.Token };
                    // A periodic full response repairs batches dropped while a guest was loading.
                    else if (data.Epoch != mapEpoch || now - subscriber.LastFull >= MapFullRefreshInterval)
                        subscriber.Queued = null;
                }
                else if (data.Epoch == mapEpoch && mapSubscribers.TryGetValue(offer.Peer, out var subscriber) &&
                    subscriber.Token == data.Token)
                    sharedMaps.Merge(data.Progress());
            }
            foreach (var pair in mapSubscribers)
            {
                var subscriber = pair.Value;
                if (sharedMaps.Same(subscriber.Queued) ||
                    !EnqueueMap(pair.Key, SharedMapKind.State, subscriber.Token, subscriber.Queued)) continue;
                if (subscriber.Queued == null) subscriber.LastFull = now;
                subscriber.Queued = sharedMaps.Copy();
            }
        }

        void UpdateGuestMaps(double now)
        {
            while (mapInbox.TryDequeue(out var data))
            {
                if (data.Token != mapToken) continue;
                if (mapEpoch.Length > 0 && mapEpoch != data.Epoch)
                {
                    // Confirm which host epoch is current with a fresh request before merging.
                    mapToken = NewMapToken();
                    mapEpoch = "";
                    mapsConfirmedByHost = new SharedMapProgress();
                    mapOutbox.Clear();
                    nextMapRequest = nextMapOffer = 0;
                    continue;
                }
                mapEpoch = data.Epoch;
                var confirmed = data.Progress();
                sharedMaps.Merge(confirmed);
                mapsConfirmedByHost.Merge(confirmed);
            }
            if (now >= nextMapRequest)
            {
                appearanceSender.SendCollectionData(AssistPacket.SharedMaps,
                    new SharedMapMessage { Kind = SharedMapKind.Request, Token = mapToken, Epoch = mapEpoch });
                nextMapRequest = now + MapRetryInterval;
            }
            if (mapEpoch.Length > 0 && now >= nextMapOffer && mapOutbox.Count == 0 &&
                !sharedMaps.Same(mapsConfirmedByHost) &&
                EnqueueMap(0, SharedMapKind.Offer, mapToken, mapsConfirmedByHost))
                nextMapOffer = now + MapRetryInterval;
        }
    }
}
