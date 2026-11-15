using System;
using SSMP.Networking.Packet;
using Silksong.CoopAssist;

static class CodecTests
{
    static void Main()
    {
        var type = typeof(IPacket).Assembly.GetType("SSMP.Networking.Packet.Packet", true);
        var random = new Random(451);
        for (int i = 0; i < 500; i++)
        {
            var original = new HostAnchor { Session = Guid.NewGuid().ToString("N"), Host = (ushort)random.Next(65536),
                Sequence = i == 499 ? ulong.MaxValue : (ulong)i + 1, Scene = "场景_" + i, Gate = "left1_" + i,
                X = (float)(random.NextDouble() * 2000 - 1000), Y = (float)(random.NextDouble() * 2000 - 1000) };
            var writer = (IPacket)Activator.CreateInstance(type);
            original.WriteData(writer);
            var bytes = (byte[])type.GetMethod("ToArray").Invoke(writer, null);
            var reader = (IPacket)Activator.CreateInstance(type, new object[] { bytes });
            var decoded = new HostAnchor(); decoded.ReadData(reader);
            if (decoded.Session != original.Session || decoded.Sequence != original.Sequence || decoded.Host != original.Host ||
                decoded.Scene != original.Scene || decoded.Gate != original.Gate || decoded.X != original.X || decoded.Y != original.Y)
                throw new Exception("Round trip failed at " + i);
            if (bytes.Length > 1024 || !decoded.IsReliable || !decoded.DropReliableDataIfNewerExists)
                throw new Exception("Packet properties failed");
        }
        Console.WriteLine("Passed 500 anchor round trips using the bundled SSMP packet codec. No sockets or Unity runtime used.");
    }
}
