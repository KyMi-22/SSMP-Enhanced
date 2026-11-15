using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using SSMP.Networking.Packet;
using SSMP.Networking.Packet.Data;
using Silksong.CoopAssist;

static class AppearanceEnvelopeTests
{
    static readonly Assembly Native=typeof(IPacket).Assembly;
    static readonly Type Packet=Native.GetType("SSMP.Networking.Packet.Packet",true);
    static readonly Type Addon=Native.GetType("SSMP.Networking.Packet.AddonPacketData",true);
    static readonly Type Info=Native.GetType("SSMP.Networking.Packet.AddonPacketInfo",true);
    static int checks;
    static void Check(bool value,string why){if(!value)throw new Exception(why);checks++;}
    static MethodInfo Method(Type t,string name)
    {for(;t!=null;t=t.BaseType){var m=t.GetMethod(name,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static|BindingFlags.DeclaredOnly);if(m!=null)return m;}throw new Exception(name);}
    static Type Shell(string name)=>Native.GetType("SSMP.Networking.Packet.Update."+name,true);
    static IPacketData ThroughEnvelope(string type,IPacketData data,Func<byte,IPacketData> factory,bool includeAnchor=false,AssistPacket kind=AssistPacket.Appearance)
    {
        var shell=Activator.CreateInstance(Shell(type));
        var count=(byte)Enum.GetValues(typeof(AssistPacket)).Length;
        var addon=Activator.CreateInstance(Addon,new object[]{count});
        var records=(IDictionary)Addon.GetProperty("PacketData").GetValue(addon);
        records.Add((byte)kind,data);
        if(includeAnchor)records.Add((byte)0,new HostAnchor{Session="abcd",Sequence=8,Host=0,Scene="Bone_11",Gate="left1",X=2,Y=3});
        var dictionaryType=typeof(System.Collections.Generic.Dictionary<,>).MakeGenericType(typeof(byte),Addon);
        var input=(IDictionary)Activator.CreateInstance(dictionaryType);input.Add((byte)0,addon);
        var writer=(IPacket)Activator.CreateInstance(Packet);
        Method(shell.GetType(),"WriteAddonDataDict").Invoke(shell,new object[]{writer,input});
        var bytes=(byte[])Packet.GetMethod("ToArray").Invoke(writer,null);
        var reader=(IPacket)Activator.CreateInstance(Packet,new object[]{bytes});
        var infoTable=(IDictionary)Method(shell.GetType(),"get_AddonPacketInfoDict").Invoke(null,null);
        infoTable[(byte)0]=Activator.CreateInstance(Info,new object[]{factory,count});
        var output=(IDictionary)Activator.CreateInstance(dictionaryType);
        Method(shell.GetType(),"ReadAddonDataDict").Invoke(shell,new object[]{reader,output});
        var result=(IDictionary)Addon.GetProperty("PacketData").GetValue(output[(byte)0]);
        if(includeAnchor)Check(((HostAnchor)result[(byte)0]).Scene=="Bone_11","anchor survives colour relay");
        return (IPacketData)result[(byte)kind];
    }
    static void Main()
    {
        Check(!AppearanceWire.CanSend(true,null),"transport connected before addon auth is not ready");
        Check(AppearanceWire.CanSend(true,(byte)0),"addon ID zero is valid");
        Check(!AppearanceWire.CanSend(false,(byte)0),"disconnected sender is blocked");
        var original=new Appearance{Id=65535,Colour="00FF11FF"};
        var received=(Appearance)ThroughEnvelope("ServerUpdatePacket",original,id=>AppearanceWire.ServerData((AssistPacket)id));
        Check(received.Colour==original.Colour,"client to server single envelope");
        var ledger=new AppearanceLedger();
        var forwarded=ledger.Publish(0,received.Colour);
        Check(forwarded.Id==0,"authenticated sender replaces claimed ID");
        var batch=new PacketDataCollection<Appearance>();batch.DataInstances.Add(forwarded);
        var old=(Appearance)ThroughEnvelope("ClientUpdatePacket",batch,id=>id==0?(IPacketData)new HostAnchor():new Appearance());
        Check(!Appearance.Valid(old.Colour),"reproduce 0.2.0 collection/single decoder mismatch");
        for(ushort n=0;n<100;n++)
        {
            var both=new PacketDataCollection<Appearance>();
            both.DataInstances.Add(ledger.Publish(0,"00FF11FF"));both.DataInstances.Add(ledger.Publish(1,"FF3300FF"));
            var decoded=(RawPacketDataCollection)ThroughEnvelope("ClientUpdatePacket",both,id=>AppearanceWire.ClientData((AssistPacket)id),true);
            var rows=decoded.DataInstances.Cast<Appearance>().ToArray();
            Check(rows.Length==2&&rows[0].Id==0&&rows[1].Id==1,"multiple players remain distinct");
            Check(rows[0].Colour=="00FF11FF"&&rows[1].Colour=="FF3300FF","server to clients colour batch");
        }
        Check(ledger.Snapshot().Length==2,"late join snapshot");
        ledger.Remove(1);Check(ledger.Snapshot().Length==1,"disconnect removes stale identity");
        ledger.Clear();Check(ledger.Snapshot().Length==0,"room shutdown clears colours");
        var styles=new NameStyleLedger();
        for(int i=0;i<20;i++)
        {
            var sent=new NameStyleMessage{Id=65535,Flags=(byte)(i%2)};
            var atServer=(NameStyleMessage)ThroughEnvelope("ServerUpdatePacket",sent,id=>AppearanceWire.ServerData((AssistPacket)id),false,AssistPacket.NameStyle);
            Check(atServer.Valid && atServer.Bold==(i%2==1),"name style client-to-server codec");
            var authenticated=styles.Publish(0,atServer.Flags);
            Check(authenticated.Id==0,"style cannot claim another player's ID");
            var rows=new PacketDataCollection<NameStyleMessage>();
            rows.DataInstances.Add(authenticated);rows.DataInstances.Add(styles.Publish(1,(byte)(1-atServer.Flags)));
            var atClient=(RawPacketDataCollection)ThroughEnvelope("ClientUpdatePacket",rows,id=>AppearanceWire.ClientData((AssistPacket)id),true,AssistPacket.NameStyle);
            var decoded=atClient.DataInstances.Cast<NameStyleMessage>().ToArray();
            Check(decoded.Length==2 && decoded[0].Id==0 && decoded[1].Id==1,"style collection preserves separate peers");
            Check(decoded[0].Bold!=decoded[1].Bold,"peer bold settings remain independent");
        }
        Check(styles.Snapshot().Length==2,"late join replays current name styles");
        var copied=styles.Snapshot();copied[0].Flags=255;
        Check(styles.Snapshot().All(row=>row.Valid),"replay snapshot cannot mutate stored styles");
        for(int flags=2;flags<256;flags++)
        {
            Check(!new NameStyleMessage{Flags=(byte)flags}.Valid,"reject unsupported style bits");
            bool rejected=false;try{styles.Publish(2,(byte)flags);}catch(ArgumentException){rejected=true;}
            Check(rejected,"ledger rejects invalid styles before relay");
        }
        styles.Remove(1);Check(styles.Snapshot().Length==1,"disconnect clears style identity");
        styles.Clear();Check(styles.Snapshot().Length==0,"shutdown clears styles");
        var mapState=new SharedMapProgress{Flags=17,Quill=2};
        for(int i=0;i<80;i++)mapState.Rooms[1].Add("Mapped_Room_"+i);
        var mapToken=Guid.NewGuid().ToString("N");var mapEpoch=Guid.NewGuid().ToString("N");
        foreach(var direction in new[]{"ServerUpdatePacket","ClientUpdatePacket"})
        {
            var mapBatch=new PacketDataCollection<SharedMapMessage>();
            foreach(var message in SharedMapMessage.Batches(SharedMapKind.State,mapToken,mapEpoch,mapState))mapBatch.DataInstances.Add(message);
            var mapped=(RawPacketDataCollection)ThroughEnvelope(direction,mapBatch,
                id=>direction=="ServerUpdatePacket"?AppearanceWire.ServerData((AssistPacket)id):AppearanceWire.ClientData((AssistPacket)id),true,AssistPacket.SharedMaps);
            var restored=new SharedMapProgress();
            foreach(var message in mapped.DataInstances.Cast<SharedMapMessage>())
            {Check(message.Token==mapToken && message.Epoch==mapEpoch,"map nonce and epoch survive native envelope");restored.Merge(message.Progress());}
            Check(restored.Same(mapState),"all map batches survive native addon framing in both directions");
        }
        TestRosaries();
        Console.WriteLine("Passed "+checks+" native addon-envelope and rosary ledger checks. No sockets or Unity.");
    }

