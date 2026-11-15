using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using SSMP.Api.Client;
using SSMP.Api.Server;
using UnityEngine;

namespace Silksong.CoopAssist
{
    public sealed partial class CoopAssistPlugin
    {
        // The borrow flow: ask whether to borrow, pick a lender, then wait for the answer.
        internal enum RosaryStage : byte { ConfirmBorrow, ChoosePeer, Waiting }

        sealed class Purchase
        {
            internal object Target, Menu;
            internal DialogueYesNoBox BenchBox;
            internal string Name, Place, Scene, Request, Peer;
            internal int Price, Amount;
            internal RosaryMode Mode;
            internal RosaryStage Stage;
            internal float Expires;
            internal RosaryMessage Grant;
            internal bool LoanCredited;
            internal RosaryTransfer LoanRow;
        }

        readonly ConcurrentQueue<(ushort Peer, RosaryMessage Data)> rosaryFromClients = new ConcurrentQueue<(ushort, RosaryMessage)>();
        readonly ConcurrentQueue<RosaryMessage> rosaryFromServer = new ConcurrentQueue<RosaryMessage>();
        readonly Dictionary<ushort, (string Identity, string AuthKey)> rosaryRoutes =
            new Dictionary<ushort, (string, string)>();
        readonly Dictionary<ushort, string> rosaryPeers = new Dictionary<ushort, string>();
        readonly Dictionary<string, (RosaryMessage Message, float Expires)> rosaryAsks =
            new Dictionary<string, (RosaryMessage, float)>();
        readonly List<string> expiredRosaryAsks = new List<string>();

        readonly TollPaymentRace tollRace = new TollPaymentRace();
        ConfigEntry<bool> loanSetting;
        RosarySave rosarySave;
        SceneData rosaryScene;
        PlayerData rosaryPlayer;
        Purchase purchase;
        bool roomLoans, rosaryDirty, rosarySaving, rosaryFailed, changingWallet, rosaryIdentitySaved;
        float nextRosaryTick, nextRosaryHello, nextRosarySend, nextRosarySave;
        int rosaryGeneration, rosaryPaymentCursor;
        readonly List<RosaryMessage> sendAfterSave = new List<RosaryMessage>();
        RosaryLedger Ledger => rosarySave?.Ledger;
        string RosaryIdentity = "";
        bool LoansOpen => Hosting ? loanSetting.Value : roomLoans;
        bool RosaryConnected => compatible && appearanceSender != null &&
            AppearanceWire.CanSend(api?.NetClient.IsConnected == true, Value(outboundAddon, "Id"));
        bool WalletReady => rosarySave != null && !rosaryFailed && !Loading && !Down && rosaryPlayer != null &&
            rosaryPlayer == PlayerData.instance && rosaryPlayer.health > 0 && !applyingSnapshot && snapshotToken.Length == 0;
        bool CanWriteWallet => WalletReady && !rosarySaving && !NativeSaveBridge.IsWriting();
        bool WalletSavePending => rosaryDirty || rosarySaving;
        int SpendableRosaries => RosaryPurchaseHooks.SpendableRosaries(rosaryPlayer);
        static long RosaryNow => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        static int TollClock => (int)(RosaryNow - 1700000000L);

        void ConfigureRosaries()
        {
            loanSetting = Config.Bind("Team", "EnableRosaryLoans", true,
                "Host only: allow rosary loans in this room (F7). Borrowing is offered whenever a purchase falls short.");
            RosaryPurchaseHooks.Install();
        }

        internal void QueueRosary(ushort peer, RosaryMessage data)
        {
            if (data.Valid && rosaryFromClients.Count < 128) rosaryFromClients.Enqueue((peer, data));
        }

        internal void QueueRosary(RosaryMessage data)
        {
            if (data.Valid && rosaryFromServer.Count < 128) rosaryFromServer.Enqueue(data);
        }

