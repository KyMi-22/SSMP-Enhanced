using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Silksong.CoopAssist
{
    // Any ordinary keyboard key may own a teammate action; only overlaps and keys the mod already uses are refused.
    public static class BindingKeyPolicy
    {
        // Backspace..F15 holds every ordinary keyboard key and excludes mouse and joystick buttons.
        public static bool IsBindable(KeyCode key) => key >= KeyCode.Backspace && key <= KeyCode.F15;

        public static bool CanUse(KeyCode key, IEnumerable<KeyCode> reserved) =>
            key != KeyCode.None && key != KeyCode.Escape && IsBindable(key) && !reserved.Contains(key);

        public static bool CanAssign(KeyCode key,int index,KeyCode[] current,KeyCode[] reserved) =>
            CanUse(key,reserved) && !current.Where((_,i)=>i!=index).Contains(key);

        public static KeyCode[] Normalize(KeyCode[] requested, KeyCode[] defaults, KeyCode[] reserved)
        {
            if(requested.Length != defaults.Length)throw new ArgumentException("Binding counts differ");
            var result = new KeyCode[requested.Length];
            // Keep valid existing keys before assigning defaults to migrated bindings.
            for(int i=0;i<result.Length;i++)
                if(CanUse(requested[i],reserved) && !result.Contains(requested[i]))result[i]=requested[i];
            for(int i=0;i<result.Length;i++)
            {
                if(result[i]!=KeyCode.None)continue;
                var preferred=defaults[i];
                result[i]=CanUse(preferred,reserved) && !result.Contains(preferred) ? preferred : FirstFree(result,reserved);
            }
            return result;
        }

        static KeyCode FirstFree(KeyCode[] taken,KeyCode[] reserved)
        {
            // A migrated action should land somewhere usable: F keys first, then letters and digits.
            foreach(var key in PreferredOrder)
                if(CanUse(key,reserved) && !taken.Contains(key))return key;
            throw new InvalidOperationException("No free key remains");
        }

        static readonly KeyCode[] PreferredOrder=Preferred();
        static KeyCode[] Preferred()
        {
            var order=new List<KeyCode>();
            for(int value=(int)KeyCode.F1;value<=(int)KeyCode.F12;value++)order.Add((KeyCode)value);
            for(int value=(int)KeyCode.A;value<=(int)KeyCode.Z;value++)order.Add((KeyCode)value);
            for(int value=(int)KeyCode.Alpha0;value<=(int)KeyCode.Alpha9;value++)order.Add((KeyCode)value);
            for(int value=(int)KeyCode.Backspace;value<=(int)KeyCode.F15;value++)order.Add((KeyCode)value);
            return order.ToArray();
        }
    }

    public static class CocoonViewSelection
    {
        // Self occupies the first slot. Both directions wrap through the same living teammates.
        public static ushort? Move(ushort? current, int direction, IEnumerable<ushort> peers)
        {
            var ordered = peers.Distinct().OrderBy(id => id).ToArray();
            int index = current.HasValue ? Array.IndexOf(ordered, current.Value) + 1 : 0;
            int count = ordered.Length + 1;
            int next = (index + Math.Sign(direction) + count) % count;
            return next == 0 ? (ushort?)null : ordered[next - 1];
        }
    }

    // IDs survive roster reordering. A departed selection never silently changes to another peer.
    public sealed class TravelSelection
    {
        public const int Nearest=-1, Cancel=-2;
        int[] rows=new[]{Nearest,Cancel};
        public int Selected { get; private set; }=Nearest;
        public bool ConfirmationBlocked { get; private set; }
        public IReadOnlyList<int> Rows=>rows;
        public void Refresh(IEnumerable<int> peerIds)
        {
            rows=new[]{Nearest}.Concat(peerIds.Where(id=>id>=0).Distinct().OrderBy(id=>id)).Concat(new[]{Cancel}).ToArray();
            if(!rows.Contains(Selected)){Selected=Nearest;ConfirmationBlocked=true;}
        }
        public void Select(int id)
        {
            if(!rows.Contains(id))return;
            Selected=id;ConfirmationBlocked=false;
        }
        public void Move(int direction)
        {
            if(direction==0)return;
            int index=Array.IndexOf(rows,Selected);
            Selected=rows[(index+Math.Sign(direction)+rows.Length)%rows.Length];
            ConfirmationBlocked=false;
        }
        public static int FindNearest(IEnumerable<(int Id,double DistanceSquared)> candidates) =>
            candidates.Where(p=>p.Id>=0 && p.DistanceSquared>=0 && !double.IsNaN(p.DistanceSquared) && !double.IsInfinity(p.DistanceSquared))
                .OrderBy(p=>p.DistanceSquared).ThenBy(p=>p.Id).Select(p=>p.Id).DefaultIfEmpty(-1).First();
    }
}
