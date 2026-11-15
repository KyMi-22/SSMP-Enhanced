using System;
using System.Collections.Generic;

namespace Silksong.CoopAssist
{
    public enum FlowStage { Sample, ClientSend, ServerSend, ClientReceive, ClientApply, HostReceive, HostApply }

    // Counts calls at local boundaries. Sequence gaps are deliberately not interpreted as packet loss.
    public sealed class PeerFlowMetrics
    {
        const int Capacity = 192;
        readonly object gate = new object();
        readonly Dictionary<long, Slot> slots = new Dictionary<long, Slot>();
        internal sealed class Slot
        {
            internal FlowStage Stage;
            internal int Peer;
            internal bool Started, Numbered, Visible, SignalStarted;
            internal ushort Sequence;
            internal double Last, SignalLast, Peak, PeakUntil, Gap, DelayTotal, DelayMaximum;
            internal long Count, Over50, Over150, Delays, Older, Duplicate;
        }
        public readonly struct Summary
        {
            public readonly FlowStage Stage;
            public readonly int Peer;
            public readonly long Count, Over50, Over150, Older, Duplicate, Delays;
            public readonly double MaximumGap, AverageDelay, MaximumDelay;
            internal Summary(Slot s)
            {
                Stage=s.Stage; Peer=s.Peer; Count=s.Count; Over50=s.Over50; Over150=s.Over150;
                Older=s.Older; Duplicate=s.Duplicate; Delays=s.Delays; MaximumGap=s.Gap;
                AverageDelay=s.Delays==0?0:s.DelayTotal/s.Delays; MaximumDelay=s.DelayMaximum;
            }
        }
        public void Observe(FlowStage stage, int peer, double now, double delay=-1, bool numbered=false, ushort sequence=0)
        {
            if (double.IsNaN(now) || double.IsInfinity(now)) return;
            var key=((long)stage<<32)|(uint)(peer+1);
            lock(gate)
            {
                if(!slots.TryGetValue(key,out var s))
                {
                    if(slots.Count>=Capacity)return;
                    s=new Slot{Stage=stage,Peer=peer};slots.Add(key,s);
                }
                if(s.Started && now<s.Last)return;
                var gap=s.Started?now-s.Last:0;
                s.Started=true;s.Last=now;s.Count++;
                s.Gap=Math.Max(s.Gap,gap);
                if(gap>.050)s.Over50++;
                if(gap>.150)s.Over150++;
                if(stage==FlowStage.ClientReceive)
                {
                    var signalGap=s.SignalStarted?Math.Max(0,now-s.SignalLast):0;
                    s.SignalStarted=true;s.SignalLast=now;
                    if(now>s.PeakUntil)s.Peak=0;
                    if(signalGap>s.Peak){s.Peak=signalGap;s.PeakUntil=now+3;}
                }
                if(delay>=0 && !double.IsInfinity(delay))
                {s.Delays++;s.DelayTotal+=delay;s.DelayMaximum=Math.Max(s.DelayMaximum,delay);}
                if(numbered)
                {
                    var difference=unchecked((short)(sequence-s.Sequence));
                    if(s.Numbered && difference<0)s.Older++;
                    else if(s.Numbered && difference==0)s.Duplicate++;
                    if(!s.Numbered || difference>0)s.Sequence=sequence;
                    s.Numbered=true;
                }
            }
        }
        public Summary[] ReadAndReset()
        {
            lock(gate)
            {
                var result=new List<Summary>();
                foreach(var s in slots.Values)
                {
                    if(s.Count==0)continue;
                    result.Add(new Summary(s));
                    s.Count=s.Over50=s.Over150=s.Older=s.Duplicate=s.Delays=0;
                    s.Gap=s.DelayTotal=s.DelayMaximum=0;
                }
                return result.ToArray();
            }
        }
        public void BeginVisibility()
        {lock(gate)foreach(var s in slots.Values)if(s.Stage==FlowStage.ClientReceive)s.Visible=false;}
        public void MarkVisible(int peer)
        {lock(gate)if(slots.TryGetValue(((long)FlowStage.ClientReceive<<32)|(uint)(peer+1),out var s))s.Visible=true;}
        public double RecentVisibleGap(double now)
        {
            lock(gate)
            {
                var result=-1.0;
                foreach(var s in slots.Values)
                    if(s.Stage==FlowStage.ClientReceive && s.Visible && s.SignalStarted)
                        result=Math.Max(result,Math.Max(Math.Max(0,now-s.SignalLast),now<=s.PeakUntil?s.Peak:0));
                return result;
            }
        }
        public void Clear(){lock(gate)slots.Clear();}
        public void ResetSignal(int peer=-1)
        {
            lock(gate)foreach(var s in slots.Values)
                if(s.Stage==FlowStage.ClientReceive&&(peer<0||s.Peer==peer))
                {s.SignalStarted=false;s.SignalLast=s.Peak=s.PeakUntil=0;}
        }
        public void Forget(int peer)
        {lock(gate)for(var stage=0;stage<=(int)FlowStage.HostApply;stage++)slots.Remove(((long)stage<<32)|(uint)(peer+1));}
    }

    public sealed class SignalPolicy
    {
        int level, pending;
        double improveAt;
        public void Reset(){level=pending=0;improveAt=0;}
        public int Update(int ping, double visibleGap, double now)
        {
            var wanted=ping<0?0:ping<80?4:ping<160?3:ping<300?2:1;
            if(visibleGap>=.350)wanted=Math.Min(wanted,1);
            else if(visibleGap>=.150)wanted=Math.Min(wanted,2);
            else if(visibleGap>=.070)wanted=Math.Min(wanted,3);
            // Report deterioration promptly; require a stable recovery before lighting more bars.
            if(level==0 || wanted<=level){level=wanted;pending=wanted;improveAt=now+2;}
            else if(wanted!=pending){pending=wanted;improveAt=now+2;}
            else if(now>=improveAt)level=wanted;
            return level;
        }
    }
}
