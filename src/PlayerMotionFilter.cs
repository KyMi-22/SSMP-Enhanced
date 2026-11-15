using System;

namespace Silksong.CoopAssist
{
    // Keep the fractional part of a tick. Restarting an integer millisecond stopwatch at each
    // sample turns 60 rendered frames into only 30 position samples on a steady 60 FPS game.
    public sealed class PositionCadence
    {
        const double Period = 1.0 / 60.0;
        double next;
        bool started;
        public bool Due(double now)
        {
            if (!started) { started = true; next = now; }
            if (now + 0.0000001 < next) return false;
            next += (Math.Floor(Math.Max(0, now - next) / Period + 0.000001) + 1) * Period;
            return true; // A delayed frame samples once, without replaying missed ticks.
        }
    }

    // A small receive-time buffer bridges uneven delivery without queuing several frames of
    // input. These are local application timestamps, not sender timestamps or measured RTT.
    // The fixed ring and the pending slot do not allocate during play.
    public sealed class PlayerMotionFilter
    {
        public const double BufferSeconds = .020;
        public const double PredictionSeconds = .050;
        struct Sample { internal double X, Y, At; }
        readonly Sample[] history = new Sample[16];
        int first, count;
        public double X { get; private set; }
        public double Y { get; private set; }
        public double SourceX { get; private set; }
        public double SourceY { get; private set; }
        public double VelocityX { get; private set; }
        public double VelocityY { get; private set; }
        public double Age { get; private set; }
        public int Merged { get; private set; }
        public double LargestGap { get; private set; }
        public int GapsOver50 { get; private set; }
        public int GapsOver150 { get; private set; }
        double queuedX, queuedY, queuedAt, receivedAt, offsetX, offsetY;
        bool pending, snap, initialized;

        public void Reset()
        {
            pending = initialized = snap = false;
            VelocityX = VelocityY = offsetX = offsetY = Age = 0;
            first = count = 0;
            ClearStatistics();
        }

        public void ClearStatistics()
        { LargestGap = 0; Merged = GapsOver50 = GapsOver150 = 0; }

        public void Push(double x, double y, double now, bool teleport = false)
        {
            if (!Finite(x) || !Finite(y) || !Finite(now) ||
                (initialized && now < receivedAt) || (pending && now < queuedAt)) return;
            if (pending) Merged++;
            queuedX = x; queuedY = y; queuedAt = now;
            pending = true; snap |= teleport;
        }

        public bool Advance(double now, double delta)
        {
            bool recovering = false;
            if (pending)
            {
                var gap = initialized ? queuedAt - receivedAt : 0;
                var dx = queuedX - SourceX; var dy = queuedY - SourceY;
                if (gap > LargestGap) LargestGap = gap;
                if (gap > .050) GapsOver50++;
                if (gap > .150) GapsOver150++;
                // Actual teleports, reuse and very long absences must never chase the old room.
                if (!initialized || snap || dx * dx + dy * dy > 16 || gap > .5)
                {
                    X = queuedX; Y = queuedY; offsetX = offsetY = 0;
                    VelocityX = VelocityY = 0;
                    first = count = 0;
                }
                else if (gap > .150)
                {
                    // A brief game stall is not a teleport. Resume from the latest state,
                    // carrying a bounded visual correction instead of snapping in one frame.
                    offsetX = X - queuedX; offsetY = Y - queuedY;
                    VelocityX = VelocityY = 0;
                    first = count = 0;
                    recovering = true;
                }
                else
                {
                    if (gap >= 1.0 / 240.0)
                    {
                        var vx = dx / gap; var vy = dy / gap;
                        var speed = Math.Sqrt(vx * vx + vy * vy);
                        if (speed > 80) { vx *= 80 / speed; vy *= 80 / speed; }
                        VelocityX = Blend(VelocityX, vx);
                        VelocityY = Blend(VelocityY, vy);
                    }
                }
                SourceX = queuedX; SourceY = queuedY; receivedAt = queuedAt;
                Add(new Sample { X = queuedX, Y = queuedY, At = queuedAt });
                initialized = true; pending = snap = false;
            }
            if (!initialized) return false;
            Age = Math.Max(0, now - receivedAt);
            var playAt = now - BufferSeconds;
            while (count > 2 && history[(first + 1) % history.Length].At <= playAt)
            { first = (first + 1) % history.Length; count--; }
            double px = SourceX, py = SourceY;
            if (count >= 2 && playAt < receivedAt)
            {
                var a = history[first]; var b = history[(first + 1) % history.Length];
                var span = b.At - a.At;
                var blend = span > 0 ? Math.Max(0, Math.Min(1, (playAt - a.At) / span)) : 1;
                px = a.X + (b.X - a.X) * blend; py = a.Y + (b.Y - a.Y) * blend;
            }
            else
            {
                // Stop predicting after this cap and keep that endpoint, without sliding back.
                var predict = Math.Max(0, Math.Min(playAt - receivedAt, PredictionSeconds));
                px += VelocityX * predict; py += VelocityY * predict;
            }
            var decay = recovering ? 1 : Math.Exp(-Math.Max(0, delta) / .025);
            offsetX *= decay; offsetY *= decay;
            X = px + offsetX; Y = py + offsetY;
            return true;
        }

        void Add(Sample sample)
        {
            if (count == history.Length) { first = (first + 1) % history.Length; count--; }
            history[(first + count) % history.Length] = sample; count++;
        }

        static double Blend(double old, double value)
        {
            // Stops and direction changes must take effect immediately on each axis.
            if (Math.Abs(value) < .02 || old * value <= 0) return value;
            return old + (value - old) * .7;
        }
        static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