        void TickRosaries()
        {
            if (Time.unscaledTime < nextRosaryTick) return;
            nextRosaryTick = Time.unscaledTime + .15f;
            try
            {
                DrainSaveCompletions();
                LoadRosarySave();
                ExpireRosaryAsks();
                if (RosaryConnected)
                {
                    if (WalletReady && rosaryIdentitySaved && !rosaryDirty && !rosarySaving && Time.unscaledTime >= nextRosaryHello)
                    {
                        RosaryIdentity = RosaryMessage.Identity(Ledger.Owner, Value(Value(api.ClientManager, "_modSettings"), "AuthKey") as string);
                        SendRosary(NewRosaryMessage(RosaryKind.Hello, id: Ledger.Owner));
                        nextRosaryHello = Time.unscaledTime + 2;
                    }
                    while (rosaryFromClients.TryDequeue(out var packet))
                    {
                        try { RouteRosary(packet.Peer, packet.Data); }
                        catch (InvalidOperationException) when (server?.NetServer.IsStarted != true ||
                            server.ServerManager.GetPlayer(packet.Peer) == null ||
                            (packet.Data.Kind != RosaryKind.Hello && !HasRosaryRoute(packet.Data.To))) { }
                    }
                    if (!rosarySaving && !NativeSaveBridge.IsWriting())
                        while (!rosaryDirty && rosaryFromServer.TryDequeue(out var incoming)) ReceiveRosary(incoming);
                }
                if (purchase != null && (!WalletReady || !RosaryConnected || Scene != purchase.Scene ||
                    Time.unscaledTime > purchase.Expires)) CancelRosaryPurchase();
                if (CanWriteWallet && !rosaryDirty && rosarySave.Exists && Ledger.HasDebts) CollectDueRosaries();
                if (RosaryConnected && CanWriteWallet && !rosarySave.Exists && Ready && Playing)
                    rosaryDirty = true;
                if (CanWriteWallet && rosaryDirty && Time.unscaledTime >= nextRosarySave) SaveRosaries();
                if (RosaryConnected && WalletReady && !rosaryDirty && !rosarySaving && Time.unscaledTime >= nextRosarySend)
                {
                    RetryRosaryTransfers();
                    nextRosarySend = Time.unscaledTime + 2;
                }
            }
            catch (Exception error)
            {
                if (!rosaryFailed)
                {
                    rosaryFailed = true;
                    Logger.LogError("[Rosaries] " + error);
                    Say("念珠账本暂时无法保存或读取，已暂停联机付款。");
                }
            }
        }

        void LoadRosarySave()
        {
            if (Loading) return;
            var player = PlayerData.instance;
            var manager = global::GameManager.instance;
            var scene = manager.sceneData;
            if (player == rosaryPlayer && scene == rosaryScene) return;
            rosaryGeneration++;
            rosarySaving = rosaryDirty = rosaryFailed = false;
            sendAfterSave.Clear();
            ledgerOpen = false;
            purchase = null;
            rosaryAsks.Clear();
            rosaryPeers.Clear();
            rosaryPlayer = player;
            rosaryScene = scene;
            rosarySave = player != null && scene != null ? new RosarySave(scene) : null;
            RosaryIdentity = "";
            rosaryIdentitySaved = rosarySave?.Exists == true;
            RosaryPurchaseHooks.ClearPayments();
            nextRosaryHello = nextRosarySend = nextRosarySave = 0;
        }

        void SaveRosaries()
        {
            rosarySave.Write();
            var generation = rosaryGeneration;
            rosarySaving = true;
            rosaryDirty = false;
            var messages = sendAfterSave.ToArray();
            sendAfterSave.Clear();
            void Complete(bool ok)
            {
                if (generation != rosaryGeneration) return;
                rosarySaving = false;
                if (!ok)
                {
                    rosaryDirty = true;
                    sendAfterSave.InsertRange(0, messages);
                    nextRosarySave = Time.unscaledTime + 3;
                    Say("念珠记录保存失败，稍后自动重试。");
                    return;
                }
                rosaryIdentitySaved = true;
                foreach (var message in messages) SendRosary(message);
                nextRosarySend = 0;
            }
            try { global::GameManager.instance.SaveGame(ok => saveCompletions.Enqueue(() => Complete(ok))); }
            catch (Exception) { Complete(false); throw; }
        }

        internal void FlushRosaryLedgerForNativeSave()
        {
            if (rosaryDirty && rosarySave != null && rosaryPlayer == PlayerData.instance) rosarySave.Write();
        }

