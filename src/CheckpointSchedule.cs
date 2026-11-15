using System;

namespace Silksong.CoopAssist
{
    // One writer, no save queue buildup. Failed writes retry; a completion from an old
    // profile/session can never acknowledge the current session's checkpoint.
    public sealed class CheckpointSchedule
    {
        public bool Saving { get; private set; }
        int generation;
        double due;
        bool requestedDuringSave;
        public void Request()
        {
            if (Saving) requestedDuringSave = true;
            else due = 0;
        }
        public void Reset(){generation++;Saving=false;due=0;requestedDuringSave=false;}
        public bool TryBegin(double now,bool safe,out int ticket)
        {
            ticket=generation;
            if(!safe || Saving || now<due)return false;
            Saving=true;return true;
        }
        public void Complete(int ticket,double now,bool success)
        {
            if(ticket!=generation || !Saving)return;
            Saving=false;
            if (!success) due = now + 5;
            else due = requestedDuringSave ? now : now + 30;
            requestedDuringSave=false;
        }
    }
}
