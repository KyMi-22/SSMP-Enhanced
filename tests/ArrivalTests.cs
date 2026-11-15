using System;
using Silksong.CoopAssist;

static class ArrivalTests
{
    static int count;
    static void Check(bool value, string name) { if (!value) throw new Exception(name); count++; }
    static void Main()
    {
        Check(ArrivalPolicy.ValidAnchor("Room_A", "left1", 1, 2), "valid anchor");
        Check(!ArrivalPolicy.ValidAnchor("", "left1", 1, 2), "empty room");
        Check(!ArrivalPolicy.ValidAnchor("Room_A", "", 1, 2), "no invented spawn gate");
        Check(!ArrivalPolicy.ValidAnchor("Room_A", "left1", float.NaN, 2), "NaN rejected");
        Check(!ArrivalPolicy.ValidAnchor("Room_A", "left1", 1, float.PositiveInfinity), "infinity rejected");
        Check(!ArrivalPolicy.ValidAnchor(new string('x', 128), "left1", 1, 2), "room length");
        Check(!ArrivalPolicy.ValidAnchor("Room_A", "left1", -100001, 2), "coordinates bounded");
        for (int bits = 0; bits < 128; bits++)
        {
            bool pending = (bits & 1) != 0, connected = (bits & 2) != 0, host = (bits & 4) != 0;
            bool ready = (bits & 8) != 0, down = (bits & 16) != 0, paired = (bits & 32) != 0;
            float age = (bits & 64) == 0 ? 1 : 4;
            var allowed = ArrivalPolicy.CanArrive(pending, connected, host, ready, down, paired, age);
            if (allowed) Check(pending && connected && !host && ready && !down && paired && age == 1, "arrival invariant " + bits);
            else Check(!pending || !connected || host || !ready || down || !paired || age == 4, "arrival exclusion " + bits);
        }
        Check(!ArrivalPolicy.CanArrive(true, true, false, true, false, true, -1), "future arrival");
        for (int bits = 0; bits < 64; bits++)
        {
            bool connected = (bits & 1) != 0, down = (bits & 2) != 0, back = (bits & 4) != 0;
            bool loading = (bits & 8) != 0, same = (bits & 16) != 0, otherDown = (bits & 32) != 0;
            var result = ArrivalPolicy.CanWatch(connected, down, back, loading, same, otherDown);
            Check(result == (bits == (1 | 2 | 4 | 16)), "spectator safety " + bits);
        }
        var state = new ArrivalPolicy();
        state.Reset(); Check(state.Pending && !state.Moving, "fresh join");
        state.Begin("BossRoom", 10); Check(state.Moving && state.Destination == "BossRoom", "track accepted transfer");
        Check(!state.Expired(35) && state.Expired(36), "bounded wait");
        state.Cancel(); Check(!state.Pending && !state.Moving, "cancel without input changes");
        state.Reset(); Check(state.Destination == "" && !state.Moving, "reconnection clears destination");
        Console.WriteLine("Passed " + count + " policy checks. Unity, transport and game behaviour not executed.");
    }
}
