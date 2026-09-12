using HarmonyLib;
using UnityEngine;

namespace BoneAndEmber.Patches
{
    // Slice 4 of docs/design/002-waypoint-strip.md: grey out death pins you have
    // already collected.
    //
    // Postfix rather than a one-time write because Minimap.UpdatePins assigns
    // pin.m_iconElement.color unconditionally on every pass (Minimap.cs, "Color
    // color2 = ..."), so anything we set outside this would be overwritten within
    // a frame. Nothing here touches saved pin data; it is colour only, which is
    // why removal is opt-in and this is the default.
    [HarmonyPatch(typeof(Minimap), "UpdatePins")]
    internal static class MinimapPinDimPatch
    {
        private const float DimAlpha = 0.35f;
        private static readonly Color Dim = new Color(0.6f, 0.6f, 0.6f, DimAlpha);

        private static void Postfix(Minimap __instance)
        {
            if (__instance == null || __instance.m_pins == null) return;
            if (!Plugin.WaypointStripEnabled.Value || Plugin.RemoveRecoveredPins.Value) return;

            for (int i = 0; i < __instance.m_pins.Count; i++)
            {
                Minimap.PinData pin = __instance.m_pins[i];
                if (pin == null || pin.m_type != Minimap.PinType.Death) continue;
                if (pin.m_iconElement == null) continue;
                if (!RecoveredDeaths.IsRecovered(pin.m_pos)) continue;

                pin.m_iconElement.color = Dim;
                if (pin.m_NamePinData != null && pin.m_NamePinData.PinNameText != null)
                {
                    pin.m_NamePinData.PinNameText.color = Dim;
                }
            }
        }
    }
}
