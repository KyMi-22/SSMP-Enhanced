using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Silksong.CoopAssist
{
    // The game hides the pointer during play. Any of the mod's own windows, or DebugMod's panels, need it back.
    internal static class PointerControl
    {
        static readonly MethodInfo SetCursor =
            AccessTools.Method(typeof(InputHandler), "SetCursorVisible", new[] { typeof(bool) });
        static bool shown;
        static bool debugProbed;
        static object debugSettings;
        static FieldInfo[] debugPanels;

        // DebugMod keeps its panel visibility on one settings object; read it instead of patching its UI.
        internal static bool DebugMenuOpen
        {
            get
            {
                try
                {
                    if (!debugProbed)
                    {
                        debugProbed = true;
                        var type = AccessTools.TypeByName("DebugMod.DebugMod");
                        var property = type?.GetProperty("settings", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                        debugSettings = property?.GetValue(null);
                        if (debugSettings != null)
                            debugPanels = debugSettings.GetType()
                                .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                                .Where(field => field.FieldType == typeof(bool) && field.Name.EndsWith("Visible", StringComparison.Ordinal))
                                .ToArray();
                    }
                    if (debugSettings == null || debugPanels == null) return false;
                    foreach (var field in debugPanels)
                        if (field.GetValue(debugSettings) is bool visible && visible) return true;
                }
                catch (Exception) { debugProbed = true; debugPanels = null; }
                return false;
            }
        }

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
