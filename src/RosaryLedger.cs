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
        // Counts only time both players are online: leaving the room freezes it, returning resumes it.
        // Zero means "not initialised"; older saves are migrated from Due on load.
        public int Remaining;
        public int OverdueElapsed;
        // Kept for builds that still read the old absolute deadline; refreshed whenever the ledger is written.
        public long Due;
        public bool Outgoing, Overdue, Hidden, Refunded, SettleAfterNativeRefund;
        public RosaryTransferState State;
        // One interest period per OverdueSeconds overdue; every further period adds another 3.
        public int Interest => !Overdue ? 0 : RosaryLedger.OverdueInterest * (1 + OverdueElapsed / RosaryLedger.OverdueSeconds);
        public int Balance => Amount + Interest - Repaid;

        // Advances the shared online clock. Returns the interest this tick added; a settled row stays settled.
        internal int Advance(int seconds)
        {
            if (seconds <= 0 || Amount + Interest - Repaid <= 0) return 0;
            var previous = Interest;
            if (Remaining > 0)
            {
                var used = Math.Min(Remaining, seconds);
                Remaining -= used;
                seconds -= used;
            }
            if (Remaining <= 0)
            {
                Overdue = true;
                OverdueElapsed += seconds;
            }
            return Interest - previous;
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
        public const int OverdueInterest = 3;
        public string Owner = Guid.NewGuid().ToString("N");
        public Dictionary<string, RosaryTransfer> Transfers = new Dictionary<string, RosaryTransfer>(StringComparer.Ordinal);
        public Dictionary<string, RosaryPayment> Payments = new Dictionary<string, RosaryPayment>(StringComparer.Ordinal);
        public HashSet<string> Receipts = new HashSet<string>(StringComparer.Ordinal);
        public Dictionary<string, TollPayment> TollPayments = new Dictionary<string, TollPayment>(StringComparer.Ordinal);

        public IEnumerable<RosaryTransfer> Debts => Transfers.Values.Where(row =>
            row.Mode == RosaryMode.Loan && row.State == RosaryTransferState.Accepted);
        public IEnumerable<RosaryTransfer> OpenDebts => Debts.Where(row => !row.Outgoing && row.Balance > 0);
        public bool HasDebts => OpenDebts.Any();

        public IEnumerable<RosaryTransfer> DueDebts() => OpenDebts.Where(row =>
            row.Remaining <= 0 || row.SettleAfterNativeRefund)
            .OrderBy(row => row.Remaining).ThenBy(row => row.Id);

        public RosaryPayment Repay(RosaryTransfer loan, int available)
        {
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
            if (row.Mode != RosaryMode.Loan) return false;
            // Accept whatever the payer handed over; the receiver credits only what it still owes, so an
            // inflated payment cannot mint rosaries here, and a legitimate late payment is never rejected.
            return true;
        }

        public void Receive(RosaryTransfer row, RosaryPayment payment)
        {
            Receipts.Add(payment.Id);
            if (payment.Refund)
            {
                row.Refunded = true;
                row.Repaid = row.Amount + row.Interest;
                return;
            }
            if (payment.Interest) row.Overdue = true;
            row.Repaid = Math.Min(row.Amount + row.Interest, row.Repaid + payment.Amount);
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
