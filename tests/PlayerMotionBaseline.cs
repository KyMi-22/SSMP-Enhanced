// Frozen 0.2.2 receive filter for behavioral comparisons under identical delivery traces.
using System;

namespace PreviousRelease
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

    // The native packet sequence counts all traffic, including unrelated events. It is not
    // the number of movement samples. Use elapsed receive time and merge positions dispatched
    // in one render frame before estimating speed. No changes to the network protocol.
    public sealed class PlayerMotionFilter
    {
        public double X { get; private set; }
        public double Y { get; private set; }
        public double SourceX { get; private set; }
        public double SourceY { get; private set; }
        public double VelocityX { get; private set; }
        public double VelocityY { get; private set; }
        public double Age { get; private set; }
        public int Merged { get; private set; }
        public double LargestGap { get; private set; }
        double queuedX, queuedY, queuedAt, receivedAt, offsetX, offsetY;
        bool pending, snap, initialized;

        public void Reset()
        {
            pending = initialized = snap = false;
            VelocityX = VelocityY = offsetX = offsetY = Age = 0;
        }

        public void Push(double x, double y, double now, bool teleport = false)
        {
            if (!Finite(x) || !Finite(y) || !Finite(now)) return;
            if (pending) Merged++;
            queuedX = x; queuedY = y; queuedAt = now;
            pending = true; snap |= teleport;
        }

        public bool Advance(double now, double delta)
        {
            if (pending)
            {
                var gap = initialized ? queuedAt - receivedAt : 0;
                var dx = queuedX - SourceX; var dy = queuedY - SourceY;
                if (gap > LargestGap) LargestGap = gap;
                // First entry, a scripted teleport, a long silence, or a large displacement
                // starts from the received position instead of chasing an obsolete position.
                if (!initialized || snap || dx * dx + dy * dy > 16 || gap > .15)
                {
                    X = queuedX; Y = queuedY; offsetX = offsetY = 0;
                    VelocityX = VelocityY = 0;
                }
                else
                {
                    offsetX = X - queuedX; offsetY = Y - queuedY;
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
                initialized = true; pending = snap = false;
            }
            if (!initialized) return false;
            Age = Math.Max(0, now - receivedAt);
            // Extrapolate for at most two ordinary ticks, then hold that endpoint. Decaying
            // velocity while recalculating from the old position makes the avatar slide back.
            var predict = Math.Min(Age, .035);
            var decay = Math.Exp(-Math.Max(0, delta) / .008);
            offsetX *= decay; offsetY *= decay;
            X = SourceX + VelocityX * predict + offsetX;
            Y = SourceY + VelocityY * predict + offsetY;
            return true;
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

