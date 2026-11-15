using System;

namespace Silksong.CoopAssist
{
    // A join is a one-shot operation, never an instruction to chase a moving host forever.
    public sealed class ArrivalPolicy
    {
        public bool Pending { get; private set; }
        public bool Moving { get; private set; }
        public string Destination { get; private set; } = "";
        public float Deadline { get; private set; }
        public void Reset() { Pending = true; Moving = false; Destination = ""; Deadline = 0; }
        public void Cancel() { Pending = false; Moving = false; Destination = ""; }
        public void Begin(string scene, float now) { Moving = true; Destination = scene; Deadline = now + 25; }
        public bool Expired(float now) => Moving && now > Deadline;
        public static bool ValidAnchor(string scene, string gate, float x, float y)
        {
            return !string.IsNullOrWhiteSpace(scene) && scene.Length < 128 &&
                !string.IsNullOrWhiteSpace(gate) && gate.Length < 128 &&
                !float.IsNaN(x) && !float.IsNaN(y) && !float.IsInfinity(x) && !float.IsInfinity(y) &&
                Math.Abs(x) < 100000 && Math.Abs(y) < 100000;
        }
        public static bool CanArrive(bool pending, bool connected, bool hosting, bool ready,
            bool down, bool pairedWithHost, float anchorAge)
        {
            return pending && connected && !hosting && ready && !down && pairedWithHost &&
                anchorAge >= 0 && anchorAge <= 3;
        }
        public static bool CanWatch(bool connected, bool down, bool screenBack, bool loading,
            bool sameScene, bool teammateDown)
        {
            return connected && down && screenBack && !loading && sameScene && !teammateDown;
        }
    }
}