        void ClearRosarySession()
        {
            roomLoans = false;
            rosaryRoutes.Clear();
            tollRace.Clear();
            rosaryPeers.Clear();
            rosaryAsks.Clear();
            CancelRosaryPurchase();
            RosaryPurchaseHooks.ClearPayments();
            ledgerOpen = false;
            while (rosaryFromClients.TryDequeue(out _)) { }
            while (rosaryFromServer.TryDequeue(out _)) { }
            nextRosaryHello = 0;
        }

        RosaryMessage NewRosaryMessage(RosaryKind kind, string to = "", string id = "") =>
            new RosaryMessage { Kind = kind, From = RosaryIdentity, To = to, Id = id };

        void SendRosary(RosaryMessage message)
        {
            if (RosaryConnected && Ledger != null && message.From == RosaryIdentity && RosaryMessage.Key(RosaryIdentity))
            {
                try { appearanceSender.SendCollectionData(AssistPacket.Rosaries, message); }
                catch (InvalidOperationException) when (!RosaryConnected) { }
            }
        }

        bool HasRosaryRoute(string identity) => rosaryRoutes.Any(route =>
            route.Value.Identity == identity && server.ServerManager.GetPlayer(route.Key)?.AuthKey == route.Value.AuthKey);

        void RouteRosary(ushort peer, RosaryMessage message)
        {
            if (!Hosting || server?.NetServer.IsStarted != true || !(server.ServerManager.GetPlayer(peer) is IServerPlayer player) ||
                message.Kind == RosaryKind.Rules || message.Kind == RosaryKind.BenchResult) return;
            if (message.Kind == RosaryKind.Hello)
            {
                if (message.From != RosaryMessage.Identity(message.Id, player.AuthKey)) return;
                if (rosaryRoutes.Any(pair => pair.Key != peer && pair.Value.Identity == message.From &&
                    server.ServerManager.GetPlayer(pair.Key)?.AuthKey == pair.Value.AuthKey)) return;
                rosaryRoutes[peer] = (message.From, player.AuthKey);
                message.Id = "";
                message.Peer = peer;
                sender.BroadcastCollectionData(AssistPacket.Rosaries, message);
                sender.SendCollectionData(AssistPacket.Rosaries, new RosaryMessage {
                    Kind = RosaryKind.Rules, Flag = loanSetting.Value
                }, peer);
                return;
            }
            if (!rosaryRoutes.TryGetValue(peer, out var owner) || owner.Identity != message.From ||
                owner.AuthKey != player.AuthKey) return;
            if (message.Kind == RosaryKind.BenchPaid)
            {
                if (api.ClientManager.GetPlayer(peer) != null && !Checked(peer)) return;
                if (tollRace.TrySettle(message.Label, message.Id, owner.Identity, message.At, TollClock, Time.unscaledTime, out bool refund))
                    sender.SendCollectionData(AssistPacket.Rosaries, new RosaryMessage {
                        Kind = RosaryKind.BenchResult, To = owner.Identity, Id = message.Id, Flag = refund
                    }, peer);
                return;
            }
            if (message.From == message.To) return;
            if (message.Kind == RosaryKind.Ask && message.Mode == RosaryMode.Loan && !loanSetting.Value) return;
            foreach (var route in rosaryRoutes)
            {
                if (route.Value.Identity != message.To || server.ServerManager.GetPlayer(route.Key)?.AuthKey != route.Value.AuthKey) continue;
                message.Peer = peer;
                sender.SendCollectionData(AssistPacket.Rosaries, message, route.Key);
                return;
            }
        }

