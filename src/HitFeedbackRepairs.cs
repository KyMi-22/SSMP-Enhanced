using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using SSMP.Api.Client;

namespace Silksong.CoopAssist
{
    internal static class HitFeedbackRepairs
    {
        // Keep native event IDs, duplicate suppression, visuals and initial send. Only shorten
        // the recovery interval when the first best-effort effect does not arrive in time.
        internal const float RetrySeconds = 1f / 30f;
        internal static void Install()
        {
            var assembly = typeof(ClientAddon).Assembly;
            var hit = assembly.GetType("SSMP.Animation.Effects.HeroHit", true);
            var h = new Harmony("silksong.coopassist.hitfeedback");
            foreach (var method in new[] { "OnTakenDamage", "WatchLocal" })
                h.Patch(AccessTools.Method(hit, method), transpiler: new HarmonyMethod(typeof(HitFeedbackRepairs), nameof(FasterRetry)));

            // Fix both lookup directions: outgoing enum selection AND remote enum -> clip.
            var animation = assembly.GetType("SSMP.Animation.AnimationManager", true);
            var lookup = AccessTools.Field(animation, "ClipEnumNames").GetValue(null);
            var normal = (IDictionary)AccessTools.Field(lookup.GetType(), "_normal").GetValue(lookup);
            var inverse = (IDictionary)AccessTools.Field(lookup.GetType(), "_inverse").GetValue(lookup);
            const string oldName = "Weakened StunEnd", realName = "Weakened Stun End";
            if (!normal.Contains(oldName) || normal.Contains(realName))
                throw new InvalidOperationException("Pinned SSMP stun clip mapping changed");
            var clip = normal[oldName];
            normal.Remove(oldName); normal.Add(realName, clip); inverse[clip] = realName;
        }

        static IEnumerable<CodeInstruction> FasterRetry(IEnumerable<CodeInstruction> input)
        {
            var code = new List<CodeInstruction>(input); var changed = 0;
            foreach (var instruction in code)
                if (instruction.opcode == OpCodes.Ldc_R4 && instruction.operand is float value && Math.Abs(value - .1f) < .000001f)
                { instruction.operand = RetrySeconds; changed++; }
            if (changed != 1) throw new InvalidOperationException("Expected one native hurt retry interval");
            return code;
        }
    }
}
