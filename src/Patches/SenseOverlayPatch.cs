using HarmonyLib;

namespace BoneAndEmber.Patches
{
    // Attaches the sense overlay (docs/design/006-sense.md) to the vanilla HUD.
    // Separate from HudAwakePatch and CompassStripPatch so 006 can be toggled and
    // debugged on its own; more than one postfix on the same method is fine.
    [HarmonyPatch(typeof(Hud), "Awake")]
    internal static class SenseOverlayPatch
    {
        private static void Postfix(Hud __instance)
        {
            if (__instance == null || __instance.GetComponent<SenseOverlay>() != null) return;

            __instance.gameObject.AddComponent<SenseOverlay>();
            Plugin.Log.LogInfo("Sense overlay attached");
        }
    }
}