        void ReceiveRosary(RosaryMessage message)
        {
            if (message.Kind == RosaryKind.Rules) { roomLoans = message.Flag; return; }
            if (message.Kind == RosaryKind.Hello)
            {
                if (api.ClientManager.GetPlayer(message.Peer) != null) rosaryPeers[message.Peer] = message.From;
                return;
            }
            if (Ledger == null || message.To != RosaryIdentity) return;
            switch (message.Kind)
            {
                case RosaryKind.Ask: ReceiveRosaryAsk(message); break;
                case RosaryKind.Decline:
                    if (purchase?.Request == message.Id && purchase.Peer == message.From)
                    {
                        if (purchase.Mode == RosaryMode.Loan) CancelLoanPurchase();
                        else
                        {
                            purchase.Request = purchase.Peer = null;
                            SayChat("队友未同意本次补差。");
                        }
                    }
                    break;
                case RosaryKind.Grant: ReceiveGrant(message); break;
                case RosaryKind.Accepted:
                case RosaryKind.Rejected: SettleGrant(message); break;
                case RosaryKind.Payment:
                case RosaryKind.Refund: ReceivePayment(message); break;
                case RosaryKind.BenchResult: ReceiveTollResult(message); break;
                case RosaryKind.PaymentAck:
                    if (Ledger.Payments.TryGetValue(message.Id, out var payment) && payment.Transfer == message.Ref &&
                        Ledger.Transfers.TryGetValue(payment.Transfer, out var transfer) && transfer.Peer == message.From)
                    {
                        Ledger.Payments.Remove(payment.Id);
                        rosaryDirty = true;
                    }
                    break;
            }
        }

        // Entry from a failed purchase: borrow when the room allows it, otherwise pool the shortfall.
        internal bool OfferRosaryPurchase(object target, int price, string name, bool bench,
            DialogueYesNoBox box = null, object menu = null)
        {
            if (!bench && LoansOpen) return BeginRosaryLoan(target, menu, price, name);
            if (!bench && price > SpendableRosaries)
                SayChat("房主未开启借款，本次可以请队友攒钱补差；房主可在 F7 打开借款。");
            return OpenContribution(target, menu, price, name, bench, box);
        }

        bool OpenContribution(object target, object menu, int price, string name, bool bench, DialogueYesNoBox box)
        {
            if (!ReadyToAsk(price)) return false;
            if (purchase != null && ReferenceEquals(purchase.Target, target))
            {
                purchase.Expires = Time.unscaledTime + 45;
                return true;
            }
            CancelRosaryPurchase();
            var manager = global::GameManager.instance;
            purchase = new Purchase {
                Target = target, Menu = menu, BenchBox = box, Price = price, Name = name, Scene = Scene,
                Place = manager.GetFormattedMapZoneString(manager.GetCurrentMapZoneEnum()),
                Mode = bench ? RosaryMode.Bench : RosaryMode.Pool, Expires = Time.unscaledTime + 45
            };
            return true;
        }

        bool ReadyToAsk(int price) =>
            CanWriteWallet && !rosaryDirty && rosaryIdentitySaved && RosaryConnected &&
            price > SpendableRosaries && price <= RosaryLedger.MaxAmount &&
            api.ClientManager.Players.Any(p => Checked(p.Id) && rosaryPeers.ContainsKey(p.Id));

        // Borrow the shortfall from one teammate: their wallet pays us, we buy at full price and owe them.
        internal bool BeginRosaryLoan(object target, object menu, int price, string name)
        {
            if (!LoansOpen || !ReadyToAsk(price)) return false;
            if (purchase != null && ReferenceEquals(purchase.Target, target))
            {
                purchase.Expires = Time.unscaledTime + 90;
                return true;
            }
            CancelRosaryPurchase();
            var manager = global::GameManager.instance;
            purchase = new Purchase {
                Target = target, Menu = menu, Price = price, Name = name, Scene = Scene,
                Place = manager.GetFormattedMapZoneString(manager.GetCurrentMapZoneEnum()),
                Mode = RosaryMode.Loan, Stage = RosaryStage.ConfirmBorrow, Expires = Time.unscaledTime + 90
            };
            return true;
        }

        void ConfirmBorrow()
        {
            if (purchase == null) return;
            borrowCursor = 0;
            purchase.Stage = RosaryStage.ChoosePeer;
            purchase.Expires = Time.unscaledTime + 60;
        }

        void DeclineBorrow()
        {
            var menu = purchase?.Menu;
            purchase = null;
            if (menu != null) RosaryPurchaseHooks.CloseNativeConfirm(menu);
        }


        internal void LogWarning(string message) => Logger.LogWarning(message);

