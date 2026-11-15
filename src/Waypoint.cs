using System;

namespace Silksong.CoopAssist
{
    public sealed class Waypoint
    {
        public string Scene = "", Gate = "";
        public float X, Y;

        public bool Valid => !string.IsNullOrWhiteSpace(Scene) && Scene.Length < 128 &&
            !string.IsNullOrWhiteSpace(Gate) && Gate.Length < 128 &&
            Math.Abs(X) < 100000 && Math.Abs(Y) < 100000;
    }

    // Exists only after a player requests travel. An unrelated load must never finish it.
    internal sealed class WaypointTrip
    {
        internal readonly string Save;
        internal readonly string Origin;
        internal readonly Waypoint Target;
        internal float Deadline { get; private set; }
        internal bool EnteringTarget { get; private set; }
        internal bool BenchExitSent;

        internal WaypointTrip(string save, string origin, Waypoint target, float now)
        {
            Save = save;
            Origin = origin;
            Target = target;
            Deadline = now + 6;
        }

        internal bool CanContinue(string save, string scene, float now, bool loading)
        {
            if (Save != save || now >= Deadline) return false;
            if (loading) return EnteringTarget;
            return scene == (EnteringTarget ? Target.Scene : Origin);
        }

        internal void BeginTransition(float now)
        {
            EnteringTarget = true;
            Deadline = now + 25;
        }
    }
}
