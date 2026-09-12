using HarmonyLib;

namespace BoneAndEmber.Patches
{
    // Attaches our HUD to the vanilla one. Postfix, because we clone vanilla's own
    // bar and it has to exist first.
    [HarmonyPatch(typeof(Hud), "Awake")]
    internal static class HudAwakePatch
    {
        private static void Postfix(Hud __instance)
        {
            if (__instance == null || __instance.GetComponent<AlwaysOnHud>() != null) return;

            __instance.gameObject.AddComponent<AlwaysOnHud>();
            Plugin.Log.LogInfo("Always-on HUD attached");
        }
    }
}