        void AskRosaries(IClientPlayer peer, RosaryMode mode)
        {
            if (!CanWriteWallet || purchase == null || purchase.Request != null ||
                !rosaryPeers.TryGetValue(peer.Id, out var identity) || !Checked(peer.Id)) return;
            int amount = purchase.Price - SpendableRosaries;
            if (amount <= 0) { CancelRosaryPurchase(); return; }
            var message = NewRosaryMessage(RosaryKind.Ask, identity, Guid.NewGuid().ToString("N"));
            message.Mode = mode;
            message.Label = LimitRosaryLabel(purchase.Name);
            message.Ref = LimitRosaryLabel(purchase.Place);
            message.Amount = amount;
            purchase.Request = message.Id;
            purchase.Peer = identity;
            purchase.Amount = amount;
            purchase.Mode = mode;
            purchase.Expires = Time.unscaledTime + 90;
            SendRosary(message);
            purchase.Stage = RosaryStage.Waiting;
            SayChat(mode == RosaryMode.Loan
                ? "已向 " + peer.Username + " 借 " + amount + " 念珠，等待同意。"
                : "已请 " + peer.Username + " 补差 " + amount + " 念珠。");
        }

        void ReceiveRosaryAsk(RosaryMessage message)
        {
            if (!WalletReady || !Checked(message.Peer) || !rosaryPeers.TryGetValue(message.Peer, out var identity) ||
                identity != message.From || (message.Mode == RosaryMode.Loan && !LoansOpen) ||
                Ledger.Transfers.ContainsKey(message.Id) || rosaryAsks.Count >= 8) return;
            rosaryAsks.TryAdd(message.Id, (message, Time.unscaledTime + 45));
            var lenderName = api.ClientManager.GetPlayer(message.Peer)?.Username ?? "队友";
            SayChat(message.Mode == RosaryMode.Loan
                ? lenderName + " 想借 " + message.Amount + " 念珠，请在弹窗里答复。"
                : lenderName + " 请求补差 " + message.Amount + " 念珠。");
        }

        void ExpireRosaryAsks()
        {
            if (rosaryAsks.Count == 0) return;
            expiredRosaryAsks.Clear();
            foreach (var ask in rosaryAsks)
                if (ask.Value.Expires <= Time.unscaledTime || api.ClientManager.GetPlayer(ask.Value.Message.Peer) == null)
                    expiredRosaryAsks.Add(ask.Key);
            foreach (var id in expiredRosaryAsks) rosaryAsks.Remove(id);
        }

        void AnswerRosaryAsk(RosaryMessage ask, bool agree)
        {
            if (!rosaryAsks.Remove(ask.Id)) return;
            if (!agree || !CanWriteWallet || !RosaryConnected || !Checked(ask.Peer) ||
                !rosaryPeers.TryGetValue(ask.Peer, out var identity) || identity != ask.From ||
                (ask.Mode == RosaryMode.Loan && !LoansOpen) || SpendableRosaries < ask.Amount)
            {
                SendRosary(NewRosaryMessage(RosaryKind.Decline, ask.From, ask.Id));
                if (agree) SayChat("目前无法借出，请核对余额和联机状态。");
                return;
            }
            var row = MakeTransfer(ask, true);
            row.State = RosaryTransferState.Offered;
            if (!Ledger.Transfers.TryAdd(row.Id, row)) return;
            ChangeWallet(-row.Amount);
            rosaryDirty = true;
            SayChat(ask.Mode == RosaryMode.Loan
                ? "已借出 " + row.Amount + " 念珠给队友，等他还。"
                : "已同意补差 " + row.Amount + " 念珠，等待队友完成购买。");
        }

        // Outgoing decides which side sends the grant and which side owes: without it the lender's row stayed
        // incoming, so the transfer message was never retried and the borrower was never credited.
        RosaryTransfer MakeTransfer(RosaryMessage message, bool outgoing) => new RosaryTransfer {
            Id = message.Id, Peer = message.From, Mode = message.Mode, Amount = message.Amount, Outgoing = outgoing,
            Name = api.ClientManager.GetPlayer(message.Peer)?.Username ?? "队友",
            Place = outgoing ? message.Ref : ""
        };

