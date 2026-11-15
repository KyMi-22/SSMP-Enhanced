using System;
using System.Collections.Generic;
using Silksong.CoopAssist;

static class PlayerMotionTests
{
    static int checks;
    static void Check(bool value, string why)
    { checks++; if (!value) throw new Exception(why); }
    static void Near(double actual, double expected, double tolerance, string why)
    { Check(Math.Abs(actual - expected) <= tolerance, why + ": " + actual + " vs " + expected); }
    static void Main()
    {
        foreach (var fps in new[] {30,60,90,113,120,144,165,240})
        {
            var cadence = new PositionCadence(); var count = 0;
            for (var i=0; i<fps*10; i++) if(cadence.Due(i/(double)fps)) count++;
            Near(count, Math.Min(fps,60)*10, 1, "sampling stays at render limit or 60 Hz, FPS="+fps);
        }
        var stalled = new PositionCadence();
        Check(stalled.Due(0),"initial sample");
        Check(stalled.Due(.253),"one fresh sample after stall");
        Check(!stalled.Due(.253),"no burst replay after stall");
        Check(stalled.Due(.267),"fractional cadence resumes after stall");
        foreach (var fps in new[] {60,120,144})
        {
            var filter = new PlayerMotionFilter();
            for (var i=0; i<fps*3; i++)
            {
                var t=i/(double)fps;
                if(i%(fps==120?2:fps==144?3:1)==0)filter.Push(t*8,0,t);
                Check(filter.Advance(t,1.0/fps),"running state exists");
                if(t>.5)Near(filter.X,t*8,.20,"constant motion does not trail excessively, FPS="+fps);
            }
            var stopAt=3.0;
            for(var i=0;i<12;i++)
            { var t=stopAt+i/(double)fps;filter.Push(24,0,t);filter.Advance(t,1.0/fps); }
            Near(filter.X,24,.001,"stop settles promptly");
            Near(filter.VelocityX,0,.001,"stopped velocity clears");
        }
        var burst=new PlayerMotionFilter();
        burst.Push(0,0,0);burst.Advance(0,.016);
        burst.Push(.08,0,.02);burst.Push(.12,0,.02);burst.Push(.16,0,.02);
        burst.Advance(.02,.02);
        Check(burst.Merged==2,"bunched positions merge before speed estimation");
        Near(burst.VelocityX,8,.001,"bunched positions do not multiply speed");
        burst.Push(.32,0,.04);burst.Advance(.04,.02);
        burst.Push(.16,0,.06);burst.Advance(.06,.02);
        Check(burst.VelocityX<0,"direction reversal applies immediately");
        burst.Advance(.14,.02);var held=burst.X;
        burst.Advance(.3,.2);Near(burst.X,held,.03,"prediction holds endpoint rather than sliding back");
        burst.Push(50,10,.31,true);burst.Advance(.31,.01);
        Near(burst.X,50,0,"teleport does not chase old position");
        Near(burst.Y,10,0,"teleport vertical axis");
        burst.Reset();Check(!burst.Advance(.32,.01),"pooled character or scripted lift returns native ownership");
        burst.Push(2,3,.33);burst.Advance(.33,.01);Near(burst.X,2,0,"new room starts cleanly");
        burst.Push(double.NaN,0,.34);burst.Advance(.34,.01);Check(!double.IsNaN(burst.X),"invalid input ignored");
        burst.Push(2,3,.35);burst.Advance(.35,.01);
        burst.Push(2.001,3,.37);burst.Advance(.37,.02);
        Check(burst.VelocityX>0,"slow movement is not mistaken for standing still");
        StallRecovery();
        Statistics();
        Diagnostics();
        Allocations();
        foreach(var fps in new[]{60,144})foreach(var seed in new[]{42,93,719})Jitter(fps,seed);
        Console.WriteLine("Passed "+checks+" movement/dispatch checks. Simulation only; not a two-PC game test.");
    }

    static void StallRecovery()
    {
        var current=new PlayerMotionFilter();var previous=new PreviousRelease.PlayerMotionFilter();
        for(var i=0;i<=60;i++)
        {var t=i/60.0;current.Push(8*t,0,t);previous.Push(8*t,0,t);current.Advance(t,1/60.0);previous.Advance(t,1/60.0);}
        for(var i=61;i<73;i++)
        {var t=i/60.0;current.Advance(t,1/60.0);previous.Advance(t,1/60.0);}
        var before=current.X;var beforeOld=previous.X;
        current.Push(9.6,0,1.2);previous.Push(9.6,0,1.2);
        current.Advance(1.2,1/60.0);previous.Advance(1.2,1/60.0);
        Check(Math.Abs(current.X-before)<.001,"200 ms stall does not create an instant visual snap");
        Check(Math.Abs(previous.X-beforeOld)>.8,"baseline trace exercises its old long-gap snap");
        for(var i=1;i<=15;i++)
        {var t=1.2+i/60.0;current.Push(9.6,0,t);current.Advance(t,1/60.0);}
        Near(current.X,9.6,.002,"recovery settles without leaving the character floating");
        current.Push(20,5,1.5,true);current.Advance(1.5,1/60.0);
        Near(current.X,20,0,"explicit teleport discards any recovery offset");
    }

