using HarmonyLib;

namespace BoneAndEmber.Patches
{
    // Attaches the compass strip (docs/design/002-waypoint-strip.md) to the vanilla
    // HUD. Separate from HudAwakePatch so 002 can be toggled and debugged without
    // touching 001; two postfixes on the same method is fine.
    [HarmonyPatch(typeof(Hud), "Awake")]
    internal static class CompassStripPatch
    {
        private static void Postfix(Hud __instance)
        {
            if (__instance == null || __instance.GetComponent<CompassStrip>() != null) return;

            __instance.gameObject.AddComponent<CompassStrip>();
            Plugin.Log.LogInfo("Compass strip attached");
        }
    }
}