        void ReceiveGrant(RosaryMessage message)
        {
            if (Ledger.Transfers.TryGetValue(message.Id, out var existing))
            {
                if (!existing.Outgoing && existing.Peer == message.From && existing.Amount == message.Amount)
                    SendGrantResult(existing);
                return;
            }
            if (WalletReady && purchase?.Request == message.Id && purchase.Peer == message.From &&
                purchase.Amount == message.Amount && purchase.Mode == message.Mode && Time.unscaledTime <= purchase.Expires)
            {
                if (purchase.Grant == null)
                {
                    purchase.Grant = message;
                    if (purchase.Mode == RosaryMode.Loan) CreditLoan(message);
                    else SayChat("队友已同意补差。请再次确认原来的购买。");
                }
                return;
            }
            RejectGrant(message);
        }

        // The lender's money is already ours: credit the wallet, write the debt, then finish the purchase.
        void CreditLoan(RosaryMessage grant)
        {
            var current = purchase;
            if (current == null || !CanReceiveRosaries(grant.Amount)) { RejectGrant(grant); return; }
            var row = MakeTransfer(grant, false);
            row.State = RosaryTransferState.Accepted;
            row.Place = LimitRosaryLabel(current.Place);
            row.Due = RosaryNow + RosaryLedger.OverdueSeconds;
            Ledger.Transfers[row.Id] = row;
            ChangeWallet(grant.Amount);
            current.LoanCredited = true;
            current.LoanRow = row;
            rosaryDirty = true;
            var accepted = NewRosaryMessage(RosaryKind.Accepted, row.Peer, row.Id);
            sendAfterSave.Add(accepted);
            SayChat("已借到 " + grant.Amount + " 念珠，继续购买。");
            var target = current.Target;
            var menu = current.Menu;
            int price = current.Price;
            purchase = null;
            if (!RosaryPurchaseHooks.ResumePurchase(target, menu, price))
                SayChat("已借到念珠，请回到商店再次确认购买。");
        }

        void RejectGrant(RosaryMessage message)
        {
            var row = MakeTransfer(message, false);
            row.State = RosaryTransferState.Rejected;
            Ledger.Transfers.Add(row.Id, row);
            rosaryDirty = true;
        }

        void CancelRosaryPurchase()
        {
            if (purchase != null && purchase.LoanCredited && purchase.LoanRow != null)
            {
                // The money already moved, so give it back through the ordinary repayment path.
                if (CanWriteWallet) RepayRosaries(purchase.LoanRow);
            }
            else if (purchase?.Grant is RosaryMessage grant && Ledger != null) RejectGrant(grant);
            var menu = purchase?.Menu;
            purchase = null;
            if (menu != null) RosaryPurchaseHooks.CloseNativeConfirm(menu);
        }

        void CancelLoanPurchase()
        {
            var menu = purchase?.Menu;
            if (purchase != null && purchase.LoanCredited && purchase.LoanRow != null && CanWriteWallet)
                RepayRosaries(purchase.LoanRow);
            purchase = null;
            if (menu != null) RosaryPurchaseHooks.CloseNativeConfirm(menu);
            SayChat("队友拒绝了借款，可能也没钱。");
        }

        void SendGrantResult(RosaryTransfer row)
        {
            var message = NewRosaryMessage(row.State == RosaryTransferState.Accepted ?
                RosaryKind.Accepted : RosaryKind.Rejected, row.Peer, row.Id);
            SendRosary(message);
        }

        void SettleGrant(RosaryMessage message)
        {
            if (!CanWriteWallet || !Ledger.Transfers.TryGetValue(message.Id, out var row) || !row.Outgoing ||
                row.Peer != message.From || row.State != RosaryTransferState.Offered) return;
            if (message.Kind == RosaryKind.Accepted)
            {
                row.State = RosaryTransferState.Accepted;
                if (row.Mode == RosaryMode.Loan) row.Due = RosaryNow + RosaryLedger.OverdueSeconds;
            }
            else
            {
                if (!CanReceiveRosaries(row.Amount)) return;
                row.State = RosaryTransferState.Rejected;
                ChangeWallet(row.Amount);
                Say("购买已取消，代付的念珠已退回。");
            }
            rosaryDirty = true;
        }

