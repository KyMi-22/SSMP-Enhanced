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

        internal static void Tick(bool wanted)
        {
            if (wanted == shown) return;
            shown = wanted;
            try
            {
                if (InputHandler.Instance != null && SetCursor != null)
                    SetCursor.Invoke(InputHandler.Instance, new object[] { wanted });
            }
            catch (Exception) { }
        }
    }
}
