using HarmonyLib;
using UnityEngine;

namespace BoneAndEmber.Patches
{
    // Vanilla's RadialOverlapPreventer rescales the radial (its Big/Small
    // setting, shrunk further if it would overlap the side tooltip panel) in
    // both OnEnable and Start. Start fires once, after our ConstructRadial
    // postfix has already applied Radial.Scale, so the very first open came
    // out at vanilla's size and every later open at ours: "big, then small
    // after I use it". With the side panel hidden there is nothing to avoid
    // overlapping, so this resets vanilla's scale to one right after it's
    // written and leaves Radial.Scale as the single size knob.
    [HarmonyPatch(typeof(RadialOverlapPreventer), "PreventOverlap")]
    internal static class RadialOverlapPreventerPatch
    {
        private static bool Prepare()
        {
            if (AccessTools.Method(typeof(RadialOverlapPreventer), "PreventOverlap") != null) return true;

            Plugin.Log.LogError(
                "RadialOverlapPreventer.PreventOverlap not found, the radial may still change size on its first open. " +
                "Re-run tools/decompile.sh and check what it is called now.");
            return false;
        }

        private static void Postfix(RectTransform ___m_elementInfoElement)
        {
            if (!Plugin.RadialEnabled.Value || ___m_elementInfoElement == null) return;
            if (___m_elementInfoElement.localScale == Vector3.one) return;

            Plugin.Log.LogInfo("radial: vanilla overlap preventer set " + HudPath.Of(___m_elementInfoElement) +
                " scale to " + ___m_elementInfoElement.localScale.x.ToString("F2") + ", resetting to 1 so Radial.Scale is the only size.");
            ___m_elementInfoElement.localScale = Vector3.one;
        }
    }
}
