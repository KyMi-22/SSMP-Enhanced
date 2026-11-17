using System;
using System.Collections.Generic;
using System.Linq;

namespace Silksong.CoopAssist
{
    public enum RosaryMode : byte { Pool, Loan, Bench }
    public enum RosaryTransferState : byte { Offered, Accepted, Rejected }

    public sealed class RosaryTransfer
    {
        public string Id, Peer, Name, Place;
        public RosaryMode Mode;
        public int Amount, Repaid;
        public long Due;
        public bool Outgoing, Overdue, Hidden, Refunded, SettleAfterNativeRefund;
        public RosaryTransferState State;
        // Session clock when the lender put the money down; 0 for records written by older builds.
        public float OfferedAt;
        // One period per OverdueSeconds after Due; each adds 3 rosaries, and the next period adds another 3.
        public int OverduePeriods;
        public int Interest => 3 * Math.Max(OverduePeriods, Overdue ? 1 : 0);
        public int Balance => Amount + Interest - Repaid;

        internal int PeriodsAt(long now) =>
            Due <= 0 || now < Due ? 0 : 1 + (int)((now - Due) / RosaryLedger.OverdueSeconds);

        // Accrues every period that has passed. Returns true when new interest was added.
        internal bool Refresh(long now)
        {
            var periods = Math.Max(PeriodsAt(now), Overdue ? 1 : 0);
            if (periods <= OverduePeriods) return false;
            OverduePeriods = periods;
            Overdue = true;
            return true;
        }
    }

    public sealed class RosaryPayment
    {
        public string Id, Transfer;
        public int Amount;
        public bool Interest, Refund;
    }

    public sealed class TollPayment
    {
        public string Id, Bench, Contribution;
        public int Personal;
        public int At;
    }

    public sealed class RosaryLedger
    {
        public const int MaxAmount = 1000000;
        public const int OverdueSeconds = 600;
        public string Owner = Guid.NewGuid().ToString("N");
        public Dictionary<string, RosaryTransfer> Transfers = new Dictionary<string, RosaryTransfer>(StringComparer.Ordinal);
        public Dictionary<string, RosaryPayment> Payments = new Dictionary<string, RosaryPayment>(StringComparer.Ordinal);
        public HashSet<string> Receipts = new HashSet<string>(StringComparer.Ordinal);
        public Dictionary<string, TollPayment> TollPayments = new Dictionary<string, TollPayment>(StringComparer.Ordinal);

        public IEnumerable<RosaryTransfer> Debts => Transfers.Values.Where(row =>
            row.Mode == RosaryMode.Loan && row.State == RosaryTransferState.Accepted);
        public bool HasDebts => Debts.Any(row => !row.Outgoing && row.Balance > 0);

        public IEnumerable<RosaryTransfer> DueDebts(long now) => Debts.Where(row =>
            !row.Outgoing && row.Balance > 0 && (row.Due <= now || row.SettleAfterNativeRefund))
            .OrderBy(row => row.Due).ThenBy(row => row.Id);

        public RosaryPayment Repay(RosaryTransfer loan, int available, long now, out bool becameOverdue)
        {
            becameOverdue = loan.Refresh(now);
            int amount = Math.Min(Math.Max(0, available), Math.Max(0, loan.Balance));
            if (amount <= 0) return null;
            loan.Repaid += amount;
            return AddPayment(loan, amount, loan.Overdue, false);
        }

        public RosaryPayment Refund(RosaryTransfer transfer)
        {
            if (transfer.Mode == RosaryMode.Loan || transfer.Refunded || transfer.Outgoing ||
                transfer.State != RosaryTransferState.Accepted) return null;
            transfer.Refunded = true;
            return AddPayment(transfer, transfer.Amount, false, true);
        }

        RosaryPayment AddPayment(RosaryTransfer transfer, int amount, bool interest, bool refund)
        {
            var payment = new RosaryPayment {
                Id = Guid.NewGuid().ToString("N"), Transfer = transfer.Id, Amount = amount, Interest = interest, Refund = refund
            };
            Payments.Add(payment.Id, payment);
            return payment;
        }

        public bool CanReceive(RosaryTransfer row, RosaryPayment payment)
        {
            if (!row.Outgoing || row.State != RosaryTransferState.Accepted || row.Refunded ||
                Receipts.Contains(payment.Id) || payment.Amount <= 0) return false;
            if (payment.Refund) return row.Mode != RosaryMode.Loan && payment.Amount == row.Amount;
            if (row.Mode != RosaryMode.Loan || (payment.Interest && row.Due == 0)) return false;
            // One period of slack: the payer may have crossed a period boundary a moment earlier.
            return payment.Amount <= row.Balance + 3;
        }

        public void Receive(RosaryTransfer row, RosaryPayment payment)
        {
            Receipts.Add(payment.Id);
            if (payment.Refund)
            {
                row.Refunded = true;
                row.Repaid = row.Amount + (row.Overdue ? 3 : 0);
                return;
            }
            if (payment.Interest && row.OverduePeriods == 0) { row.OverduePeriods = 1; row.Overdue = true; }
            row.Repaid += payment.Amount;
        }
    }

    internal sealed class TollPaymentRace
    {
        readonly Dictionary<string, (string Payer, int PaidAt, double SeenAt)> lastByBench =
            new Dictionary<string, (string, int, double)>();
        readonly Dictionary<string, (string Payer, double Time, bool Refund)> answers =
            new Dictionary<string, (string, double, bool)>();

        internal bool TrySettle(string bench, string id, string payer, int paidAt, int wallClock, double now, out bool refund)
        {
            if (answers.TryGetValue(id, out var prior))
            {
                refund = prior.Refund;
                return payer == prior.Payer;
            }
            bool recent = Math.Abs((long)paidAt - wallClock) <= 20;
            refund = recent && lastByBench.TryGetValue(bench, out var first) && first.Payer != payer &&
                Math.Abs((long)first.PaidAt - paidAt) <= 3 && now - first.SeenAt <= 20;
            if (recent && !refund) lastByBench[bench] = (payer, paidAt, now);
            answers[id] = (payer, now, refund);
            if (answers.Count > 256)
            {
                foreach (var entry in answers.Where(pair => now - pair.Value.Time > 120).ToArray()) answers.Remove(entry.Key);
                foreach (var entry in lastByBench.Where(pair => now - pair.Value.SeenAt > 20).ToArray()) lastByBench.Remove(entry.Key);
            }
            return true;
        }

        internal void Clear()
        {
            lastByBench.Clear();
            answers.Clear();
        }
    }
}
