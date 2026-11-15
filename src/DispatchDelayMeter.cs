using System;

namespace Silksong.CoopAssist
{
    // Bounded, allocation-free after construction. Only matching decoded objects are measured;
    // no cross-computer clock subtraction and no inference about wire latency or packet loss.
    public sealed class DispatchDelayMeter
    {
        readonly object gate = new object();
        readonly object[] objects = new object[512];
        readonly double[] times = new double[512];
        int next;
        long count, missing;
        double total, maximum;
        public void Decoded(object data, double now)
        {
            if (data == null) return;
            lock (gate)
            {
                objects[next] = data; times[next] = now;
                next = (next + 1) % objects.Length;
            }
        }
        public double Applied(object data, double now)
        {
            if (data == null) return -1;
            lock (gate)
            {
                for (var n = 1; n <= objects.Length; n++)
                {
                    var index = (next - n + objects.Length) % objects.Length;
                    if (!ReferenceEquals(objects[index], data)) continue;
                    var delay = Math.Max(0, now - times[index]); objects[index] = null;
                    count++; total += delay; maximum = Math.Max(maximum, delay); return delay;
                }
                missing++; return -1;
            }
        }
        public readonly struct Summary
        {
            public readonly long Count, Missing;
            public readonly double Average, Maximum;
            internal Summary(long count, long missing, double total, double maximum)
            { Count=count; Missing=missing; Average=count==0?0:total/count; Maximum=maximum; }
        }
        public Summary ReadAndReset()
        {
            lock (gate)
            {
                var result = new Summary(count, missing, total, maximum);
                count = missing = 0; total = maximum = 0; return result;
            }
        }
        public void Clear()
        {
            lock (gate)
            { Array.Clear(objects,0,objects.Length);next=0;count=missing=0;total=maximum=0; }
        }
    }
}
