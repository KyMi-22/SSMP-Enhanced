using System;
using System.Collections.Generic;

namespace Silksong.CoopAssist
{
    // The latest result remains in settings; a short-lived copy also covers hotkeys and closed menus.
    public sealed class AssistFeedback
    {
        public sealed class Notice
        {
            public readonly string Message;
            public readonly double ShownAt;
            internal Notice(string message,double now){Message=message;ShownAt=now;}
            public bool IsRecent(double now)=>now>=ShownAt && now-ShownAt<12;
        }
        readonly List<Notice> notices=new List<Notice>(3);
        public IReadOnlyList<Notice> Notices=>notices;
        public const string IdleMessage = "操作结果会显示在这里。";
        public string Message { get; private set; } = IdleMessage;
        public double ShownAt { get; private set; } = double.NegativeInfinity;
        public event Action<string> Changed;

        public void Publish(string message, double now)
        {
            if(string.IsNullOrWhiteSpace(message))return;
            Message = message;
            ShownAt = now;
            notices.RemoveAll(notice=>!notice.IsRecent(now));
            if(notices.Count>0 && notices[notices.Count-1].Message==message)notices.RemoveAt(notices.Count-1);
            notices.Add(new Notice(message,now));
            if(notices.Count>3)notices.RemoveAt(0);
            Changed?.Invoke(message);
        }

        public bool IsRecent(double now) => now >= ShownAt && now - ShownAt < 12;
    }
}
