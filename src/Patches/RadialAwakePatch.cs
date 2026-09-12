using HarmonyLib;
using Valheim.UI;

namespace BoneAndEmber.Patches
{
    // Attaches our hover-fade driver to the radial. Postfix, because
    // RadialMotion reads the RadialBase component on its own GameObject.
    [HarmonyPatch(typeof(RadialBase), "Awake")]
    internal static class RadialAwakePatch
    {
        private static void Postfix(RadialBase __instance)
        {
            if (__instance == null || __instance.GetComponent<RadialMotion>() != null) return;

            __instance.gameObject.AddComponent<RadialMotion>();
            Plugin.Log.LogInfo("radial: motion driver attached");
        }
    }
}
