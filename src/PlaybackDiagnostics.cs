using HarmonyLib;
using SSMP.Api.Client;

namespace Silksong.CoopAssist
{
    internal static class PlaybackDiagnostics
    {
        static readonly DispatchDelayMeter meter = new DispatchDelayMeter();
        internal static void Install()
        {
            var assembly = typeof(ClientAddon).Assembly;
            var h = new Harmony("silksong.coopassist.playbackmetrics");
            h.Patch(AccessTools.Method(assembly.GetType("SSMP.Networking.Packet.Data.PlayerUpdate",true),"ReadData"),
                postfix:new HarmonyMethod(typeof(PlaybackDiagnostics),nameof(AfterDecode)));
            h.Patch(AccessTools.Method(assembly.GetType("SSMP.Game.Client.ClientManager",true),"OnPlayerUpdate"),
                prefix:new HarmonyMethod(typeof(PlaybackDiagnostics),nameof(BeforeApply)));
        }
        static void AfterDecode(object __instance) => meter.Decoded(__instance,PlayerMotionRepairs.Now);
        static void BeforeApply(object __0) => NetworkDiagnostics.ClientApplied(__0,meter.Applied(__0,PlayerMotionRepairs.Now));
        internal static void Clear() { meter.Clear(); NetworkDiagnostics.Clear(); }
        internal static string Report()
        {
            var window=meter.ReadAndReset();
            return "[Playback] decoded-to-main-thread application: matched="+window.Count+", unmatched="+window.Missing+
                ", mean="+(window.Average*1000).ToString("0.0")+" ms, max="+(window.Maximum*1000).ToString("0")+
                " ms (local decode/dispatch/holding delay; not network RTT)";
        }
    }
}
