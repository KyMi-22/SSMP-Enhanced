using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Silksong.CoopAssist
{
    // The game hides the pointer during play; the mod's own windows need it, and hand it back when closed.
    // Kept out of TeamControls.cs because the key-binding tests compile that file without Harmony.
    internal static class PointerControl
    {
        static readonly MethodInfo SetCursor =
            AccessTools.Method(typeof(InputHandler), "SetCursorVisible", new[] { typeof(bool) });
        static bool shown;
        static float nextAssert;

        internal static void Tick(bool wanted)
        {
            if (InputHandler.Instance == null || SetCursor == null) return;
            if (!wanted)
            {
                if (!shown) return;
                shown = false;
                // A native menu owns the pointer while paused; do not hide it behind the pause screen.
                if (global::GameManager.instance != null &&
                    (global::GameManager.instance.isPaused || global::GameManager.instance.IsNonGameplayScene())) return;
                Invoke(false);
                return;
            }
            // Re-assert at a low rate: the game may have hidden the pointer without going through our prefix.
            if (shown && Time.unscaledTime < nextAssert) return;
            nextAssert = Time.unscaledTime + .5f;
            if (Invoke(true)) shown = true;
        }

        static bool Invoke(bool show)
        {
            try { SetCursor.Invoke(InputHandler.Instance, new object[] { show }); return true; }
            catch (Exception) { return false; }
        }
    }
}