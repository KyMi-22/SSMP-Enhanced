using Newtonsoft.Json.Linq;

namespace Silksong.CoopAssist
{
    internal static class GrandGateProgress
    {
        internal const string Defeated = "defeatedLastJudge";
        internal const string GateOpened = "SeenLastJudgeGateOpen";
        internal const string GateVisited = "visitedGrandGate";
        internal const string ActStarted = "act2Started";
        internal static readonly string[] StoryRecords = { Defeated, GateOpened, GateVisited, ActStarted };

        internal static void Align(JObject host, JObject guest, JObject target)
        {
            // Keep a completed gate when both saves have already beaten the Judge.
            // An ahead guest cannot use this to skip an uncleared host's fight.
            bool guestFinished = IsSet(host, Defeated) && IsSet(guest, Defeated) && IsSet(guest, GateOpened);
            target[GateOpened] = IsSet(host, GateOpened) || guestFinished;
            target[GateVisited] = IsSet(host, GateVisited) || (guestFinished && IsSet(guest, GateVisited));
            if (guestFinished && IsSet(guest, ActStarted)) target[ActStarted] = true;
        }

        static bool IsSet(JObject data, string field) =>
            data[field] is JValue value && value.Type == JTokenType.Boolean && (bool)value;
    }
}
