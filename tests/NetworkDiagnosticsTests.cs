using System;
using System.Threading.Tasks;
using Silksong.CoopAssist;

static class NetworkDiagnosticsTests
{
    static int checks;
    static void Check(bool value,string why){checks++;if(!value)throw new Exception(why);}
    static void Near(double a,double b,string why)=>Check(Math.Abs(a-b)<.000001,why);
    static void Main()
    {
        var m=new PeerFlowMetrics();
        m.Observe(FlowStage.ClientReceive,1,0,numbered:true,sequence:65535);
        m.Observe(FlowStage.ClientReceive,1,.02,numbered:true,sequence:0);
        m.Observe(FlowStage.ClientReceive,1,.04,numbered:true,sequence:0);
        m.Observe(FlowStage.ClientReceive,1,.24,numbered:true,sequence:65534);
        m.Observe(FlowStage.ClientReceive,2,.1);
        m.Observe(FlowStage.ClientReceive,2,.12);
        m.Observe(FlowStage.HostApply,1,.3,.06);
        m.Observe(FlowStage.HostApply,1,.4,.02);
        var rows=m.ReadAndReset();Check(rows.Length==3,"directions and peers have separate windows");
        foreach(var s in rows)
        {
            if(s.Stage==FlowStage.ClientReceive && s.Peer==1)
            {Check(s.Count==4,"all observations counted");Check(s.Older==1 && s.Duplicate==1,"wrap is newer; reordered/duplicate are distinguished");Near(s.MaximumGap,.2,"largest arrival gap");Check(s.Over150==1,"long gap counted once");}
            if(s.Stage==FlowStage.ClientReceive && s.Peer==2)Near(s.MaximumGap,.02,"another peer does not inherit gap");
            if(s.Stage==FlowStage.HostApply){Near(s.AverageDelay,.04,"host dispatch mean");Near(s.MaximumDelay,.06,"host dispatch maximum");}
        }
        Check(m.ReadAndReset().Length==0,"idle reporting does not repeat old counts");
        m.Observe(FlowStage.ClientReceive,1,.26,numbered:true,sequence:1);
        rows=m.ReadAndReset();Near(rows[0].MaximumGap,.02,"new window preserves continuity without historic maximum");
        Check(rows[0].Older==0 && rows[0].Duplicate==0,"new sequence follows wrapped last accepted sequence");
        Near(m.RecentVisibleGap(.27),-1,"different rooms have no staleness penalty");
        m.MarkVisible(1);Near(m.RecentVisibleGap(.27),.2,"recent jitter remains visible across report reset");
        Near(m.RecentVisibleGap(.8),.54,"a visible peer that stops updating degrades signal");
        m.BeginVisibility();Near(m.RecentVisibleGap(.8),-1,"leaving room removes staleness penalty");
        m.ResetSignal(1);m.MarkVisible(1);Near(m.RecentVisibleGap(10),-1,"returning peer waits for a new sample instead of treating another room as packet loss");
        m.Observe(FlowStage.ClientReceive,1,10);Near(m.RecentVisibleGap(10),0,"first sample after scene reset has no inherited gap");
        rows=m.ReadAndReset();Check(rows.Length==1&&rows[0].MaximumGap>9,"scene signal reset retains real diagnostic timing");
        m.ResetSignal(2);m.Observe(FlowStage.ClientReceive,2,10);m.MarkVisible(2);m.Observe(FlowStage.ClientReceive,2,10.4);
        m.ResetSignal(1);Near(m.RecentVisibleGap(10.4),.4,"one peer's scene reset must not hide another peer's interruption");
        m.ResetSignal();Near(m.RecentVisibleGap(11),-1,"local loading clears only signal sampling");m.ReadAndReset();
        m.Forget(1);m.Observe(FlowStage.ClientReceive,1,2,numbered:true,sequence:0);
        rows=m.ReadAndReset();Check(rows.Length==1 && rows[0].Older==0 && rows[0].MaximumGap==0,"reused peer ID starts with fresh numbering and timing");
        m.Clear();Check(m.ReadAndReset().Length==0,"disconnect drops historical peers");
        m.Observe(FlowStage.ClientReceive,1,4);m.MarkVisible(1);
        m.Observe(FlowStage.ClientReceive,1,3);Near(m.RecentVisibleGap(4),0,"backwards clock input does not corrupt last arrival");
        m.Observe(FlowStage.ClientReceive,1,double.NaN);Check(m.ReadAndReset()[0].Count==1,"invalid timestamp ignored");
        m.Clear();Parallel.For(0,4,i=>{for(var n=0;n<5000;n++)m.Observe(FlowStage.HostApply,i,n*.01,.001);});
        rows=m.ReadAndReset();long total=0;foreach(var row in rows)total+=row.Count;Check(total==20000,"concurrent directions do not lose counters");
        m.Clear();for(var i=0;i<1000;i++)m.Observe(FlowStage.ClientReceive,i,1);Check(m.ReadAndReset().Length==192,"untrusted peer IDs cannot grow diagnostics without bound");
        var signal=new SignalPolicy();Check(signal.Update(10,-1,0)==4,"low ping initially lights four bars");
        Check(signal.Update(10,.6,.5)==1,"long position interruption overrides low average ping");
        Check(signal.Update(10,0,1)==1,"recovery starts without immediate flicker");
        Check(signal.Update(10,0,2)==1,"recovery needs sustained good updates");
        Check(signal.Update(10,0,3)==4,"stable recovery lights bars again");
        Check(signal.Update(200,-1,3.5)==2,"high round trip degrades even with no local peer");
        Check(signal.Update(-1,-1,4)==0,"disconnected state is unknown, not good signal");
        signal=new SignalPolicy();Check(signal.Update(0,-1,0)==4,"host loopback zero is a valid ping");
        Check(signal.Update(0,.08,.5)==3,"moderate jitter affects host signal too");
        Check(signal.Update(0,.2,1)==2,"medium interruption lights two bars");
        signal.Reset();Check(signal.Update(10,-1,5)==4,"a completed load starts a fresh signal state");
        m.Clear();var packet=new object();var meter=new DispatchDelayMeter();
        for(var i=0;i<3000;i++){m.Observe(FlowStage.ClientReceive,1,i*.01);m.MarkVisible(1);m.RecentVisibleGap(i*.01);meter.Decoded(packet,i);meter.Applied(packet,i+.01);}
        var allocated=GC.GetAllocatedBytesForCurrentThread();
        for(var i=3000;i<13000;i++){m.Observe(FlowStage.ClientReceive,1,i*.01);m.BeginVisibility();m.MarkVisible(1);m.RecentVisibleGap(i*.01);meter.Decoded(packet,i);meter.Applied(packet,i+.01);}
        Check(GC.GetAllocatedBytesForCurrentThread()==allocated,"warmed metrics, visibility and dispatch add no per-update allocations");
        Console.WriteLine("Passed "+checks+" network metrics and signal checks. Pure simulation, not multiplayer runtime validation.");
    }
}
