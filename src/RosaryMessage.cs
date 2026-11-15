using System;
using System.Security.Cryptography;
using System.Text;
using SSMP.Networking.Packet;

namespace Silksong.CoopAssist
{
    public enum RosaryKind : byte { Hello, Rules, Ask, Decline, Grant, Accepted, Rejected, Payment, PaymentAck, Refund, BenchPaid, BenchResult }

    public sealed class RosaryMessage : IPacketData
    {
        public RosaryKind Kind;
        public RosaryMode Mode;
        public string From = "", To = "", Id = "", Ref = "", Label = "";
        public ushort Peer;
        public int Amount, At;
        public bool Flag;
        public bool IsReliable => true;
        public bool DropReliableDataIfNewerExists => false;

        public void WriteData(IPacket packet)
        {
            packet.Write((byte)Kind); packet.Write((byte)Mode); packet.Write(From); packet.Write(To);
            packet.Write(Id); packet.Write(Ref); packet.Write(Label); packet.Write(Peer); packet.Write(Amount); packet.Write(At); packet.Write(Flag);
        }

        public void ReadData(IPacket packet)
        {
            Kind = (RosaryKind)packet.ReadByte(); Mode = (RosaryMode)packet.ReadByte();
            From = packet.ReadString(); To = packet.ReadString(); Id = packet.ReadString();
            Ref = packet.ReadString(); Label = packet.ReadString(); Peer = packet.ReadUShort();
            Amount = packet.ReadInt(); At = packet.ReadInt(); Flag = packet.ReadBool();
        }

        internal static bool Key(string value) => Guid.TryParseExact(value, "N", out _);
        internal static string Identity(string slotNonce, string authKey)
        {
            if (!Key(slotNonce) || string.IsNullOrEmpty(authKey)) return "";
            using var hash = new HMACSHA256(Encoding.UTF8.GetBytes(authKey));
            return new Guid(hash.ComputeHash(Encoding.UTF8.GetBytes("CoopAssist rosaries " + slotNonce)).AsSpan(0, 16)).ToString("N");
        }
        public bool Valid => Kind <= RosaryKind.BenchResult && Mode <= RosaryMode.Bench &&
            Label != null && Label.Length <= 128 && Ref != null && Ref.Length <= 128 &&
            Amount >= 0 && Amount <= RosaryLedger.MaxAmount &&
            (Kind == RosaryKind.Rules || Kind == RosaryKind.BenchResult || Key(From)) &&
            (Kind != RosaryKind.Hello || Id == "" || Key(Id)) &&
            (Kind <= RosaryKind.Rules || (Key(To) && Key(Id))) &&
            (Kind != RosaryKind.Payment && Kind != RosaryKind.PaymentAck && Kind != RosaryKind.Refund || Key(Ref)) &&
            (Kind != RosaryKind.Ask && Kind != RosaryKind.Grant && Kind != RosaryKind.Payment && Kind != RosaryKind.Refund || Amount > 0) &&
            (Kind != RosaryKind.BenchPaid || (Key(Label) && At > 0));
    }
}