    static void Statistics()
    {
        var f=new PlayerMotionFilter();f.Push(0,0,0);f.Advance(0,.01);
        f.Push(.1,0,.2);f.Advance(.2,.01);
        Near(f.LargestGap,.2,.00001,"gap is measured for the current reporting window");
        Check(f.GapsOver150==1,"a long gap counts once");
        f.ClearStatistics();f.Advance(.21,.01);
        Check(f.LargestGap==0&&f.GapsOver150==0,"idle frames do not re-report the same historical gap");
        f.Push(.1,0,.22);f.Advance(.22,.01);
        Check(f.LargestGap<.03,"next window only sees its new arrivals");
        f.Reset();Check(f.LargestGap==0,"room/lift reset clears reporting state");
    }

    static void Diagnostics()
    {
        var meter=new DispatchDelayMeter();var a=new object();var b=new object();
        meter.Decoded(a,1);meter.Decoded(b,1.01);meter.Applied(b,1.02);meter.Applied(a,1.2);
        var s=meter.ReadAndReset();Check(s.Count==2&&s.Missing==0,"dispatch can match out-of-order application");
        Near(s.Maximum,.2,.000001,"main-thread hold is measured against decode time");
        Near(s.Average,.105,.000001,"mean decode-to-apply delay");
        Check(meter.ReadAndReset().Count==0,"report windows reset");
        meter.Applied(a,1.3);Check(meter.ReadAndReset().Missing==1,"a repeated apply is not counted as a new sample");
        meter.Decoded(a,2);meter.Clear();meter.Applied(a,3);
        Check(meter.ReadAndReset().Missing==1,"disconnect clears stored packet references");
        var packets=new object[513];for(var i=0;i<packets.Length;i++){packets[i]=new object();meter.Decoded(packets[i],i);}
        meter.Applied(packets[0],600);meter.Applied(packets[512],600);s=meter.ReadAndReset();
        Check(s.Count==1&&s.Missing==1,"diagnostics remain bounded when packets are never applied");
    }

    static void Allocations()
    {
        var f=new PlayerMotionFilter();var meter=new DispatchDelayMeter();var packet=new object();
        for(var i=0;i<2000;i++)
        {f.Push(i*.01,0,i*.01);f.Advance(i*.01,.01);meter.Decoded(packet,i);meter.Applied(packet,i+.01);}
        var bytes=GC.GetAllocatedBytesForCurrentThread();
        for(var i=2000;i<12000;i++)
        {f.Push(i*.01,0,i*.01);f.Advance(i*.01,.01);f.ClearStatistics();meter.Decoded(packet,i);meter.Applied(packet,i+.01);}
        bytes=GC.GetAllocatedBytesForCurrentThread()-bytes;
        Check(bytes==0,"movement and bounded diagnostics allocate no managed objects in the warmed pure logic loop");
    }

    struct Delivery { internal double Sent, At; internal int Sequence; }
    static void Jitter(int fps,int seed)
    {
        var random=new Random(seed);var deliveries=new List<Delivery>();
        for(var i=0;i<1800;i++)
        {
            if(i%47==23)continue; // An isolated lost update.
            var sent=i/60.0;var arrival=sent+.010+random.NextDouble()*.024;
            if(i%31==12||i%31==13)arrival=Math.Ceiling(arrival/.04)*.04;
            deliveries.Add(new Delivery{Sent=sent,At=arrival,Sequence=i});
        }
        deliveries.Sort((a,b)=>a.At.CompareTo(b.At));
        var f=new PlayerMotionFilter();var old=new PreviousRelease.PlayerMotionFilter();
        var at=0;var sequence=-1;double x=0,ox=0,error=0,oldError=0;var count=0;
        for(var i=0;i<fps*29;i++)
        {
            var t=i/(double)fps;
            while(at<deliveries.Count&&deliveries[at].At<=t)
            {
                var d=deliveries[at++];if(d.Sequence<=sequence)continue;sequence=d.Sequence;
                // Both filters see identical accepted positions and main-thread timestamps.
                f.Push(d.Sent*8,0,t);old.Push(d.Sent*8,0,t);
            }
            if(!f.Advance(t,1.0/fps)||!old.Advance(t,1.0/fps))continue;
            if(t>.5)
            {
                var e=f.X-x-8.0/fps;var oe=old.X-ox-8.0/fps;
                error+=e*e;oldError+=oe*oe;count++;
                Check(Math.Abs(f.X-8*t)<.8,"bounded visual delay in a jitter trace");
            }
            x=f.X;ox=old.X;
        }
        var rms=Math.Sqrt(error/count);var baseline=Math.Sqrt(oldError/count);
        Console.WriteLine("Trace FPS="+fps+", seed="+seed+": per-frame motion error "+rms.ToString("0.0000")+" vs 0.2.2 "+baseline.ToString("0.0000"));
        Check(rms<baseline*.9,"jitter trace improves motion continuity by at least 10 percent");
    }
}