        internal bool HasRosaryGrant(object target, int price) =>
            CanWriteWallet && !rosaryDirty && purchase?.Grant != null && ReferenceEquals(purchase.Target, target) &&
            purchase.Price == price && purchase.Scene == Scene && Time.unscaledTime <= purchase.Expires &&
            SpendableRosaries < price && SpendableRosaries >= price - purchase.Amount;

        internal bool IsFundedBench(object token) => purchase?.Grant != null &&
            purchase.Mode == RosaryMode.Bench && ReferenceEquals(purchase.Target, token);

        internal void CancelUnusedBenchContribution() => CancelRosaryPurchase();

        internal void ExtendBenchConfirmation()
        {
            if (purchase != null) purchase.Expires = Time.unscaledTime + 10;
        }

        internal RosaryTransfer UseRosaryGrant(object target, int price)
        {
            if (!HasRosaryGrant(target, price)) return null;
            var row = MakeTransfer(purchase.Grant, false);
            row.State = RosaryTransferState.Accepted;
            row.Place = LimitRosaryLabel(purchase.Place);
            if (row.Mode == RosaryMode.Loan) row.Due = RosaryNow + RosaryLedger.OverdueSeconds;
            Ledger.Transfers.Add(row.Id, row);
            purchase = null;
            rosaryDirty = true;
            return row;
        }

        internal void ChargePersonalRosaries(int amount)
        {
            CurrencyCounter.RefreshStartCount(CurrencyType.Money);
            rosaryPlayer.TakeGeo(amount);
            CurrencyCounter.Take(amount, CurrencyType.Money);
        }

        internal bool RecordTollPayment(string bench, int personal, string contribution)
        {
            if (!RosaryConnected || !rosaryIdentitySaved || !CanWriteWallet ||
                !api.ClientManager.Players.Any(peer => Checked(peer.Id))) return false;
            var payment = new TollPayment { Id = Guid.NewGuid().ToString("N"), Bench = bench,
                Personal = personal, Contribution = contribution, At = TollClock };
            Ledger.TollPayments.Add(payment.Id, payment);
            rosaryDirty = true;
            return true;
        }

        void ReceiveTollResult(RosaryMessage message)
        {
            if (!CanWriteWallet || !Ledger.TollPayments.TryGetValue(message.Id, out var toll)) return;
            if (message.Flag)
            {
                if (!CanReceiveRosaries(toll.Personal)) return;
                if (toll.Personal > 0) ChangeWallet(toll.Personal);
                if (toll.Contribution != null && Ledger.Transfers.TryGetValue(toll.Contribution, out var row))
                    UndoRosaryShare(row);
                Say("队友同时解锁了这张长椅，重复支付的念珠已退回。");
            }
            Ledger.TollPayments.Remove(toll.Id);
            rosaryDirty = true;
        }

        internal void UndoRosaryShare(RosaryTransfer row)
        {
            if (row == null || Ledger == null || !Ledger.Transfers.TryGetValue(row.Id, out var current)) return;
            if (current.Mode == RosaryMode.Loan)
            {
                current.SettleAfterNativeRefund = true;
                rosaryDirty = true;
            }
            else if (Ledger.Refund(current) != null) rosaryDirty = true;
        }

        void ChangeWallet(int change)
        {
            CurrencyCounter.RefreshStartCount(CurrencyType.Money);
            changingWallet = true;
            try
            {
                if (change > 0) rosaryPlayer.AddGeo(change);
                else rosaryPlayer.TakeGeo(-change);
            }
            finally { changingWallet = false; }
            if (change > 0) CurrencyCounter.Add(change, CurrencyType.Money);
            else CurrencyCounter.Take(-change, CurrencyType.Money);
        }

        bool CanReceiveRosaries(int amount) =>
            rosaryPlayer.geo <= GlobalSettings.Gameplay.GetCurrencyCap(CurrencyType.Money) - amount;

        internal void CollectAfterRosaryGain(PlayerData player, int amount)
        {
            if (amount <= 0 || changingWallet || !CanWriteWallet || player != rosaryPlayer ||
                !Ledger.HasDebts) return;
            CollectDueRosaries();
        }

