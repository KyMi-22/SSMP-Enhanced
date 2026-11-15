using System;
using System.Threading;

namespace Silksong.CoopAssist
{
    // A timeout never hands the array to a consumer. Successful payloads retain exclusive
    // ownership, including when a receiver queues their byte[] for later processing.
    public static class ReceiveScratch
    {
        [ThreadStatic] static byte[] pending;
        static long created,reused,handedOff;
        public static byte[] Buffer(int length)
        {
            if(pending==null||pending.Length!=length){pending=new byte[length];Interlocked.Increment(ref created);}
            else Interlocked.Increment(ref reused);
            return pending;
        }
        public static int Received(int length)
        {if(length>=0){pending=null;Interlocked.Increment(ref handedOff);}return length;}
        public readonly struct Summary
        {
            public readonly long Created,Reused,HandedOff;
            internal Summary(long c,long r,long h){Created=c;Reused=r;HandedOff=h;}
        }
        public static Summary ReadAndReset()=>new Summary(Interlocked.Exchange(ref created,0),Interlocked.Exchange(ref reused,0),Interlocked.Exchange(ref handedOff,0));
    }
}