    static void TestRosaries()
    {
        var debtor = new RosaryLedger();
        var lender = new RosaryLedger();
        var id = Guid.NewGuid().ToString("N");
        var amount = 40;
        var due = 1000 + RosaryLedger.OverdueSeconds;
        var debt = new RosaryTransfer { Id = id, Amount = amount, Due = due, Mode = RosaryMode.Loan,
            State = RosaryTransferState.Accepted, Peer = lender.Owner };
        var credit = new RosaryTransfer { Id = id, Amount = amount, Due = due, Mode = RosaryMode.Loan,
            State = RosaryTransferState.Accepted, Peer = debtor.Owner, Outgoing = true };
        debtor.Transfers.Add(id, debt);
        lender.Transfers.Add(id, credit);
        var beforeDue = debtor.Repay(debt, 12, due - 1, out var late);
        Check(!late && beforeDue.Amount == 12 && !beforeDue.Interest, "early manual payment carries no interest");
        var onDue = debtor.Repay(debt, 10, due, out late);
        Check(late && onDue.Interest && debt.Balance == 21, "single 3-rosary interest after 528 seconds");
        Check(lender.CanReceive(credit, onDue), "can receive overdue payment before older payment");
        lender.Receive(credit, onDue);
        Check(!lender.CanReceive(credit, onDue), "duplicate payment is idempotent");
        Check(lender.CanReceive(credit, beforeDue), "later receipt accepts earlier principal payment");
        lender.Receive(credit, beforeDue);
        var remainder = debtor.Repay(debt, 99, due + 60, out late);
        Check(!late && remainder.Amount == 21 && debt.Balance == 0, "remaining debt drains only its balance");
        lender.Receive(credit, remainder);
        Check(credit.Balance == 0 && credit.Repaid == 43, "both ends agree on principal and interest");
        Check(debtor.Refund(debt) == null, "native duplicate refund does not cancel previous repayments");

        var bench = new RosaryTransfer { Id = Guid.NewGuid().ToString("N"), Amount = 15, Mode = RosaryMode.Bench,
            State = RosaryTransferState.Accepted };
        var refund = debtor.Refund(bench);
        Check(refund.Amount == 15 && refund.Refund && debtor.Refund(bench) == null, "bench refunds exact contribution once");
        Check(!debtor.Debts.Contains(bench), "bench never appears as loan");
        var race = new TollPaymentRace();
        Check(race.TrySettle("bench", "first", "a", 100, 100, 0, out bool refundFirst) && !refundFirst,
            "first bench payment wins");
        Check(race.TrySettle("bench", "second", "b", 102, 102, 2, out bool refundSecond) && refundSecond,
            "crossed payment is refunded");
        Check(race.TrySettle("bench", "second", "b", 102, 300, 200, out bool refundRetry) && refundRetry,
            "duplicate report retains its refund result");
        Check(race.TrySettle("bench", "old", "c", 80, 300, 200, out bool refundOld) && !refundOld,
            "reconnect does not group a stale payment with a new bench rental");

        var ask = new RosaryMessage { Kind = RosaryKind.Ask, Mode = RosaryMode.Loan, From = debtor.Owner,
            To = lender.Owner, Id = id, Amount = amount, Label = "商店", Ref = "圣堡" };
        var payment = new RosaryMessage { Kind = RosaryKind.Payment, Mode = RosaryMode.Loan, From = debtor.Owner,
            To = lender.Owner, Id = onDue.Id, Ref = id, Amount = onDue.Amount, Flag = true };
        foreach (var direction in new[] {"ServerUpdatePacket", "ClientUpdatePacket"})
        {
            var batch = new PacketDataCollection<RosaryMessage>();
            batch.DataInstances.Add(ask);
            batch.DataInstances.Add(payment);
            var decoded = (RawPacketDataCollection)ThroughEnvelope(direction, batch,
                packetId => direction == "ServerUpdatePacket" ? AppearanceWire.ServerData((AssistPacket)packetId) :
                    AppearanceWire.ClientData((AssistPacket)packetId), true, AssistPacket.Rosaries);
            var rows = decoded.DataInstances.Cast<RosaryMessage>().ToArray();
            Check(rows.Length == 2 && rows.All(row => row.Valid), "same-frame economy messages survive native collection");
            Check(rows[0].Amount == amount && rows[0].Label == "商店" && rows[0].Ref == "圣堡" &&
                rows[1].Flag && rows[1].Ref == id,
                "approval and repayment identity survive the wire");
        }
        ask.Amount = -1;
        Check(!ask.Valid, "invalid negative amount rejected");
        var owner = RosaryMessage.Identity(id, "original-auth-key");
        Check(owner == RosaryMessage.Identity(id, "original-auth-key") &&
            owner != RosaryMessage.Identity(id, "another-auth-key"), "save identity stable across reconnect, bound to auth key");
    }

}