        void CollectDueRosaries()
        {
            var now = RosaryNow;
            foreach (var row in Ledger.DueDebts(now))
            {
                var payment = Ledger.Repay(row, SpendableRosaries, now, out bool newlyOverdue);
                if (payment != null) ChangeWallet(-payment.Amount);
                if (newlyOverdue) Say("对 " + row.Name + " 的欠款已逾期，加收 3 念珠，后续余额将优先还款。");
                rosaryDirty |= newlyOverdue || payment != null;
            }
        }

        void RepayRosaries(RosaryTransfer row)
        {
            if (!CanWriteWallet || row.Outgoing || row.Balance <= 0) return;
            var payment = Ledger.Repay(row, SpendableRosaries, RosaryNow, out bool newlyOverdue);
            if (payment == null) Say("身上的散装念珠不足。");
            else
            {
                ChangeWallet(-payment.Amount);
                Say("已交付 " + payment.Amount + " 念珠用于还款。");
            }
            rosaryDirty |= newlyOverdue || payment != null;
        }

        void ReceivePayment(RosaryMessage message)
        {
            if (!CanWriteWallet || !Ledger.Transfers.TryGetValue(message.Ref, out var row) || row.Peer != message.From) return;
            var ack = NewRosaryMessage(RosaryKind.PaymentAck, row.Peer, message.Id);
            ack.Ref = row.Id;
            if (Ledger.Receipts.Contains(message.Id)) { SendRosary(ack); return; }
            var payment = new RosaryPayment {
                Id = message.Id, Transfer = row.Id, Amount = message.Amount, Interest = message.Flag,
                Refund = message.Kind == RosaryKind.Refund
            };
            if (!Ledger.CanReceive(row, payment) || !CanReceiveRosaries(payment.Amount)) return;
            ChangeWallet(payment.Amount);
            Ledger.Receive(row, payment);
            sendAfterSave.Add(ack);
            rosaryDirty = true;
            Say((payment.Refund ? "已退回代付的 " : "已收到还款 ") + payment.Amount + " 念珠。");
        }

        void RetryRosaryTransfers()
        {
            foreach (var toll in Ledger.TollPayments.Values.Take(8))
            {
                var message = NewRosaryMessage(RosaryKind.BenchPaid, RosaryIdentity, toll.Id);
                message.Label = toll.Bench;
                message.At = toll.At;
                SendRosary(message);
            }
            int remaining = 8;
            var online = new HashSet<string>(api.ClientManager.Players.Where(p => rosaryPeers.ContainsKey(p.Id))
                .Select(p => rosaryPeers[p.Id]));
            foreach (var row in Ledger.Transfers.Values)
            {
                if (!row.Outgoing || row.State != RosaryTransferState.Offered || !online.Contains(row.Peer)) continue;
                var message = NewRosaryMessage(RosaryKind.Grant, row.Peer, row.Id);
                message.Mode = row.Mode;
                message.Amount = row.Amount;
                SendRosary(message);
                if (--remaining == 0) break;
            }
            // Send the outcome when the lender retries its grant. Repayments are independently idempotent.
            if (Ledger.Payments.Count == 0) return;
            var eligible = Ledger.Payments.Values.Where(payment => online.Contains(Ledger.Transfers[payment.Transfer].Peer)).ToArray();
            if (eligible.Length == 0) return;
            int start = rosaryPaymentCursor % eligible.Length;
            int count = Math.Min(32, eligible.Length);
            rosaryPaymentCursor = (start + count) % eligible.Length;
            for (int index = 0; index < count; index++)
            {
                var payment = eligible[(start + index) % eligible.Length];
                var row = Ledger.Transfers[payment.Transfer];
                var message = NewRosaryMessage(payment.Refund ? RosaryKind.Refund : RosaryKind.Payment, row.Peer, payment.Id);
                message.Ref = row.Id;
                message.Amount = payment.Amount;
                message.Flag = payment.Interest;
                SendRosary(message);
            }
        }

        static string LimitRosaryLabel(string value) => string.IsNullOrEmpty(value) ? "" :
            value.Length <= 128 ? value : value.Substring(0, 128);
    }
}
