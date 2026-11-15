using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using SSMP.Api.Client;
using UnityEngine;

namespace Silksong.CoopAssist
{
    internal static class PlayerMotionRepairs
    {
        sealed class Replica
        {
            internal readonly PlayerMotionFilter Filter = new PlayerMotionFilter();
            internal bool IsPlayer;
        }
        static readonly ConditionalWeakTable<Stopwatch, PositionCadence> sendClocks = new ConditionalWeakTable<Stopwatch, PositionCadence>();
        static readonly ConditionalWeakTable<Component, Replica> replicas = new ConditionalWeakTable<Component, Replica>();
        static readonly MethodInfo Elapsed = AccessTools.PropertyGetter(typeof(Stopwatch), "ElapsedMilliseconds");
        static readonly MethodInfo Gate = AccessTools.Method(typeof(PlayerMotionRepairs), nameof(SampleElapsed));
        static readonly MethodInfo Heartbeat = AccessTools.Method(typeof(PlayerMotionRepairs), nameof(HeartbeatElapsed));
        static long sampleGates, received, merged, gapsOver50, gapsOver150;
        static double largestGap;
        internal static double Now => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

        internal static void Install()
        {
            var assembly = typeof(ClientAddon).Assembly;
            var h = new Harmony("silksong.coopassist.playermotion");
            h.Patch(AccessTools.Method(assembly.GetType("SSMP.Game.Client.ClientManager", true), "OnHeroControllerUpdate"),
                transpiler: new HarmonyMethod(typeof(PlayerMotionRepairs), nameof(SamplingGate)));
            var interpolation = assembly.GetType("SSMP.Fsm.PredictiveInterpolation", true);
            h.Patch(AccessTools.Method(interpolation, "SetNewState"), prefix: new HarmonyMethod(typeof(PlayerMotionRepairs), nameof(BeforePosition)));
            h.Patch(AccessTools.Method(interpolation, "ManualUpdate"), prefix: new HarmonyMethod(typeof(PlayerMotionRepairs), nameof(BeforeUpdate)));
            h.Patch(AccessTools.Method(interpolation, "OnEnable"), postfix: new HarmonyMethod(typeof(PlayerMotionRepairs), nameof(AfterEnable)));
            h.Patch(AccessTools.Method(interpolation, "SetPredictionEnabled"), postfix: new HarmonyMethod(typeof(PlayerMotionRepairs), nameof(AfterEnable)));
        }

        static IEnumerable<CodeInstruction> SamplingGate(IEnumerable<CodeInstruction> input)
        {
            var code = new List<CodeInstruction>(input);
            var count = 0;
            foreach (var instruction in code)
                if (instruction.Calls(Elapsed)) count++;
            if (count != 2) throw new InvalidOperationException("Position sampling target changed; expected two stopwatch reads");
            // Repeat stationary coordinates at the same cadence: otherwise a final moving
            // sample keeps being predicted until the native 250 ms heartbeat says it stopped.
            // Both replacements keep the stack signature Stopwatch -> Int64.
            var index = 0;
            foreach (var instruction in code)
                if (instruction.Calls(Elapsed))
                { instruction.opcode = OpCodes.Call; instruction.operand = index++ == 0 ? Gate : Heartbeat; }
            return code;
        }

        static long HeartbeatElapsed(Stopwatch clock) => 251;

        static long SampleElapsed(Stopwatch clock)
        {
            if (!sendClocks.GetValue(clock, _ => new PositionCadence()).Due(Now)) return 0;
            sampleGates++;
            return 17; // The native test is greater than the integer constant 16.
        }

        static Replica State(Component component) => replicas.GetValue(component, c => {
            var name = c.gameObject.name;
            return new Replica { IsPlayer = name == "Player Container" || name.StartsWith("Player Container ", StringComparison.Ordinal) };
        });

        static void AfterEnable(Component __instance)
        {
            if (replicas.TryGetValue(__instance, out var state)) state.Filter.Reset();
        }

        static bool BeforePosition(Component __instance, Vector3 __0, uint __2, bool __3, bool ____predictionDisabled,
            ref uint ____lastServerSequenceId, ref Vector3 ____lastServerPosition,
            ref float ____lastUpdateTime, ref bool ____isInitialized, ref bool ____hasNewServerData)
        {
            var state = State(__instance);
            if (!state.IsPlayer) return true;
            if (!RuntimeRepairs.LowLatency || ____predictionDisabled) { state.Filter.Reset(); return true; }
            // Preserve native sequence safety, including unsigned wrap. A postfix used to
            // re-accept stale states that native code had rejected, and native ForceSnap could
            // write the transform before our renderer corrected it again in the same frame.
            if (__2 != 0)
            {
                if (unchecked((int)(__2 - ____lastServerSequenceId)) <= 0) return false;
                ____lastServerSequenceId = __2;
            }
            state.Filter.Push(__0.x, __0.y, Now, __3);
            ____lastServerPosition = __0; ____lastUpdateTime = Time.time;
            ____isInitialized = ____hasNewServerData = true;
            received++;
            return false;
        }

        static bool BeforeUpdate(Component __instance, float __0, bool ____predictionDisabled,
            Transform ____cachedTransform, ref Vector3 ____velocity, ref Vector3 ____visualOffset,
            ref Vector3 ____visualOffsetVelocity, ref Vector3 ____logicalPosition, ref float ____timeSinceLastPacket)
        {
            if (!replicas.TryGetValue(__instance, out var state) || !state.IsPlayer) return true;
            if (!RuntimeRepairs.LowLatency || ____predictionDisabled)
            { state.Filter.Reset(); return true; }
            var filter = state.Filter;
            if (!filter.Advance(Now, __0)) return true;
            largestGap = Math.Max(largestGap, filter.LargestGap);
            merged += filter.Merged; gapsOver50 += filter.GapsOver50; gapsOver150 += filter.GapsOver150;
            filter.ClearStatistics();
            var z = ____cachedTransform.position.z;
            ____cachedTransform.position = new Vector3((float)filter.X, (float)filter.Y, z);
            // Keep the native read-only velocity and base position useful to its other
            // systems. Scripted lift rides retain ownership via _predictionDisabled.
            ____velocity = new Vector3((float)filter.VelocityX, (float)filter.VelocityY, 0);
            ____logicalPosition = new Vector3((float)filter.SourceX, (float)filter.SourceY, z);
            ____visualOffset = ____visualOffsetVelocity = Vector3.zero;
            ____timeSinceLastPacket = (float)filter.Age;
            return false;
        }

        internal static string Report()
        {
            var text = "[Motion] position sampling gates=" + sampleGates + ", received=" + received +
                ", largest observed position gap=" + (largestGap * 1000).ToString("0") + " ms; player filter=" + RuntimeRepairs.LowLatency;
            text += "; window-only gaps >50/150ms=" + gapsOver50 + "/" + gapsOver150 + ", merged=" + merged;
            sampleGates = received = merged = gapsOver50 = gapsOver150 = 0; largestGap = 0;
            return text;
        }
    }
}
