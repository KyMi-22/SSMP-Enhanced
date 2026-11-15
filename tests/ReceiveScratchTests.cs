using System;
using System.Threading;
using Silksong.CoopAssist;

static class ReceiveScratchTests
{
    static int checks;
    static void Check(bool result,string why){checks++;if(!result)throw new Exception(why);}
    static void Main()
    {
        ReceiveScratch.Received(0);
        var first=ReceiveScratch.Buffer(1400);first[0]=19;
        for(var i=0;i<1000;i++)
        {Check(ReceiveScratch.Received(-1)==-1,"timeout value preserved");Check(ReferenceEquals(first,ReceiveScratch.Buffer(1400)),"unused timeout array is reused");}
        Check(ReceiveScratch.Received(12)==12,"successful length preserved");
        var second=ReceiveScratch.Buffer(1400);
        Check(!ReferenceEquals(first,second),"a successful payload is never reused by the next receive");
        second[0]=72;Check(first[0]==19,"a delayed consumer keeps exclusive packet contents");
        ReceiveScratch.Received(-1);Check(ReferenceEquals(second,ReceiveScratch.Buffer(1400)),"only unconsumed buffers are reusable");
        ReceiveScratch.Received(0);Check(!ReferenceEquals(second,ReceiveScratch.Buffer(1400)),"zero-length callback also transfers ownership");
        Check(ReceiveScratch.Buffer(2048).Length==2048,"changed requested length cannot reuse undersized storage");
        var arrays=new byte[4][];var threads=new Thread[4];var ready=new Barrier(4);
        for(var i=0;i<4;i++){var index=i;threads[i]=new Thread(()=>{arrays[index]=ReceiveScratch.Buffer(1400);ready.SignalAndWait();arrays[index][0]=(byte)index;});threads[i].Start();}
        foreach(var t in threads)t.Join();ready.Dispose();
        for(var i=0;i<4;i++)for(var j=i+1;j<4;j++)Check(!ReferenceEquals(arrays[i],arrays[j]),"receive threads cannot share writable buffers");
        for(var i=0;i<4;i++)Check(arrays[i][0]==i,"thread-local contents isolated");
        ReceiveScratch.Received(0);ReceiveScratch.Buffer(1400);ReceiveScratch.Received(-1);
        var allocated=GC.GetAllocatedBytesForCurrentThread();
        for(var i=0;i<10000;i++){ReceiveScratch.Buffer(1400);ReceiveScratch.Received(-1);}
        Check(GC.GetAllocatedBytesForCurrentThread()==allocated,"warm timeout loop allocates no arrays");
        Console.WriteLine("Passed "+checks+" receive-buffer ownership/timeout/concurrency checks. No live network executed.");
    }
}
